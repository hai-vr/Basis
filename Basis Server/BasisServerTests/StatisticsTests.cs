using System;
using System.Threading.Tasks;
using BasisNetworkServer.BasisNetworking;
using Xunit;

namespace BasisServerTests
{
    public class BasisNetworkStatisticsTests : IDisposable
    {
        public BasisNetworkStatisticsTests()
        {
            BasisNetworkStatistics.Clear();
            BasisNetworkStatistics.IsRecordingData = true;
        }

        public void Dispose()
        {
            BasisNetworkStatistics.IsRecordingData = false;
            BasisNetworkStatistics.Clear();
        }

        [Fact]
        public void RecordInbound_SnapshotShowsCorrectCount()
        {
            BasisNetworkStatistics.RecordInbound(5, 100);
            BasisNetworkStatistics.RecordInbound(5, 200);

            var snapshot = BasisNetworkStatistics.GetSnapshot();

            Assert.True(snapshot.PerIndex.ContainsKey(5));
            Assert.Equal(2UL, snapshot.PerIndex[5].Count);
            Assert.Equal(300UL, snapshot.PerIndex[5].Bytes);
        }

        [Fact]
        public void RecordOutbound_SnapshotShowsCorrectCount()
        {
            BasisNetworkStatistics.RecordOutbound(10, 500);

            var snapshot = BasisNetworkStatistics.GetSnapshot();

            Assert.True(snapshot.OutPerIndex.ContainsKey(10));
            Assert.Equal(1UL, snapshot.OutPerIndex[10].Count);
            Assert.Equal(500UL, snapshot.OutPerIndex[10].Bytes);
        }

        [Fact]
        public void SnapshotAndReset_ClearsCounters()
        {
            BasisNetworkStatistics.RecordInbound(1, 100);
            BasisNetworkStatistics.RecordOutbound(2, 200);

            var snapshot1 = BasisNetworkStatistics.SnapshotAndReset();
            Assert.True(snapshot1.PerIndex.ContainsKey(1));
            Assert.True(snapshot1.OutPerIndex.ContainsKey(2));

            var snapshot2 = BasisNetworkStatistics.GetSnapshot();
            Assert.Empty(snapshot2.PerIndex);
            Assert.Empty(snapshot2.OutPerIndex);
        }

        [Fact]
        public void Clear_ZerosEverything()
        {
            BasisNetworkStatistics.RecordInbound(0, 100);
            BasisNetworkStatistics.RecordOutbound(255, 999);

            BasisNetworkStatistics.Clear();

            var snapshot = BasisNetworkStatistics.GetSnapshot();
            Assert.Empty(snapshot.PerIndex);
            Assert.Empty(snapshot.OutPerIndex);
        }

        [Fact]
        public void IsRecordingData_False_DoesNotRecord()
        {
            BasisNetworkStatistics.IsRecordingData = false;

            BasisNetworkStatistics.RecordInbound(1, 100);
            BasisNetworkStatistics.RecordOutbound(2, 200);

            BasisNetworkStatistics.IsRecordingData = true; // re-enable to read snapshot
            var snapshot = BasisNetworkStatistics.GetSnapshot();
            Assert.Empty(snapshot.PerIndex);
            Assert.Empty(snapshot.OutPerIndex);
        }

        [Fact]
        public void ThreadSafety_ConcurrentRecording()
        {
            Parallel.For(0, 1000, i =>
            {
                byte index = (byte)(i % 256);
                BasisNetworkStatistics.RecordInbound(index, 1);
                BasisNetworkStatistics.RecordOutbound(index, 1);
            });

            var snapshot = BasisNetworkStatistics.GetSnapshot();

            // Sum all inbound counts should equal 1000
            ulong totalIn = 0;
            foreach (var kvp in snapshot.PerIndex)
                totalIn += kvp.Value.Count;
            Assert.Equal(1000UL, totalIn);

            ulong totalOut = 0;
            foreach (var kvp in snapshot.OutPerIndex)
                totalOut += kvp.Value.Count;
            Assert.Equal(1000UL, totalOut);
        }

        [Fact]
        public void SnapshotResetEncode_Decode_RoundTrip()
        {
            BasisNetworkStatistics.RecordInbound(1, 100);
            BasisNetworkStatistics.RecordInbound(1, 200);
            BasisNetworkStatistics.RecordOutbound(5, 50);

            byte[] encoded = BasisNetworkStatistics.Snapshot.SnapshotResetEncode(compress: true);
            Assert.NotNull(encoded);
            Assert.True(encoded.Length > 0);

            var decoded = BasisNetworkStatistics.Snapshot.Decode(encoded, compressed: true);
            Assert.Equal(2UL, decoded.PerIndex[1].Count);
            Assert.Equal(300UL, decoded.PerIndex[1].Bytes);
            Assert.Equal(1UL, decoded.OutPerIndex[5].Count);
            Assert.Equal(50UL, decoded.OutPerIndex[5].Bytes);
        }

        [Fact]
        public void SnapshotResetEncode_Uncompressed_RoundTrip()
        {
            BasisNetworkStatistics.RecordInbound(42, 777);

            byte[] encoded = BasisNetworkStatistics.Snapshot.SnapshotResetEncode(compress: false);
            var decoded = BasisNetworkStatistics.Snapshot.Decode(encoded, compressed: false);

            Assert.Equal(1UL, decoded.PerIndex[42].Count);
            Assert.Equal(777UL, decoded.PerIndex[42].Bytes);
        }

        [Fact]
        public void Snapshot_EmptyData_EncodesDecodes()
        {
            byte[] encoded = BasisNetworkStatistics.Snapshot.SnapshotResetEncode(compress: false);
            var decoded = BasisNetworkStatistics.Snapshot.Decode(encoded, compressed: false);

            Assert.Empty(decoded.PerIndex);
            Assert.Empty(decoded.OutPerIndex);
        }
    }
}
