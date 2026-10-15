using Basis.Network.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BasisNetworkServer.Storage
{
    public sealed class BasisStorageService : IDisposable
    {
        public const int DefaultLanes = 4, LaneQueueLimit = 1024, MaxConflictRetries = 3, MaxTrackedBudgets = 8192;
        public const double ReadsPerSecond = 20, ReadBurst = 60, WritesPerSecond = 2, WriteBurst = 10;
        private const long LaneCacheBytes = 16L * 1024 * 1024, IdleBudgetSeconds = 60;
        private readonly IBasisStorageBackend backend;
        private readonly Lane[] lanes;
        private readonly ConcurrentDictionary<string, Budget> budgets = new ConcurrentDictionary<string, Budget>(StringComparer.Ordinal);
        private int bytesPerPlayer, disposed;
        private long maxTotalBytes, totalBytes, totalRecords, served, refused, failures, lastPrune;
        public string BackendName => backend.Name;
        public int BytesPerPlayer => Volatile.Read(ref bytesPerPlayer);
        public long MaxTotalBytes => Interlocked.Read(ref maxTotalBytes);
        public long TotalBytes => Interlocked.Read(ref totalBytes);
        public long TotalRecords => Interlocked.Read(ref totalRecords);
        public long Served => Interlocked.Read(ref served);
        public long Refused => Interlocked.Read(ref refused);
        public long Failures => Interlocked.Read(ref failures);
        public BasisStorageService(IBasisStorageBackend backend, int bytesPerPlayer, long maxTotalBytes, int laneCount = DefaultLanes)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            Configure(bytesPerPlayer, maxTotalBytes);
            backend.Measure(out long records, out long bytes);
            totalRecords = records;
            totalBytes = bytes;
            lanes = new Lane[Math.Max(1, laneCount)];
            for (int i = 0; i < lanes.Length; i++) lanes[i] = new Lane(this, i);
        }
        public void Configure(int bytesPerPlayer, long maxTotalBytes)
        {
            Volatile.Write(ref this.bytesPerPlayer, Math.Min(Math.Max(1, bytesPerPlayer), BasisStorageProtocol.MaxBytesPerPlayer));
            Interlocked.Exchange(ref this.maxTotalBytes, Math.Max(0, maxTotalBytes));
        }
        public BasisStorageStatus Submit(string owner, BasisStorageRequest request, Action<BasisStorageReply> reply)
        {
            if (reply == null) throw new ArgumentNullException(nameof(reply));
            if (Volatile.Read(ref disposed) != 0) return Refuse(BasisStorageStatus.Unavailable);
            if (!IsValidOwner(owner)) return Refuse(BasisStorageStatus.Denied);
            if (!Charge(owner, request)) return Refuse(BasisStorageStatus.RateLimited);
            if (!IsWellFormed(request)) return Refuse(BasisStorageStatus.Invalid);
            return Enqueue(new Work { Kind = WorkKind.Request, Owner = owner, Request = request, Reply = reply });
        }
        public void Forget(string owner)
        {
            if (!IsValidOwner(owner) || Volatile.Read(ref disposed) != 0) return;
            Enqueue(new Work { Kind = WorkKind.Evict, Owner = owner });
        }
        public bool TryInspect(string owner, TimeSpan timeout, out BasisStorageRecord snapshot)
        {
            snapshot = null;
            Work work = new Work { Kind = WorkKind.Inspect, Owner = owner, Done = new ManualResetEventSlim(false) };
            if (!Wait(work, timeout)) return false;
            snapshot = work.Snapshot;
            return work.Result;
        }
        public bool TryWipe(string owner, TimeSpan timeout)
        {
            Work work = new Work { Kind = WorkKind.Wipe, Owner = owner, Done = new ManualResetEventSlim(false) };
            return Wait(work, timeout) && work.Result;
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            foreach (Lane lane in lanes) lane.Queue.CompleteAdding();
            foreach (Lane lane in lanes)
            {
                if (!lane.Thread.Join(TimeSpan.FromSeconds(15))) BNL.LogWarning($"Player storage lane {lane.Index} did not finish its queued writes before shutdown.");
            }
            backend.Dispose();
        }
        public static bool IsValidOwner(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return false;
            try { return BasisStorageProtocol.StrictUtf8.GetByteCount(owner) <= BasisStorageRecord.MaxOwnerBytes; }
            catch (ArgumentException) { return false; }
        }
        private bool Wait(Work work, TimeSpan timeout)
        {
            if (!IsValidOwner(work.Owner) || Volatile.Read(ref disposed) != 0 || Enqueue(work) != BasisStorageStatus.Ok) return false;
            return work.Done.Wait(timeout);
        }
        private BasisStorageStatus Enqueue(Work work)
        {
            work.OwnerKey = BasisStorageRecord.OwnerKeyOf(work.Owner);
            Lane lane = lanes[LaneOf(work.OwnerKey)];
            try
            {
                if (lane.Queue.TryAdd(work)) return BasisStorageStatus.Ok;
            }
            catch (InvalidOperationException)
            {
                return Refuse(BasisStorageStatus.Unavailable);
            }
            return Refuse(BasisStorageStatus.Busy);
        }
        private int LaneOf(string ownerKey) => (int)(Convert.ToUInt32(ownerKey.Substring(0, 8), 16) % (uint)lanes.Length);
        private BasisStorageStatus Refuse(BasisStorageStatus status)
        {
            Interlocked.Increment(ref refused);
            return status;
        }
        private static bool IsWellFormed(BasisStorageRequest request)
        {
            if (!BasisStorageProtocol.IsKnown(request.Op) || !BasisStorageProtocol.IsValidName(request.Scope)) return false;
            if (BasisStorageProtocol.HasKey(request.Op) && !BasisStorageProtocol.IsValidName(request.Key)) return false;
            return request.Op != BasisStorageOp.Set || (request.Value != null && request.Value.Length <= BasisStorageProtocol.MaxValueBytes);
        }
        private bool Charge(string owner, BasisStorageRequest request)
        {
            long now = Stopwatch.GetTimestamp();
            long pruned = Interlocked.Read(ref lastPrune);
            if (budgets.Count > MaxTrackedBudgets && now - pruned > Stopwatch.Frequency && Interlocked.CompareExchange(ref lastPrune, now, pruned) == pruned) PruneBudgets(now);
            int limit = BytesPerPlayer;
            double byteRate = limit / 2.0, byteBurst = limit * 2.0;
            Budget budget = budgets.GetOrAdd(owner, _ => new Budget { Reads = ReadBurst, Writes = WriteBurst, Bytes = byteBurst, Stamp = now });
            lock (budget)
            {
                double elapsed = Math.Max(0, (now - budget.Stamp) / (double)Stopwatch.Frequency);
                budget.Stamp = Math.Max(now, budget.Stamp);
                budget.Reads = Math.Min(ReadBurst, budget.Reads + elapsed * ReadsPerSecond);
                budget.Writes = Math.Min(WriteBurst, budget.Writes + elapsed * WritesPerSecond);
                budget.Bytes = Math.Min(byteBurst, budget.Bytes + elapsed * byteRate);
                if (!BasisStorageProtocol.Mutates(request.Op))
                {
                    if (budget.Reads < 1) return false;
                    budget.Reads -= 1;
                    return true;
                }
                double bytes = request.Op == BasisStorageOp.Set && request.Value != null ? Math.Min(request.Value.Length, byteBurst) : 0;
                if (budget.Writes < 1 || budget.Bytes < bytes) return false;
                budget.Writes -= 1;
                budget.Bytes -= bytes;
                return true;
            }
        }
        private void PruneBudgets(long now)
        {
            long idle = IdleBudgetSeconds * Stopwatch.Frequency;
            foreach (KeyValuePair<string, Budget> pair in budgets)
            {
                if (now - Volatile.Read(ref pair.Value.Stamp) > idle) budgets.TryRemove(pair.Key, out _);
            }
        }
        private void Process(Lane lane, Work work)
        {
            switch (work.Kind)
            {
                case WorkKind.Evict:
                    lane.Forget(work.OwnerKey);
                    break;
                case WorkKind.Inspect:
                    Entry inspected = Load(lane, work.OwnerKey, work.Owner);
                    work.Snapshot = inspected.Record.Clone();
                    work.Result = true;
                    break;
                case WorkKind.Wipe:
                    work.Result = Wipe(lane, work.OwnerKey, work.Owner);
                    break;
                case WorkKind.Request:
                    BasisStorageReply reply = Execute(lane, work);
                    Interlocked.Increment(ref served);
                    work.Replied = true;
                    work.Reply(reply);
                    break;
            }
        }
        private BasisStorageReply Execute(Lane lane, Work work)
        {
            BasisStorageRequest request = work.Request;
            BasisStorageReply reply = new BasisStorageReply { Op = request.Op, RequestId = request.RequestId, Status = BasisStorageStatus.Ok };
            for (int attempt = 0; ; attempt++)
            {
                Entry entry = Load(lane, work.OwnerKey, work.Owner);
                BasisStorageRecord record = entry.Record;
                int limit = BytesPerPlayer;
                reply.Used = record.Used;
                reply.Limit = limit;
                switch (request.Op)
                {
                    case BasisStorageOp.Get:
                        if (record.TryGet(request.Scope, request.Key, out byte[] value)) reply.Value = value;
                        else reply.Status = BasisStorageStatus.NotFound;
                        return reply;
                    case BasisStorageOp.Keys:
                        reply.Keys = record.KeysOf(request.Scope);
                        return reply;
                    case BasisStorageOp.Usage:
                        return reply;
                }
                BasisStorageRecord next = record.Clone();
                BasisStorageStatus status = Apply(next, request, limit, out bool changed);
                if (status != BasisStorageStatus.Ok || !changed)
                {
                    reply.Status = status;
                    return reply;
                }
                int stored = next.IsEmpty ? 0 : next.BlobLength;
                long growth = stored - entry.StoredBytes;
                if (growth > 0 && TotalBytes + growth > MaxTotalBytes)
                {
                    reply.Status = BasisStorageStatus.Full;
                    return reply;
                }
                next.Revision = record.Revision + 1;
                bool saved = next.IsEmpty ? backend.TryDelete(work.OwnerKey, record.Revision) : backend.TrySave(work.OwnerKey, next.Serialize(), record.Revision, next.Revision);
                if (!saved)
                {
                    lane.Forget(work.OwnerKey);
                    if (attempt < MaxConflictRetries) continue;
                    reply.Status = BasisStorageStatus.Busy;
                    return reply;
                }
                Interlocked.Add(ref totalBytes, growth);
                if (entry.StoredBytes == 0 && stored > 0) Interlocked.Increment(ref totalRecords);
                else if (entry.StoredBytes > 0 && stored == 0) Interlocked.Decrement(ref totalRecords);
                lane.Remember(new Entry { OwnerKey = work.OwnerKey, Record = next.IsEmpty ? new BasisStorageRecord(work.Owner) : next, StoredBytes = stored });
                reply.Used = next.Used;
                return reply;
            }
        }
        private static BasisStorageStatus Apply(BasisStorageRecord record, BasisStorageRequest request, int limit, out bool changed)
        {
            changed = false;
            switch (request.Op)
            {
                case BasisStorageOp.Set:
                    BasisStorageStatus status = record.Set(request.Scope, request.Key, request.Value, limit);
                    changed = status == BasisStorageStatus.Ok;
                    return status;
                case BasisStorageOp.Delete:
                    changed = record.Remove(request.Scope, request.Key);
                    return changed ? BasisStorageStatus.Ok : BasisStorageStatus.NotFound;
                case BasisStorageOp.Clear:
                    changed = record.Clear(request.Scope) > 0;
                    return BasisStorageStatus.Ok;
                default:
                    return BasisStorageStatus.Invalid;
            }
        }
        private bool Wipe(Lane lane, string ownerKey, string owner)
        {
            for (int attempt = 0; attempt <= MaxConflictRetries; attempt++)
            {
                lane.Forget(ownerKey);
                Entry entry = Load(lane, ownerKey, owner);
                if (entry.StoredBytes == 0) return true;
                if (!backend.TryDelete(ownerKey, entry.Record.Revision)) continue;
                Interlocked.Add(ref totalBytes, -entry.StoredBytes);
                Interlocked.Decrement(ref totalRecords);
                lane.Forget(ownerKey);
                return true;
            }
            return false;
        }
        private Entry Load(Lane lane, string ownerKey, string owner)
        {
            if (lane.TryRecall(ownerKey, out Entry entry)) return entry;
            entry = new Entry { OwnerKey = ownerKey, Record = new BasisStorageRecord(owner) };
            if (backend.TryLoad(ownerKey, out byte[] blob))
            {
                if (BasisStorageRecord.TryParse(blob, owner, out BasisStorageRecord record))
                {
                    entry.Record = record;
                    entry.StoredBytes = blob.Length;
                }
                else
                {
                    backend.Quarantine(ownerKey);
                    Interlocked.Add(ref totalBytes, -blob.Length);
                    Interlocked.Decrement(ref totalRecords);
                    BNL.LogError($"Player storage record {ownerKey} failed its integrity check and was set aside; that player starts again from empty storage.");
                }
            }
            lane.Remember(entry);
            return entry;
        }
        private void Run(Lane lane)
        {
            foreach (Work work in lane.Queue.GetConsumingEnumerable())
            {
                try
                {
                    Process(lane, work);
                }
                catch (Exception ex)
                {
                    long count = Interlocked.Increment(ref failures);
                    if (count <= 5 || count % 100 == 0) BNL.LogError($"Player storage ({backend.Name}) failed a {work.Kind} (failure #{count}): {ex.GetBaseException().Message}");
                    lane.Forget(work.OwnerKey);
                    if (work.Kind == WorkKind.Request && !work.Replied)
                    {
                        try { work.Reply(new BasisStorageReply { Op = work.Request.Op, RequestId = work.Request.RequestId, Status = BasisStorageStatus.Unavailable, Limit = BytesPerPlayer }); }
                        catch (Exception replyFailure) { BNL.LogError($"Player storage could not report a failure: {replyFailure.Message}"); }
                    }
                }
                finally
                {
                    work.Done?.Set();
                }
            }
        }
        private enum WorkKind : byte
        {
            Request,
            Evict,
            Inspect,
            Wipe,
        }
        private sealed class Work
        {
            public WorkKind Kind;
            public string Owner, OwnerKey;
            public BasisStorageRequest Request;
            public Action<BasisStorageReply> Reply;
            public BasisStorageRecord Snapshot;
            public bool Result, Replied;
            public ManualResetEventSlim Done;
        }
        private sealed class Entry
        {
            public string OwnerKey;
            public BasisStorageRecord Record;
            public int StoredBytes;
        }
        private sealed class Budget
        {
            public double Reads, Writes, Bytes;
            public long Stamp;
        }
        private sealed class Lane
        {
            public readonly int Index;
            public readonly BlockingCollection<Work> Queue = new BlockingCollection<Work>(new ConcurrentQueue<Work>(), LaneQueueLimit);
            public readonly Thread Thread;
            private readonly Dictionary<string, LinkedListNode<Entry>> index = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
            private readonly LinkedList<Entry> order = new LinkedList<Entry>();
            private long cachedBytes;
            public Lane(BasisStorageService service, int index)
            {
                Index = index;
                Thread = new Thread(() => service.Run(this)) { IsBackground = true, Name = $"BasisStorage{index}" };
                Thread.Start();
            }
            public bool TryRecall(string ownerKey, out Entry entry)
            {
                if (!index.TryGetValue(ownerKey, out LinkedListNode<Entry> node))
                {
                    entry = null;
                    return false;
                }
                order.Remove(node);
                order.AddFirst(node);
                entry = node.Value;
                return true;
            }
            public void Remember(Entry entry)
            {
                Forget(entry.OwnerKey);
                index[entry.OwnerKey] = order.AddFirst(entry);
                cachedBytes += entry.Record.BlobLength;
                while (cachedBytes > LaneCacheBytes && order.Count > 1) Forget(order.Last.Value.OwnerKey);
            }
            public void Forget(string ownerKey)
            {
                if (ownerKey == null || !index.TryGetValue(ownerKey, out LinkedListNode<Entry> node)) return;
                index.Remove(ownerKey);
                order.Remove(node);
                cachedBytes -= node.Value.Record.BlobLength;
            }
        }
    }
}
