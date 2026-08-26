using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

// CAUTION: Beta API, subject to change.
namespace HVR.Discovery
{
    public sealed class HVRDiscoveryBeacon
    {
        private readonly Component _component;
        private readonly Dictionary<string, object> _scriptingData = new();

        [PublicAPI] public static HVRDiscoveryBeacon NewBeacon(Component component) => new(component);

        private HVRDiscoveryBeacon(Component component)
        {
            _component = component;
        }

        [PublicAPI] public void InitializeScriptValue(string key, object value) { _scriptingData.TryAdd(key, value); }
        [PublicAPI] public bool TryGetScriptValue(string key, out object value) { return _scriptingData.TryGetValue(key, out value); }
        [PublicAPI] public object HasScriptValue(string key) { return _scriptingData.ContainsKey(key); }
        [PublicAPI] public object GetScriptValue(string key) { return _scriptingData[key]; }

        [PublicAPI] public Transform AsTransform => _component.transform;

        public Component Component => _component;
    }

    public sealed class HVRDiscoveryFinder
    {
        private readonly Component _component;
        private readonly float _range;
        private readonly HVRDiscovery.BeaconEnterOrExit _whenBeaconEnterOrExit;

        [PublicAPI] public static HVRDiscoveryFinder NewRangeFinder(Component component, float range, HVRDiscovery.BeaconEnterOrExit whenBeaconEnterOrExit) => new(component, range, whenBeaconEnterOrExit);

        private HVRDiscoveryFinder(Component component, float range, HVRDiscovery.BeaconEnterOrExit whenBeaconEnterOrExit)
        {
            _component = component;
            _range = range;
            _whenBeaconEnterOrExit = whenBeaconEnterOrExit;
        }

        [PublicAPI] public Transform AsTransform => _component.transform;

        public Component Component => _component;
        public float Range => _range;
        public HVRDiscovery.BeaconEnterOrExit WhenBeaconEnterOrExit => _whenBeaconEnterOrExit;
    }

    public class HVRDiscovery
    {
        private const BasisDebug.LogTag LogTag = BasisDebug.LogTag.Props;
        public const float UpdateInterval = 0.1f;
        public const float HousekeepingInterval = 10f;
        public const int BetaHardLimit = 20;

        public delegate void BeaconEnterOrExit(HVRDiscoveryBeacon beacon, bool isEntering);

        private readonly List<HVRDiscoveryBeacon> _beacons = new();
        private readonly List<HVRDiscoveryFinder> _finderKeys = new();
        private readonly Dictionary<HVRDiscoveryFinder, HashSet<HVRDiscoveryBeacon>> _finderToBeaconsDict = new();
        private float _lastTime;
        private float _lastHousekeepingTime;

        private static readonly HVRDiscovery Instance = new();

        public static void Tick() => Instance.SimulateTick();

        public void SimulateTick()
        {
            try
            {
                DoSimulateTick();
            }
            catch (Exception e)
            {
                BasisDebug.LogError(e, LogTag);
            }
        }

        private void DoSimulateTick()
        {
            if (Time.time - _lastTime < UpdateInterval) return;
            _lastTime = Time.time;

            Housekeeping();

            // TODO: Temporary naive implementation for the beta API. With BetaHardLimit set to 20, this has a maximum of 400 distance checks.
            // This should be replaced with another system, such as jobs or a compute shader after review by other developers.
            for (var index = 0; index < Math.Min(_finderKeys.Count, BetaHardLimit); index++)
            {
                var finder = _finderKeys[index];
                var containedBeacons = _finderToBeaconsDict[finder];
                var sqRange = finder.Range * finder.Range;

                for (var i = 0; i < Math.Min(_beacons.Count, BetaHardLimit); i++)
                {
                    var beacon = _beacons[i];

                    var sqDistance = (beacon.AsTransform.position - finder.AsTransform.position).sqrMagnitude;

                    var isInside = sqDistance <= sqRange;
                    var wasInside = containedBeacons.Contains(beacon);

                    if (isInside && !wasInside)
                    {
                        containedBeacons.Add(beacon);
                        finder.WhenBeaconEnterOrExit.Invoke(beacon, true);
                    }
                    else if (!isInside && wasInside)
                    {
                        containedBeacons.Remove(beacon);
                        finder.WhenBeaconEnterOrExit.Invoke(beacon, false);
                    }
                }
            }
        }

        private void Housekeeping()
        {
            if (Time.time - _lastHousekeepingTime < HousekeepingInterval) return;
            _lastHousekeepingTime = Time.time;

            // Reap beacons or finders that vanished without the caller having invoked Unregister (e.g., due to failed Cilbox contexts)
            foreach (var beacon in _beacons) { if (null == beacon.Component) Unregister(beacon); }
            foreach (var finder in _finderKeys) { if (null == finder.Component) Unregister(finder); }
        }

        [PublicAPI]
        public void Register(HVRDiscoveryBeacon beacon)
        {
            if (!_beacons.Contains(beacon))
            {
                _beacons.Add(beacon);
                BasisDebug.Log($"Registering beacon {beacon.Component.name}.", LogTag);
            }
        }

        [PublicAPI]
        public void Unregister(HVRDiscoveryBeacon beacon)
        {
            if (_beacons.Remove(beacon))
            {
                BasisDebug.Log($"Unregistered beacon {beacon.Component.name}.", LogTag);
            }
        }

        [PublicAPI]
        public void Unregister(HVRDiscoveryFinder finder)
        {
            if (_finderKeys.Remove(finder))
            {
                BasisDebug.Log($"Unregistered finder {finder.Component.name}.", LogTag);
            }
            _finderToBeaconsDict.Remove(finder);
        }

        [PublicAPI]
        public void Register(HVRDiscoveryFinder finder)
        {
            if (!_finderToBeaconsDict.ContainsKey(finder))
            {
                _finderKeys.Add(finder);
                _finderToBeaconsDict[finder] = new HashSet<HVRDiscoveryBeacon>();
                BasisDebug.Log($"Registered finder {finder.Component.name}.", LogTag);
            }
        }
    }
}
