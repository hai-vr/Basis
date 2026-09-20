using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var eventLog = string.Join('\n', File.ReadLines(path)
                .TakeLast(10)
                .Select(line => line));
            return new List<byte[]>() { Encoding.UTF8.GetBytes(eventLog) };
        }
    }
}
