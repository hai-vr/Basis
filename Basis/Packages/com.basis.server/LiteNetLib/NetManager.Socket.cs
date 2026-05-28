#if UNITY_2018_3_OR_NEWER
#define UNITY_SOCKET_FIX
#endif
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    public partial class NetManager
    {
        private Socket _udpSocketv4;
        private Socket _udpSocketv6;
        private Thread _receiveThread;
        private IPEndPoint _bufferEndPointv4;
        private IPEndPoint _bufferEndPointv6;
        private EndPoint _receiveEndPoint4 = new IPEndPoint(IPAddress.Any, 0);
        private EndPoint _receiveEndPoint6 = new IPEndPoint(IPAddress.IPv6Any, 0);

        // Extra SO_REUSEPORT sockets + their receive threads, populated when MultiSocketCount > 1 on Linux.
        private readonly List<Socket> _extraSocketsV4 = new List<Socket>();
        private readonly List<Socket> _extraSocketsV6 = new List<Socket>();
        private readonly List<Thread> _extraReceiveThreads = new List<Thread>();
        private const int SO_REUSEPORT_LINUX = 15;
        private bool _useReusePort;
#if UNITY_SOCKET_FIX
        private PausedSocketFix _pausedSocketFix;
        private bool _useSocketFix;
#endif

#if NET8_0_OR_GREATER
        // ThreadStatic so each receive thread (primary + SO_REUSEPORT extras) gets its own
        // SocketAddress scratch — Socket.ReceiveFrom writes the source address into this buffer,
        // so a shared instance across threads would race.
        [ThreadStatic] private static SocketAddress _sockAddrCacheV4Ts;
        [ThreadStatic] private static SocketAddress _sockAddrCacheV6Ts;

        private static SocketAddress GetSockAddrCache(AddressFamily af)
        {
            if (af == AddressFamily.InterNetwork)
            {
                if (_sockAddrCacheV4Ts == null) _sockAddrCacheV4Ts = new SocketAddress(AddressFamily.InterNetwork);
                return _sockAddrCacheV4Ts;
            }
            if (_sockAddrCacheV6Ts == null) _sockAddrCacheV6Ts = new SocketAddress(AddressFamily.InterNetworkV6);
            return _sockAddrCacheV6Ts;
        }
#endif

        private const int SioUdpConnreset = -1744830452; //SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12
        private static readonly IPAddress MulticastAddressV6 = IPAddress.Parse("ff02::1");
        public static readonly bool IPv6Support;

        // special case in iOS (and possibly android that should be resolved in unity)
        internal bool NotConnected;

        /// <summary>
        /// Poll timeout in microseconds. Increasing can slightly increase performance in cost of slow NetManager.Stop(Socket.Close)
        /// </summary>
        public int ReceivePollingTime = 50000; //0.05 second

        public short Ttl
        {
            get
            {
#if UNITY_SWITCH
                return 0;
#else
                return _udpSocketv4.Ttl;
#endif
            }
            internal set
            {
#if !UNITY_SWITCH
                _udpSocketv4.Ttl = value;
#endif
            }
        }

        static NetManager()
        {
#if DISABLE_IPV6
            IPv6Support = false;
#elif !UNITY_2019_1_OR_NEWER && !UNITY_2018_4_OR_NEWER && (!UNITY_EDITOR && ENABLE_IL2CPP)
            string version = UnityEngine.Application.unityVersion;
            IPv6Support = Socket.OSSupportsIPv6 && int.Parse(version.Remove(version.IndexOf('f')).Split('.')[2]) >= 6;
#else
            IPv6Support = Socket.OSSupportsIPv6;
#endif
        }

        private bool ProcessError(SocketException ex)
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.NotConnected:
                    NotConnected = true;
                    return true;
                case SocketError.Interrupted:
                case SocketError.NotSocket:
                case SocketError.OperationAborted:
                    return true;
                case SocketError.ConnectionReset:
                case SocketError.MessageSize:
                case SocketError.TimedOut:
                case SocketError.NetworkReset:
                case SocketError.WouldBlock:
                    ////NetDebug.Write($"[R]Ignored error: {(int)ex.SocketErrorCode} - {ex}");
                    break;
                default:
                    NetDebug.WriteError($"[R]Error code: {(int)ex.SocketErrorCode} - {ex}");
                    CreateEvent(NetEvent.EType.Error, errorCode: ex.SocketErrorCode);
                    break;
            }
            return false;
        }

        private void ManualReceive(Socket socket, EndPoint bufferEndPoint, int maxReceive)
        {
            //Reading data
            try
            {
                int packetsReceived = 0;
                while (socket.Available > 0)
                {
                    ReceiveFrom(socket, ref bufferEndPoint);
                    packetsReceived++;
                    if (packetsReceived == maxReceive)
                        break;
                }
            }
            catch (SocketException ex)
            {
                ProcessError(ex);
            }
            catch (ObjectDisposedException)
            {

            }
            catch (Exception e)
            {
                //protects socket receive thread
                NetDebug.WriteError("[NM] SocketReceiveThread error: " + e);
            }
        }

        private void NativeReceiveLogic() => NativeReceiveLogicForSocketPair(_udpSocketv4, _udpSocketv6);

        private void NativeReceiveLogicForSocketPair(Socket socketv4, Socket socketV6)
        {
            IntPtr socketHandle4 = socketv4.Handle;
            IntPtr socketHandle6 = socketV6?.Handle ?? IntPtr.Zero;
            byte[] addrBuffer4 = new byte[NativeSocket.IPv4AddrSize];
            byte[] addrBuffer6 = new byte[NativeSocket.IPv6AddrSize];
            var tempEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var selectReadList = new List<Socket>(2);
            var packet = PoolGetPacket(NetConstants.MaxPacketSize);

            while (_isRunning)
            {
                try
                {
                    if (socketV6 == null)
                    {
                        if (NativeReceiveFrom(socketHandle4, addrBuffer4) == false)
                            return;
                        continue;
                    }
                    bool messageReceived = false;
                    if (socketv4.Available != 0 || selectReadList.Contains(socketv4))
                    {
                        if (NativeReceiveFrom(socketHandle4, addrBuffer4) == false)
                            return;
                        messageReceived = true;
                    }
                    if (socketV6.Available != 0 || selectReadList.Contains(socketV6))
                    {
                        if (NativeReceiveFrom(socketHandle6, addrBuffer6) == false)
                            return;
                        messageReceived = true;
                    }

                    selectReadList.Clear();

                    if (messageReceived)
                        continue;

                    selectReadList.Add(socketv4);
                    selectReadList.Add(socketV6);

                    Socket.Select(selectReadList, null, null, ReceivePollingTime);
                }
                catch (SocketException ex)
                {
                    if (ProcessError(ex))
                        return;
                }
                catch (ObjectDisposedException)
                {
                    //socket closed
                    return;
                }
                catch (ThreadAbortException)
                {
                    //thread closed
                    return;
                }
                catch (Exception e)
                {
                    //protects socket receive thread
                    NetDebug.WriteError("[NM] SocketReceiveThread error: " + e);
                }
            }

            bool NativeReceiveFrom(IntPtr s, byte[] address)
            {
                int addrSize = address.Length;
                packet.Size = NativeSocket.RecvFrom(s, packet.RawData, NetConstants.MaxPacketSize, address, ref addrSize);
                if (packet.Size == 0)
                    return true; //socket closed or empty packet

                if (packet.Size == -1)
                {
                    //Linux timeout EAGAIN
                    return ProcessError(new SocketException((int)NativeSocket.GetSocketError())) == false;
                }

                ////NetDebug.WriteForce($"[R]Received data from {endPoint}, result: {packet.Size}");
                //refresh temp Addr/Port
                short family = (short)((address[1] << 8) | address[0]);
                tempEndPoint.Port = (ushort)((address[2] << 8) | address[3]);
                if ((NativeSocket.UnixMode && family == NativeSocket.AF_INET6) || (!NativeSocket.UnixMode && (AddressFamily)family == AddressFamily.InterNetworkV6))
                {
                    uint scope = unchecked((uint)(
                        (address[27] << 24) +
                        (address[26] << 16) +
                        (address[25] << 8) +
                        (address[24])));
#if NETCOREAPP || NETSTANDARD2_1 || NETSTANDARD2_1_OR_GREATER
                    tempEndPoint.Address = new IPAddress(new ReadOnlySpan<byte>(address, 8, 16), scope);
#else
                    byte[] addrBuffer = new byte[16];
                    Buffer.BlockCopy(address, 8, addrBuffer, 0, 16);
                    tempEndPoint.Address = new IPAddress(addrBuffer, scope);
#endif
                }
                else //IPv4
                {
                    long ipv4Addr = unchecked((uint)((address[4] & 0x000000FF) |
                                                     (address[5] << 8 & 0x0000FF00) |
                                                     (address[6] << 16 & 0x00FF0000) |
                                                     (address[7] << 24)));
                    tempEndPoint.Address = new IPAddress(ipv4Addr);
                }

                if (TryGetPeer(tempEndPoint, out var peer))
                {
                    //use cached native ep
                    OnMessageReceived(packet, peer);
                }
                else
                {
                    OnMessageReceived(packet, tempEndPoint);
                    tempEndPoint = new IPEndPoint(IPAddress.Any, 0);
                }
                packet = PoolGetPacket(NetConstants.MaxPacketSize);
                return true;
            }
        }

        private void ReceiveFrom(Socket s, ref EndPoint bufferEndPoint)
        {
            var packet = PoolGetPacket(NetConstants.MaxPacketSize);
#if NET8_0_OR_GREATER
            var sockAddr = GetSockAddrCache(s.AddressFamily);
            packet.Size = s.ReceiveFrom(packet, SocketFlags.None, sockAddr);
            OnMessageReceived(packet, TryGetPeer(sockAddr, out var peer) ? peer : (IPEndPoint)bufferEndPoint.Create(sockAddr));
#else
            packet.Size = s.ReceiveFrom(packet.RawData, 0, NetConstants.MaxPacketSize, SocketFlags.None, ref bufferEndPoint);
            OnMessageReceived(packet, (IPEndPoint)bufferEndPoint);
#endif
        }

        private void ReceiveLogic() => ReceiveLogicForSocketPair(_udpSocketv4, _udpSocketv6);

        private void ReceiveLogicForSocketPair(Socket socketv4, Socket socketV6)
        {
            // Per-thread endpoint instances so multi-socket extras don't alias the primary
            // socket's templates. Socket.ReceiveFrom replaces the ref rather than mutating
            // the original, but allocating per-thread keeps this fact local to each loop.
            EndPoint bufferEndPoint4 = new IPEndPoint(IPAddress.Any, 0);
            EndPoint bufferEndPoint6 = new IPEndPoint(IPAddress.IPv6Any, 0);
            var selectReadList = new List<Socket>(2);

            while (_isRunning)
            {
                //Reading data
                try
                {
                    if (socketV6 == null)
                    {
                        if (socketv4.Available == 0 && !socketv4.Poll(ReceivePollingTime, SelectMode.SelectRead))
                            continue;
                        ReceiveFrom(socketv4, ref bufferEndPoint4);
                    }
                    else
                    {
                        bool messageReceived = false;
                        if (socketv4.Available != 0 || selectReadList.Contains(socketv4))
                        {
                            ReceiveFrom(socketv4, ref bufferEndPoint4);
                            messageReceived = true;
                        }
                        if (socketV6.Available != 0 || selectReadList.Contains(socketV6))
                        {
                            ReceiveFrom(socketV6, ref bufferEndPoint6);
                            messageReceived = true;
                        }

                        selectReadList.Clear();

                        if (messageReceived)
                            continue;

                        selectReadList.Add(socketv4);
                        selectReadList.Add(socketV6);
                        Socket.Select(selectReadList, null, null, ReceivePollingTime);
                    }
                    ////NetDebug.Write(NetLogLevel.Trace, $"[R]Received data from {bufferEndPoint}, result: {packet.Size}");
                }
                catch (SocketException ex)
                {
                    if (ProcessError(ex))
                        return;
                }
                catch (ObjectDisposedException)
                {
                    //socket closed
                    return;
                }
                catch (ThreadAbortException)
                {
                    //thread closed
                    return;
                }
                catch (Exception e)
                {
                    //protects socket receive thread
                    NetDebug.WriteError("[NM] SocketReceiveThread error: " + e);
                }
            }
        }

        /// <summary>
        /// Start logic thread and listening on selected port
        /// </summary>
        /// <param name="addressIPv4">bind to specific ipv4 address</param>
        /// <param name="addressIPv6">bind to specific ipv6 address</param>
        /// <param name="port">port to listen</param>
        /// <param name="manualMode">mode of library</param>
        public bool Start(IPAddress addressIPv4, IPAddress addressIPv6, int port, bool manualMode)
        {
            if (IsRunning && NotConnected == false)
                return false;

            NotConnected = false;
            _manualMode = manualMode;
            UseNativeSockets = UseNativeSockets && NativeSocket.IsSupported;

            // SO_REUSEPORT multi-socket ingress: Linux-only. Decide here so the primary socket
            // also opts in (SO_REUSEPORT must be set on every socket sharing the port).
            int extraSocketCount = 0;
            if (MultiSocketCount > 1)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    _useReusePort = true;
                    extraSocketCount = MultiSocketCount - 1;
                }
                else
                {
                    NetDebug.WriteError($"[NM] MultiSocketCount={MultiSocketCount} requested but SO_REUSEPORT is Linux-only; falling back to single socket");
                }
            }

            _udpSocketv4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            if (!BindSocket(_udpSocketv4, new IPEndPoint(addressIPv4, port)))
                return false;

            LocalPort = ((IPEndPoint)_udpSocketv4.LocalEndPoint).Port;

#if UNITY_SOCKET_FIX
            if (_useSocketFix && _pausedSocketFix == null)
                _pausedSocketFix = new PausedSocketFix(this, addressIPv4, addressIPv6, port, manualMode);
#endif

            _isRunning = true;
            if (_manualMode)
            {
                _bufferEndPointv4 = new IPEndPoint(IPAddress.Any, 0);
            }

            //Check IPv6 support
            if (IPv6Support && IPv6Enabled)
            {
                _udpSocketv6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                //Use one port for two sockets
                if (BindSocket(_udpSocketv6, new IPEndPoint(addressIPv6, LocalPort)))
                {
                    if (_manualMode)
                        _bufferEndPointv6 = new IPEndPoint(IPAddress.IPv6Any, 0);
                }
                else
                {
                    _udpSocketv6 = null;
                }
            }

            if (!manualMode)
            {
                ThreadStart ts = ReceiveLogic;
                if (UseNativeSockets)
                    ts = NativeReceiveLogic;
                _receiveThread = new Thread(ts)
                {
                    Name = $"ReceiveThread({LocalPort})",
                    IsBackground = true
                };
                _receiveThread.Start();
                if (_logicThread == null)
                {
                    _logicThread = new Thread(UpdateLogic) { Name = "LogicThread", IsBackground = true };
                    _logicThread.Start();
                }

                // Spawn extra SO_REUSEPORT sockets + receive threads. Each gets its own v4
                // (and v6 if enabled) socket bound to the same port. The Linux kernel hashes
                // inbound 4-tuples across them — per-peer order preserved, single-recv-thread
                // pps wall lifted.
                for (int i = 0; i < extraSocketCount; i++)
                {
                    var extraV4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    if (!BindSocket(extraV4, new IPEndPoint(addressIPv4, LocalPort)))
                    {
                        NetDebug.WriteError($"[NM] Extra REUSEPORT socket #{i + 1} (v4) bind failed; stopping at {i} extras");
                        extraV4.Close();
                        break;
                    }
                    _extraSocketsV4.Add(extraV4);

                    Socket extraV6 = null;
                    if (_udpSocketv6 != null)
                    {
                        extraV6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                        if (!BindSocket(extraV6, new IPEndPoint(addressIPv6, LocalPort)))
                        {
                            // v6 bind can fail with AddressAlreadyInUse on dual-stack hosts even with
                            // REUSEPORT — degrade to v4-only for this slot rather than aborting.
                            extraV6.Close();
                            extraV6 = null;
                        }
                        else
                        {
                            _extraSocketsV6.Add(extraV6);
                        }
                    }

                    Socket capturedV4 = extraV4;
                    Socket capturedV6 = extraV6;
                    Thread extraThread = new Thread(() =>
                    {
                        if (UseNativeSockets)
                            NativeReceiveLogicForSocketPair(capturedV4, capturedV6);
                        else
                            ReceiveLogicForSocketPair(capturedV4, capturedV6);
                    })
                    {
                        Name = $"ReceiveThread({LocalPort}#{i + 1})",
                        IsBackground = true
                    };
                    _extraReceiveThreads.Add(extraThread);
                    extraThread.Start();
                }
            }

            return true;
        }

        private bool BindSocket(Socket socket, IPEndPoint ep)
        {
            //Setup socket
            socket.ReceiveTimeout = 500;
            socket.SendTimeout = 500;
            socket.ReceiveBufferSize = NetConstants.SocketBufferSize;
            socket.SendBufferSize = NetConstants.SocketBufferSize;
            socket.Blocking = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    socket.IOControl(SioUdpConnreset, new byte[] { 0 }, null);
                }
                catch
                {
                    //ignored
                }
            }

            try
            {
                // SO_REUSEPORT needs ReuseAddress too on most BSDs / Linux for port sharing.
                bool wantReuse = ReuseAddress || _useReusePort;
                socket.ExclusiveAddressUse = !wantReuse;
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, wantReuse);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, DontRoute);
                if (_useReusePort && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try
                    {
                        // SO_REUSEPORT = 15 on Linux. Set MUST happen before bind. Enables the
                        // kernel to RSS-hash inbound 4-tuples across all sockets sharing the port,
                        // giving one receive thread per socket without app-level load balancing.
                        socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_REUSEPORT_LINUX, 1);
                    }
                    catch (SocketException ex)
                    {
                        NetDebug.WriteError($"[NM] SO_REUSEPORT setsockopt failed ({ex.SocketErrorCode}); multi-socket disabled");
                        _useReusePort = false;
                    }
                }
            }
            catch
            {
                //Unity with IL2CPP throws an exception here, it doesn't matter in most cases so just ignore it
            }
            if (ep.AddressFamily == AddressFamily.InterNetwork)
            {
                Ttl = NetConstants.SocketTTL;

                if (BroadcastReceiveEnabled)
                {
                    try { socket.EnableBroadcast = true; }
                    catch (SocketException e)
                    {
                        NetDebug.WriteError($"[B]Broadcast error: {e.SocketErrorCode}");
                    }
                }

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    try { socket.DontFragment = true; }
                    catch (SocketException e)
                    {
                        NetDebug.WriteError($"[B]DontFragment error: {e.SocketErrorCode}");
                    }
                }
            }
            //Bind
            try
            {
                socket.Bind(ep);
                //NetDebug.Write(NetLogLevel.Trace, $"[B]Successfully binded to port: {((IPEndPoint)socket.LocalEndPoint).Port}, AF: {socket.AddressFamily}");

                //join multicast (only needed when broadcast receive is enabled)
                if (BroadcastReceiveEnabled && ep.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    try
                    {
#if !UNITY_SOCKET_FIX
                        socket.SetSocketOption(
                            SocketOptionLevel.IPv6,
                            SocketOptionName.AddMembership,
                            new IPv6MulticastOption(MulticastAddressV6));
#endif
                    }
                    catch (Exception)
                    {
                        // Unity3d throws exception - ignored
                    }
                }
            }
            catch (SocketException bindException)
            {
                switch (bindException.SocketErrorCode)
                {
                    //IPv6 bind fix
                    case SocketError.AddressAlreadyInUse:
                        if (socket.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            try
                            {
                                //Set IPv6Only
                                socket.DualMode = false;
                                socket.Bind(ep);
                            }
                            catch (SocketException ex)
                            {
                                //because its fixed in 2018_3
                                NetDebug.WriteError($"[B]Bind exception: {ex}, errorCode: {ex.SocketErrorCode}");
                                return false;
                            }
                            return true;
                        }
                        break;
                    //hack for iOS (Unity3D)
                    case SocketError.AddressFamilyNotSupported:
                        return true;
                }
                NetDebug.WriteError($"[B]Bind exception: {bindException}, errorCode: {bindException.SocketErrorCode}");
                return false;
            }
            return true;
        }

        internal int SendRawAndRecycle(NetPacket packet, IPEndPoint remoteEndPoint)
        {
            int result = SendRaw(packet.RawData, 0, packet.Size, remoteEndPoint);
            PoolRecycle(packet);
            return result;
        }

        internal int SendRaw(NetPacket packet, IPEndPoint remoteEndPoint)
        {
            return SendRaw(packet.RawData, 0, packet.Size, remoteEndPoint);
        }

        internal int SendRaw(byte[] message, int start, int length, IPEndPoint remoteEndPoint)
        {
            if (!_isRunning)
                return 0;

            NetPacket expandedPacket = null;
            if (_extraPacketLayer != null)
            {
                expandedPacket = PoolGetPacket(length + _extraPacketLayer.ExtraPacketSizeForLayer);
                Buffer.BlockCopy(message, start, expandedPacket.RawData, 0, length);
                start = 0;
                _extraPacketLayer.ProcessOutBoundPacket(ref remoteEndPoint, ref expandedPacket.RawData, ref start, ref length);
                message = expandedPacket.RawData;
            }

            var socket = _udpSocketv4;
            if (remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 && IPv6Support)
            {
                socket = _udpSocketv6;
                if (socket == null)
                    return 0;
            }

            int result;
            try
            {
                if (UseNativeSockets && remoteEndPoint is NetPeer peer)
                {
                    unsafe
                    {
                        fixed (byte* dataWithOffset = &message[start])
                            result = NativeSocket.SendTo(socket.Handle, dataWithOffset, length, peer.NativeAddress, peer.NativeAddress.Length);
                    }
                    if (result == -1)
                        throw NativeSocket.GetSocketException();
                }
                else
                {
#if NET8_0_OR_GREATER
                    result = socket.SendTo(new ReadOnlySpan<byte>(message, start, length), SocketFlags.None, remoteEndPoint.Serialize());
#else
                    result = socket.SendTo(message, start, length, SocketFlags.None, remoteEndPoint);
#endif
                }
                ////NetDebug.WriteForce("[S]Send packet to {0}, result: {1}", remoteEndPoint, result);
            }
            catch (SocketException ex)
            {
                switch (ex.SocketErrorCode)
                {
                    case SocketError.NoBufferSpaceAvailable:
                    case SocketError.Interrupted:
                        return 0;
                    case SocketError.MessageSize:
                        //NetDebug.Write(NetLogLevel.Trace, $"[SRD] 10040, datalen: {length}");
                        return 0;

                    case SocketError.HostUnreachable:
                    case SocketError.NetworkUnreachable:
                        if (DisconnectOnUnreachable && remoteEndPoint is NetPeer peer)
                        {
                            DisconnectPeerForce(
                                peer,
                                ex.SocketErrorCode == SocketError.HostUnreachable
                                    ? DisconnectReason.HostUnreachable
                                    : DisconnectReason.NetworkUnreachable,
                                ex.SocketErrorCode,
                                null);
                        }

                        CreateEvent(NetEvent.EType.Error, remoteEndPoint: remoteEndPoint, errorCode: ex.SocketErrorCode);
                        return -1;

                    case SocketError.Shutdown:
                        CreateEvent(NetEvent.EType.Error, remoteEndPoint: remoteEndPoint, errorCode: ex.SocketErrorCode);
                        return -1;

                    default:
                        NetDebug.WriteError($"[S] {ex}");
                        return -1;
                }
            }
            catch (Exception ex)
            {
                NetDebug.WriteError($"[S] {ex}");
                return 0;
            }
            finally
            {
                if (expandedPacket != null)
                    PoolRecycle(expandedPacket);
            }

            if (result <= 0)
                return 0;

            if (EnableStatistics)
            {
                Statistics.IncrementPacketsSent();
                Statistics.AddBytesSent(length);
            }

            return result;
        }

        public bool SendBroadcast(NetDataWriter writer, int port)
        {
            return SendBroadcast(writer.Data, 0, writer.Length, port);
        }

        public bool SendBroadcast(byte[] data, int port)
        {
            return SendBroadcast(data, 0, data.Length, port);
        }

        public bool SendBroadcast(byte[] data, int start, int length, int port)
        {
            if (!IsRunning)
                return false;

            NetPacket packet;
            if (_extraPacketLayer != null)
            {
                var headerSize = NetPacket.GetHeaderSize(PacketProperty.Broadcast);
                packet = PoolGetPacket(headerSize + length + _extraPacketLayer.ExtraPacketSizeForLayer);
                packet.Property = PacketProperty.Broadcast;
                Buffer.BlockCopy(data, start, packet.RawData, headerSize, length);
                var checksumComputeStart = 0;
                int preCrcLength = length + headerSize;
                IPEndPoint emptyEp = null;
                _extraPacketLayer.ProcessOutBoundPacket(ref emptyEp, ref packet.RawData, ref checksumComputeStart, ref preCrcLength);
            }
            else
            {
                packet = PoolGetWithData(PacketProperty.Broadcast, data, start, length);
            }

            bool broadcastSuccess = false;
            bool multicastSuccess = false;
            try
            {
                broadcastSuccess = _udpSocketv4.SendTo(
                    packet.RawData,
                    0,
                    packet.Size,
                    SocketFlags.None,
                    new IPEndPoint(IPAddress.Broadcast, port)) > 0;

                if (_udpSocketv6 != null)
                {
                    multicastSuccess = _udpSocketv6.SendTo(
                        packet.RawData,
                        0,
                        packet.Size,
                        SocketFlags.None,
                        new IPEndPoint(MulticastAddressV6, port)) > 0;
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.HostUnreachable)
                    return broadcastSuccess;
                NetDebug.WriteError($"[S][MCAST] {ex}");
                return broadcastSuccess;
            }
            catch (Exception ex)
            {
                NetDebug.WriteError($"[S][MCAST] {ex}");
                return broadcastSuccess;
            }
            finally
            {
                PoolRecycle(packet);
            }

            return broadcastSuccess || multicastSuccess;
        }

        private void CloseSocket()
        {
            _isRunning = false;
            if (_receiveThread != null && _receiveThread != Thread.CurrentThread)
                _receiveThread.Join();
            _receiveThread = null;

            foreach (var t in _extraReceiveThreads)
            {
                if (t != null && t != Thread.CurrentThread) t.Join();
            }
            _extraReceiveThreads.Clear();
            foreach (var s in _extraSocketsV4) s?.Close();
            foreach (var s in _extraSocketsV6) s?.Close();
            _extraSocketsV4.Clear();
            _extraSocketsV6.Clear();
            _useReusePort = false;

            _udpSocketv4?.Close();
            _udpSocketv6?.Close();
            _udpSocketv4 = null;
            _udpSocketv6 = null;
        }
    }
}
