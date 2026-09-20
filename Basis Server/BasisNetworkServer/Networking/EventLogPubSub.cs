using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace BasisNetworkServer.BasisNetworking
{
    public sealed class EventLogPubSub : IBasisCustomServerDataPublisher
    {
        private const string ChannelName = "eventlog";
        private static EventLogPubSub _instance;

        public static void Start()
        {
            if (_instance != null) return;
            _instance = new EventLogPubSub();
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
            BasisNetworkHandleCustomServerData.RegisterChannel(ChannelName, this);
        }

        private void StopInternal()
        {
            BasisNetworkHandleCustomServerData.UnregisterChannel(ChannelName);
        }

        public List<byte[]> GetInitialState()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "event_log.jsonl");
            var eventLog = File.ReadAllBytes(path);
            return new List<byte[]>() { eventLog };
        }
    }
}
