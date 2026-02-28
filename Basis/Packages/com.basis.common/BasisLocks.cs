using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Basis.Scripts.Common
{
    /// <summary>
    /// High-performance global lock registry.
    /// Each context tracks owners and their re-entrant lock counts.
    /// Thread-safe.
    /// </summary>
    public static class BasisLocks
    {
        public const string LookRotation = "LookRotation";
        public const string Movement = "Movement";
        public const string Crouching = "Crouching";

        private static readonly ConcurrentDictionary<string, ContextState> States =
            new ConcurrentDictionary<string, ContextState>();

        private sealed class ContextState
        {
            public readonly object Sync = new object();
            public readonly Dictionary<string, int> Owners = new Dictionary<string, int>();
            public int TotalCount;
        }

        public static LockContext GetContext(string context)
        {
            if (string.IsNullOrWhiteSpace(context))
                throw new ArgumentNullException(nameof(context));

            States.GetOrAdd(context, _ => new ContextState());
            return new LockContext(context);
        }

        public static LockContext CopyContext(LockContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return new LockContext(context.Context);
        }

        public static void DebugDump(string context = null)
        {
            if (!string.IsNullOrWhiteSpace(context))
            {
                UnityEngine.Debug.Log(new LockContext(context).ToString());
                return;
            }

            var sb = new StringBuilder();
            foreach (var key in States.Keys)
                sb.AppendLine(new LockContext(key).ToString());

            UnityEngine.Debug.Log(sb.ToString());
        }

        public sealed class LockContext : IEnumerable<string>
        {
            public readonly string Context;

            internal LockContext(string context)
            {
                Context = context ?? throw new ArgumentNullException(nameof(context));
            }

            private ContextState GetState()
            {
                return States.GetOrAdd(Context, _ => new ContextState());
            }

            public void Add(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    throw new ArgumentNullException(nameof(key));

                var state = GetState();

                lock (state.Sync)
                {
                    if (state.Owners.TryGetValue(key, out int count))
                        state.Owners[key] = count + 1;
                    else
                        state.Owners[key] = 1;

                    state.TotalCount++;
                }
            }

            public bool Remove(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return false;

                if (!States.TryGetValue(Context, out var state))
                    return false;

                lock (state.Sync)
                {
                    if (!state.Owners.TryGetValue(key, out int count))
                        return false;

                    if (count <= 1)
                        state.Owners.Remove(key);
                    else
                        state.Owners[key] = count - 1;

                    state.TotalCount--;
                    return true;
                }
            }

            public void Clear()
            {
                if (!States.TryGetValue(Context, out var state))
                    return;

                lock (state.Sync)
                {
                    state.Owners.Clear();
                    state.TotalCount = 0;
                }
            }

            public bool Contains(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return false;

                if (!States.TryGetValue(Context, out var state))
                    return false;

                lock (state.Sync)
                    return state.Owners.ContainsKey(key);
            }

            public bool ContainsOnly(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return false;

                if (!States.TryGetValue(Context, out var state))
                    return false;

                lock (state.Sync)
                    return state.TotalCount > 0 &&
                           state.Owners.Count == 1 &&
                           state.Owners.ContainsKey(key);
            }

            public int Count
            {
                get
                {
                    if (!States.TryGetValue(Context, out var state))
                        return 0;

                    lock (state.Sync)
                        return state.TotalCount;
                }
            }

            public List<string> ToList()
            {
                if (!States.TryGetValue(Context, out var state))
                    return new List<string>();

                lock (state.Sync)
                {
                    var result = new List<string>(state.TotalCount);
                    foreach (var kvp in state.Owners)
                    {
                        for (int i = 0; i < kvp.Value; i++)
                            result.Add(kvp.Key);
                    }
                    return result;
                }
            }

            public override string ToString()
            {
                if (!States.TryGetValue(Context, out var state))
                    return $"{Context}[]";

                lock (state.Sync)
                {
                    if (state.TotalCount == 0)
                        return $"{Context}[]";

                    var entries = state.Owners
                        .Select(kvp => kvp.Value == 1
                            ? kvp.Key
                            : $"{kvp.Key} x{kvp.Value}");

                    return $"{Context}[{string.Join(", ", entries)}]";
                }
            }

            public IEnumerator<string> GetEnumerator()
            {
                return ToList().GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override bool Equals(object obj)
            {
                return obj is LockContext other &&
                       other.Context == Context;
            }

            public override int GetHashCode()
            {
                return Context.GetHashCode();
            }

            public static bool operator ==(LockContext a, LockContext b)
                => a?.Context == b?.Context;

            public static bool operator !=(LockContext a, LockContext b)
                => !(a == b);

            public static implicit operator bool(LockContext context)
                => context != null && context.Count > 0;
        }
    }
}
