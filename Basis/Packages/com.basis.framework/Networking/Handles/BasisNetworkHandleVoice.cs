using Basis.Network.Core;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Receivers;
using System.Threading;
using System;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Concurrent;
using static SerializableBasis;

public static class BasisNetworkHandleVoice
{
    private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private const int TimeoutMilliseconds = 1000;
    public static ConcurrentQueue<ServerAudioSegmentMessage> Message = new ConcurrentQueue<ServerAudioSegmentMessage>();
    public const int MaxStoredServerAudioSegmentMessage = 250;

    public static async Task HandleAudioUpdate(NetPacketReader Reader)
    {
        try
        {
            await semaphore.WaitAsync(TimeoutMilliseconds);
            try
            {
                if (Message.TryDequeue(out ServerAudioSegmentMessage audioUpdate) == false)
                {
                    audioUpdate = new ServerAudioSegmentMessage();
                }
                audioUpdate.Deserialize(Reader);
                if (BasisNetworkPlayers.RemotePlayers.TryGetValue(audioUpdate.playerIdMessage.playerID, out BasisNetworkReceiver player))
                {
                    if (audioUpdate.audioSegmentData.LengthUsed == 0)
                    {
                        BasisDebug.LogError("Audio Segment Data Length was zero this is now unsupported", BasisDebug.LogTag.Voice);
                    }
                    else
                    {
                        player.ReceiveNetworkAudio(audioUpdate);
                    }
                }
                else
                {
                    BasisDebug.Log("Missing Player For Message" + audioUpdate.playerIdMessage.playerID);
                }
                Message.Enqueue(audioUpdate);
                while (Message.Count > MaxStoredServerAudioSegmentMessage)
                {
                    Message.TryDequeue(out ServerAudioSegmentMessage seg);
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"Error in HandleAudioUpdate:{ex.Message}{ex.StackTrace}");
            }
            finally
            {
                semaphore.Release();
                if (Reader.IsNull == false)
                {
                    Reader.Recycle();
                }
            }
        }
        catch (OperationCanceledException)
        {
            BasisDebug.LogError("HandleAudioUpdate task canceled.");
        }
    }
}
