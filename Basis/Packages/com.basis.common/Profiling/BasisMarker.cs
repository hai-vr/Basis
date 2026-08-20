using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Profiling;
public readonly struct BasisMarker
{
    readonly ProfilerMarker marker;
    public BasisMarker(string name) { marker = new ProfilerMarker(name); }
    public BasisMarker(string group, string name) { marker = new ProfilerMarker(group + "." + name); }
    public ProfilerMarker Marker { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return marker; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public ProfilerMarker.AutoScope Auto() { return marker.Auto(); }
    [MethodImpl(MethodImplOptions.AggressiveInlining), Conditional("ENABLE_PROFILER")] public void Begin() { marker.Begin(); }
    [MethodImpl(MethodImplOptions.AggressiveInlining), Conditional("ENABLE_PROFILER")] public void End() { marker.End(); }
    public static implicit operator ProfilerMarker(BasisMarker basisMarker) { return basisMarker.marker; }
}
