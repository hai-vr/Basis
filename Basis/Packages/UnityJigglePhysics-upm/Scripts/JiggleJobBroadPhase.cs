using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GatorDragonGames.JigglePhysics {
[BurstCompile]
public unsafe struct JiggleGridCell {
    public static int2 GetKeyForPosition(float3 position) {
        const float gridSizeMeters = 0.5f;
        return (int2)math.round(position.xz*(1f/gridSizeMeters));
    }

    public int staleness;
    public int count;
    public int* colliderIndices;

    public JiggleGridCell(int capacity) {
        staleness = 0;
        count = 0;
        colliderIndices = (int*)UnsafeUtility.Malloc(
            sizeof(int) * capacity,
            UnsafeUtility.AlignOf<int>(),
            Allocator.Persistent
        );
    }

    public void Dispose() {
        if (colliderIndices != null) {
            UnsafeUtility.Free(colliderIndices, Allocator.Persistent);
            colliderIndices = null;
        }
    }
}

// TODO: I don't actually know what a broadphase is, might need to be labelled something different?
[BurstCompile]
public struct JiggleJobBroadPhaseClear : IJob {
    public NativeHashMap<int2, JiggleGridCell> broadPhaseMap;
    public NativeReference<JiggleGridCell> globalCell;

    public JiggleJobBroadPhaseClear(JiggleMemoryBus bus) {
        broadPhaseMap = bus.broadPhaseMap;
        globalCell = bus.globalCell;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        broadPhaseMap = bus.broadPhaseMap;
        globalCell = bus.globalCell;
    }

    public void Execute() {
        var keyArray = broadPhaseMap.GetKeyArray(Allocator.Temp);
        var keyLength = keyArray.Length;
        for (int i = 0; i < keyLength; i++) {
            var key = keyArray[i];
            var gridCell = broadPhaseMap[key];
            gridCell.count = 0;
            gridCell.staleness++;
            if (gridCell.staleness > 3) {
                gridCell.Dispose();
                broadPhaseMap.Remove(key);
            } else {
                broadPhaseMap[key] = gridCell;
            }
        }
        
        var global = globalCell.Value;
        global.count = 0;
        globalCell.Value = global;
        
        keyArray.Dispose();
    }
}

[BurstCompile]
public struct JiggleJobBroadPhase : IJob {
    public NativeHashMap<int2, JiggleGridCell> broadPhaseMap;
    public NativeReference<JiggleGridCell> globalCell;
    [ReadOnly] public NativeArray<JiggleCollider> jiggleColliders;
    public int jiggleColliderCount;

    [ReadOnly] public NativeArray<JiggleCullingCamera> cullingCameras;
    public int cullingCameraCount;
    public byte frustumCull;
    public byte distanceCull;
    public float maxCollisionDistance;
    public float nearKeepRadius;
    public float frustumMargin;

    public const int MAX_COLLIDERS = 128;
    public const int GLOBAL_COLLIDER_EDGE_LENGTH = 10;
    private const int GLOBAL_COLLIDER_CELLS = GLOBAL_COLLIDER_EDGE_LENGTH*GLOBAL_COLLIDER_EDGE_LENGTH;

    public JiggleJobBroadPhase(JiggleMemoryBus bus) {
        broadPhaseMap = bus.broadPhaseMap;
        jiggleColliders = bus.sceneColliders;
        jiggleColliderCount = bus.sceneColliderCount;
        globalCell = bus.globalCell;
        cullingCameras = default;
        cullingCameraCount = 0;
        frustumCull = 0;
        distanceCull = 0;
        maxCollisionDistance = 0f;
        nearKeepRadius = 0f;
        frustumMargin = 0f;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        broadPhaseMap = bus.broadPhaseMap;
        jiggleColliders = bus.sceneColliders;
        jiggleColliderCount = bus.sceneColliderCount;
        globalCell = bus.globalCell;
    }

    public void Execute() {
        for (int i = 0; i < jiggleColliderCount; i++) {
            var collider = jiggleColliders[i];
            float3 position = collider.localToWorldMatrix.c3.xyz;
            if (IsColliderCulled(collider, position)) {
                continue;
            }
            float3 aabbExtent;
            switch (collider.type) {
                case JiggleCollider.JiggleColliderType.Capsule: {
                    var up = math.abs(collider.GetWorldAxis());
                    aabbExtent = up * collider.worldHeight * 0.5f + new float3(collider.worldRadius);
                    break;
                }
                case JiggleCollider.JiggleColliderType.Plane:
                    // Planes are infinite, always go into the global cell
                    aabbExtent = new float3(GLOBAL_COLLIDER_EDGE_LENGTH + 1);
                    break;
                default: // Sphere
                    aabbExtent = new float3(collider.worldRadius);
                    break;
            }
            int2 min = JiggleGridCell.GetKeyForPosition(position - aabbExtent);
            int2 max = JiggleGridCell.GetKeyForPosition(position + aabbExtent);
            if ((max.x - min.x) * (max.y - min.y) > GLOBAL_COLLIDER_CELLS) {
                var global = globalCell.Value;
                unsafe {
                    global.colliderIndices[global.count] = i;
                    global.count = math.min(global.count + 1, MAX_COLLIDERS-1);
                }
                globalCell.Value = global;
                continue;
            }
            for (int x = min.x; x <= max.x; x++) {
                for (int y = min.y; y <= max.y; y++) {
                    int2 grid = new int2(x, y);
                    if (!broadPhaseMap.ContainsKey(grid)) {
                        broadPhaseMap.Add(grid, new JiggleGridCell(MAX_COLLIDERS));
                    }

                    if (broadPhaseMap.TryGetValue(grid, out JiggleGridCell gridCell)) {
                        gridCell.staleness = 0;
                        unsafe {
                            gridCell.colliderIndices[gridCell.count] = i;
                            gridCell.count = math.min(gridCell.count + 1, MAX_COLLIDERS-1);
                        }
                        broadPhaseMap[grid] = gridCell;
                    }

                }
            }

        }
    }

    private bool IsColliderCulled(in JiggleCollider collider, float3 position) {
        if (cullingCameraCount == 0 || (frustumCull == 0 && distanceCull == 0)) {
            return false;
        }
        if (collider.type == JiggleCollider.JiggleColliderType.Plane) {
            return false;
        }
        var boundingRadius = collider.worldRadius;
        if (collider.type == JiggleCollider.JiggleColliderType.Capsule) {
            boundingRadius += collider.worldHeight * 0.5f;
        }
        var nearSq = nearKeepRadius * nearKeepRadius;
        var maxRange = maxCollisionDistance + boundingRadius;
        var maxSq = maxRange * maxRange;
        for (int c = 0; c < cullingCameraCount; c++) {
            var cam = cullingCameras[c];
            var distSq = math.distancesq(cam.position, position);
            if (distSq <= nearSq) {
                return false;
            }
            if (distanceCull != 0 && distSq > maxSq) {
                continue;
            }
            if (frustumCull == 0 || SphereInFrustum(cam, position, boundingRadius + frustumMargin)) {
                return false;
            }
        }
        return true;
    }

    private static bool SphereInFrustum(in JiggleCullingCamera cam, float3 center, float radius) {
        if (math.dot(cam.plane0.xyz, center) + cam.plane0.w < -radius) return false;
        if (math.dot(cam.plane1.xyz, center) + cam.plane1.w < -radius) return false;
        if (math.dot(cam.plane2.xyz, center) + cam.plane2.w < -radius) return false;
        if (math.dot(cam.plane3.xyz, center) + cam.plane3.w < -radius) return false;
        if (math.dot(cam.plane4.xyz, center) + cam.plane4.w < -radius) return false;
        if (math.dot(cam.plane5.xyz, center) + cam.plane5.w < -radius) return false;
        return true;
    }
}

}