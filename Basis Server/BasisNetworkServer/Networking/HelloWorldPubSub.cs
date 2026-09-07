using Basis.Network.Core;
using Basis.Network.Server.Messaging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace BasisNetworkServer.BasisNetworking
{
    public sealed class HelloWorldPubSub : IPubSubDataProvider
    {
        private const string ChannelName = "hello.world";
        private static HelloWorldPubSub _instance;
        private Thread _worker;
        private volatile bool _running;
        private int _counter;

        public static void Start()
        {
            if (_instance != null) return;
            _instance = new HelloWorldPubSub();
            _instance.StartInternal();
        }

        public static void Stop()
        {
            if (_instance == null) return;
            _instance.StopInternal();
            _instance = null;
        }

        private void StartInternal()
        {
            _running = true;
            _counter = 0;
            BasisNetworkHandlePubSub.RegisterChannel(ChannelName, this);
            _worker = new Thread(Run)
            {
                Name = "HelloWorldPubSub",
                IsBackground = true
            };
            _worker.Start();
        }

        private void StopInternal()
        {
            _running = false;
            _worker?.Join(100);
            BasisNetworkHandlePubSub.UnregisterChannel(ChannelName);
        }

        private void Run()
        {
            while (_running)
            {
                try
                {
                    byte[] data = BitConverter.GetBytes(_counter);
                    if (!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(data);
                    }
                    BasisNetworkHandlePubSub.Publish(ChannelName, data);
                    _counter++;
                }
                catch (Exception ex)
                {
                    BNL.LogError($"[HelloWorldPubSub] Error: {ex.Message}");
                }
                Thread.Sleep(1000);
            }
        }

        public List<byte[]> GetInitialState()
        {
            return new List<byte[]> { Encoding.UTF8.GetBytes("Hello, World!") };
        }
    }
}
