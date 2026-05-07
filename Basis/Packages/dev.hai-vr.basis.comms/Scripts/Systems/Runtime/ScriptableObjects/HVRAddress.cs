using System;
using System.Collections.Generic;
using HVR.Vixxy;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [CreateAssetMenu(menuName = "HVR.Basis/Comms", fileName = "HVRAddress")]
    public class HVRAddress : ScriptableObject
    {
        public string path;

        public string AsPath()
        {
            return !string.IsNullOrWhiteSpace(path) ? path : name;
        }

        [NonSerialized] private static readonly Dictionary<string, int> AddressToIdDict = new();
        [NonSerialized] private static readonly Dictionary<int, string> IdToAddressDict = new(); // TODO: Could probably make a List and stop using _nextId, or make a bidirectional dictionary
        [NonSerialized] private static int _nextId = 1;

        [NonSerialized] private static readonly Dictionary<string, int> Sha1ToIdDict = new();
        [NonSerialized] private static readonly Dictionary<int, string> IdToSha1Dict = new();

        /// Generates a GUID address. Use this is when the string address doesn't matter, and you need an internal identifier to reference a value.
        /// Please store this address, don't call this over and over.
        /// Valid IDs start at 1.
        public static int NewRandomAddress()
        {
            return AddressToId(Guid.NewGuid().ToString());
        }

        /// Returns an ID for that address, storing that address if it was not seen before.
        /// This ID is only valid for the duration of the app's execution; don't store it across app executions.
        /// Valid IDs start at 1.<br/>
        /// You should store the returned value of this somewhere, the whole point of having addresses represented as a number is to
        /// avoid using string references on frequently invoked methods.
        public static int AddressToId(string address)
        {
            if (AddressToIdDict.TryGetValue(address, out var ifFromAddress)) return ifFromAddress;

            var sha1 = HVR_VixxyUtil.FromSha1ToString(HVR_VixxyUtil.FullSha1(address));
            if (Sha1ToIdDict.TryGetValue(sha1, out var idFromSha1))
            {
                AddressToIdDict.Add(address, idFromSha1);
                IdToAddressDict.Add(idFromSha1, address);

                return idFromSha1;
            }

            var newId = _nextId;
            AddressToIdDict.Add(address, newId);
            IdToAddressDict.Add(newId, address);
            Sha1ToIdDict.Add(sha1, newId);
            IdToSha1Dict.Add(newId, sha1);
            _nextId++;

            return newId;
        }

        /// Returns an ID for that sha1, storing that sha1 if it was not seen before.
        public static int Sha1ToId(string sha1)
        {
            if (Sha1ToIdDict.TryGetValue(sha1, out var id)) return id;

            var newId = _nextId;
            Sha1ToIdDict.Add(sha1, newId);
            IdToSha1Dict.Add(newId, sha1);
            _nextId++;

            return -1;
        }

        /// Returns the string address for an ID that was returned by any method of this class. Throws an exception if that ID was never seen.
        public static string ResolveKnownAddressFromId(int knownAddressId)
        {
            if (IdToAddressDict.TryGetValue(knownAddressId, out var id)) return id;
            throw new IndexOutOfRangeException();
        }

        /// Generates an address in the form of (pathlike@sha1+componentIndexOnThisType).
        public static string GenerateAddressFromPath<T>(T discriminatorComponent, Transform context) where T : Component
        {
            var componentIndex = Array.IndexOf(discriminatorComponent.GetComponents<T>(), discriminatorComponent);
            var path = HVR_VixxyUtil.GenerateRelativeLikePath(context, discriminatorComponent.transform);
            return $"{path}@{HVR_VixxyUtil.SimpleSha1(path)}+{componentIndex}";
        }
    }
}
