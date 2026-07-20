using System.Collections.Generic;
using Unity.Burst;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

public class JiggleDoubleBufferTransformAccessArray {

    private TransformAccessArray transformAccessArray;
    private int transformCount;

    private TransformAccessArray newTransformAccessArray;
    private int newTransformCount;

    public TransformAccessArray GetTransformAccessArray() => transformAccessArray;

    private bool shouldClear = false;

    // Named per instance so a profile says which of the four buffers is rebuilding.
    private readonly ProfilerMarker clearMarker;
    private readonly ProfilerMarker generateMarker;

    public JiggleDoubleBufferTransformAccessArray(int initialCapacity, string name = "Unnamed") {
        transformAccessArray = new TransformAccessArray(initialCapacity);
        newTransformAccessArray = new TransformAccessArray(initialCapacity);
        clearMarker = new ProfilerMarker($"ClearAccessArrays.{name}");
        generateMarker = new ProfilerMarker($"GenerateNewAccessArrays.{name}");
    }

    public void Flip() {
        (transformAccessArray, newTransformAccessArray) = (newTransformAccessArray, transformAccessArray);
        (transformCount, newTransformCount) = (newTransformCount, transformCount);
        shouldClear = true;
    }

    public void Dispose() {
        if (transformAccessArray.isCreated) {
            transformAccessArray.Dispose();
        }

        if (newTransformAccessArray.isCreated) {
            newTransformAccessArray.Dispose();
        }
    }

    public void ClearIfNeeded(int maxRemoveCount = 512) {
        if (!shouldClear) {
            return;
        }

        using var scope = clearMarker.Auto();

        var capacity = newTransformAccessArray.capacity;
        newTransformAccessArray.Dispose();
        newTransformAccessArray = new TransformAccessArray(capacity);

        newTransformCount = 0;
        shouldClear = false;
    }

    public void GenerateNewAccessArrays(ref int currentIndex, out bool hasFinished, List<Transform> transformAccessList, int maxAddCount = 512) {
        if (shouldClear) {
            ClearIfNeeded(maxAddCount);
            hasFinished = false;
            return;
        }

        using var scope = generateMarker.Auto();
        var count = transformAccessList.Count;
        int addedSoFar = 0;
        for (var index = currentIndex; index < count && addedSoFar < maxAddCount; index++) {
            var transform = transformAccessList[index];
            if (!transform) {
                newTransformAccessArray.Add(JiggleMemoryBus.GetDummyTransform(index));
            } else {
                newTransformAccessArray.Add(transform);
            }

            addedSoFar++;
        }

        currentIndex += addedSoFar;

        if (currentIndex == count) {
            newTransformCount = count;
            hasFinished = true;
            return;
        }

        hasFinished = false;
    }
}

}