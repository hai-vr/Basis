using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quadric-error-metric edge-collapse simplifier for the far LOD generator. Avatar meshes are
/// triangle soup (hair cards, cloth shells, accessories), so vertices are position-welded first
/// to connect shells, boundary edges get penalty planes so open sheets keep their silhouette,
/// and a grid-clustering fallback guarantees the triangle budget even when collapse quality
/// checks refuse to go further. Bone attributes (2-influence, collapsed to humanoid bones)
/// ride along by copying from the surviving endpoint nearest the collapse position.
/// </summary>
public static class BasisFarLodMeshSimplifier
{
    private const float WeldEpsilonFactor = 2e-4f;
    private const float BoundaryPenalty = 100f;
    private const float FlipRejectDot = 0.15f;

    public static void Simplify(List<Vector3> positions, List<byte> boneA, List<byte> boneB, List<byte> weightA, List<byte> hiddenFlag, List<int> indices, int targetTriangles)
    {
        Weld(positions, boneA, boneB, weightA, indices);
        if (indices.Count / 3 > targetTriangles)
        {
            Collapse(positions, boneA, boneB, weightA, hiddenFlag, indices, targetTriangles);
        }
        if (indices.Count / 3 > targetTriangles)
        {
            ClusterFallback(positions, boneA, boneB, weightA, indices, targetTriangles);
        }
        Compact(positions, boneA, boneB, weightA, hiddenFlag, indices);
    }

    private static void Weld(List<Vector3> positions, List<byte> boneA, List<byte> boneB, List<byte> weightA, List<int> indices)
    {
        if (positions.Count == 0)
        {
            return;
        }
        Vector3 min = positions[0];
        Vector3 max = positions[0];
        for (int i = 1; i < positions.Count; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }
        float cell = Mathf.Max((max - min).magnitude * WeldEpsilonFactor, 1e-6f);
        float inverseCell = 1f / cell;

        Dictionary<Vector3Int, int> cellToVertex = new Dictionary<Vector3Int, int>(positions.Count);
        int[] remap = new int[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 scaled = (positions[i] - min) * inverseCell;
            Vector3Int key = new Vector3Int(Mathf.RoundToInt(scaled.x), Mathf.RoundToInt(scaled.y), Mathf.RoundToInt(scaled.z));
            if (cellToVertex.TryGetValue(key, out int existing))
            {
                remap[i] = existing;
            }
            else
            {
                cellToVertex[key] = i;
                remap[i] = i;
            }
        }
        for (int i = 0; i < indices.Count; i++)
        {
            indices[i] = remap[indices[i]];
        }
        RemoveDegenerates(indices);
    }

    private static void RemoveDegenerates(List<int> indices)
    {
        int write = 0;
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            if (a == b || b == c || a == c)
            {
                continue;
            }
            indices[write] = a;
            indices[write + 1] = b;
            indices[write + 2] = c;
            write += 3;
        }
        indices.RemoveRange(write, indices.Count - write);
    }

    // Symmetric 4x4 quadric, upper triangle.
    private struct Quadric
    {
        public double M00, M01, M02, M03, M11, M12, M13, M22, M23, M33;

        public void AddPlane(double a, double b, double c, double d, double weight)
        {
            M00 += a * a * weight; M01 += a * b * weight; M02 += a * c * weight; M03 += a * d * weight;
            M11 += b * b * weight; M12 += b * c * weight; M13 += b * d * weight;
            M22 += c * c * weight; M23 += c * d * weight;
            M33 += d * d * weight;
        }

        public void Add(in Quadric other)
        {
            M00 += other.M00; M01 += other.M01; M02 += other.M02; M03 += other.M03;
            M11 += other.M11; M12 += other.M12; M13 += other.M13;
            M22 += other.M22; M23 += other.M23;
            M33 += other.M33;
        }

        public double Evaluate(Vector3 v)
        {
            double x = v.x, y = v.y, z = v.z;
            return M00 * x * x + 2.0 * M01 * x * y + 2.0 * M02 * x * z + 2.0 * M03 * x
                 + M11 * y * y + 2.0 * M12 * y * z + 2.0 * M13 * y
                 + M22 * z * z + 2.0 * M23 * z
                 + M33;
        }

        public bool TrySolveOptimal(out Vector3 result)
        {
            // Solve [A|b] where A is the 3x3 block and b = -(M03, M13, M23).
            double a00 = M00, a01 = M01, a02 = M02;
            double a11 = M11, a12 = M12, a22 = M22;
            double det = a00 * (a11 * a22 - a12 * a12) - a01 * (a01 * a22 - a12 * a02) + a02 * (a01 * a12 - a11 * a02);
            if (System.Math.Abs(det) < 1e-12)
            {
                result = default;
                return false;
            }
            double invDet = 1.0 / det;
            double b0 = -M03, b1 = -M13, b2 = -M23;
            double x = (b0 * (a11 * a22 - a12 * a12) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a12 - a11 * b2)) * invDet;
            double y = (a00 * (b1 * a22 - a12 * b2) - b0 * (a01 * a22 - a02 * a12) + a02 * (a01 * b2 - b1 * a02)) * invDet;
            double z = (a00 * (a11 * b2 - b1 * a12) - a01 * (a01 * b2 - b1 * a02) + b0 * (a01 * a12 - a11 * a02)) * invDet;
            result = new Vector3((float)x, (float)y, (float)z);
            return !(float.IsNaN(result.x) || float.IsInfinity(result.x) ||
                     float.IsNaN(result.y) || float.IsInfinity(result.y) ||
                     float.IsNaN(result.z) || float.IsInfinity(result.z));
        }
    }

    private struct HeapEntry
    {
        public float Cost;
        public int VertexA;
        public int VertexB;
        public int VersionA;
        public int VersionB;
        public Vector3 Target;
    }

    private static void Collapse(List<Vector3> positions, List<byte> boneA, List<byte> boneB, List<byte> weightA, List<byte> hiddenFlag, List<int> indices, int targetTriangles)
    {
        int vertexCount = positions.Count;
        int triangleCount = indices.Count / 3;
        Vector3[] pos = new Vector3[vertexCount];
        positions.CopyTo(pos);
        int[] tris = indices.ToArray();
        bool[] triAlive = new bool[triangleCount];
        Quadric[] quadrics = new Quadric[vertexCount];
        int[] versions = new int[vertexCount];
        List<int>[] vertexTris = new List<int>[vertexCount];

        Dictionary<long, int> edgeUse = new Dictionary<long, int>(triangleCount * 2);
        for (int t = 0; t < triangleCount; t++)
        {
            int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
            triAlive[t] = true;
            (vertexTris[i0] ??= new List<int>(8)).Add(t);
            (vertexTris[i1] ??= new List<int>(8)).Add(t);
            (vertexTris[i2] ??= new List<int>(8)).Add(t);

            Vector3 p0 = pos[i0], p1 = pos[i1], p2 = pos[i2];
            Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
            float area2 = cross.magnitude;
            if (area2 > 1e-12f)
            {
                Vector3 normal = cross / area2;
                double d = -Vector3.Dot(normal, p0);
                double weight = area2 * 0.5;
                quadrics[i0].AddPlane(normal.x, normal.y, normal.z, d, weight);
                quadrics[i1].AddPlane(normal.x, normal.y, normal.z, d, weight);
                quadrics[i2].AddPlane(normal.x, normal.y, normal.z, d, weight);
            }

            CountEdge(edgeUse, i0, i1);
            CountEdge(edgeUse, i1, i2);
            CountEdge(edgeUse, i2, i0);
        }

        // Boundary preservation: an edge used by exactly one triangle gets a heavy plane
        // through the edge, perpendicular to its face, so open sheets don't erode inward.
        for (int t = 0; t < triangleCount; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = tris[t * 3 + e];
                int b = tris[t * 3 + (e + 1) % 3];
                if (edgeUse[EdgeKey(a, b)] != 1)
                {
                    continue;
                }
                int c = tris[t * 3 + (e + 2) % 3];
                Vector3 edge = pos[b] - pos[a];
                Vector3 faceNormal = Vector3.Cross(edge, pos[c] - pos[a]);
                Vector3 planeNormal = Vector3.Cross(edge, faceNormal);
                float length = planeNormal.magnitude;
                if (length < 1e-12f)
                {
                    continue;
                }
                planeNormal /= length;
                double d = -Vector3.Dot(planeNormal, pos[a]);
                double weight = BoundaryPenalty * edge.sqrMagnitude;
                quadrics[a].AddPlane(planeNormal.x, planeNormal.y, planeNormal.z, d, weight);
                quadrics[b].AddPlane(planeNormal.x, planeNormal.y, planeNormal.z, d, weight);
            }
        }

        List<HeapEntry> heap = new List<HeapEntry>(edgeUse.Count + 64);
        foreach (KeyValuePair<long, int> pair in edgeUse)
        {
            int a = (int)(pair.Key >> 32);
            int b = (int)(pair.Key & 0xFFFFFFFF);
            HeapPush(heap, MakeEntry(a, b, pos, quadrics, versions));
        }
        edgeUse = null;

        int aliveTriangles = triangleCount;
        HashSet<int> neighborScratch = new HashSet<int>();
        List<int> mergedTris = new List<int>(16);

        while (aliveTriangles > targetTriangles && heap.Count > 0)
        {
            HeapEntry entry = HeapPop(heap);
            int va = entry.VertexA;
            int vb = entry.VertexB;
            if (versions[va] != entry.VersionA || versions[vb] != entry.VersionB)
            {
                continue;
            }
            List<int> trisA = vertexTris[va];
            List<int> trisB = vertexTris[vb];
            if (trisA == null || trisB == null)
            {
                continue;
            }

            // Reject collapses that flip any surviving triangle.
            if (WouldFlip(trisA, tris, triAlive, pos, va, vb, entry.Target) ||
                WouldFlip(trisB, tris, triAlive, pos, vb, va, entry.Target))
            {
                continue;
            }

            // Commit: vb merges into va at the target position.
            bool keepB = (entry.Target - pos[vb]).sqrMagnitude < (entry.Target - pos[va]).sqrMagnitude;
            if (keepB)
            {
                boneA[va] = boneA[vb];
                boneB[va] = boneB[vb];
                weightA[va] = weightA[vb];
            }
            // Hidden only survives a merge when both sides were hidden — a seam vertex
            // absorbing a visible one must count as visible for the texture pass.
            hiddenFlag[va] = (byte)(hiddenFlag[va] & hiddenFlag[vb]);
            pos[va] = entry.Target;
            quadrics[va].Add(in quadrics[vb]);
            versions[va]++;
            versions[vb]++;

            mergedTris.Clear();
            mergedTris.AddRange(trisA);
            for (int i = 0; i < trisB.Count; i++)
            {
                int t = trisB[i];
                if (!triAlive[t])
                {
                    continue;
                }
                bool containsA = false;
                for (int k = 0; k < 3; k++)
                {
                    int index = t * 3 + k;
                    if (tris[index] == vb)
                    {
                        tris[index] = va;
                    }
                    else if (tris[index] == va)
                    {
                        containsA = true;
                    }
                }
                if (containsA)
                {
                    triAlive[t] = false;
                    aliveTriangles--;
                }
                else
                {
                    mergedTris.Add(t);
                }
            }
            vertexTris[vb] = null;

            // Rebuild va's alive triangle list and prune newly degenerate faces.
            List<int> rebuilt = new List<int>(mergedTris.Count);
            for (int i = 0; i < mergedTris.Count; i++)
            {
                int t = mergedTris[i];
                if (!triAlive[t])
                {
                    continue;
                }
                int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
                if (i0 == i1 || i1 == i2 || i0 == i2)
                {
                    triAlive[t] = false;
                    aliveTriangles--;
                    continue;
                }
                if (!rebuilt.Contains(t))
                {
                    rebuilt.Add(t);
                }
            }
            vertexTris[va] = rebuilt;

            neighborScratch.Clear();
            for (int i = 0; i < rebuilt.Count; i++)
            {
                int t = rebuilt[i];
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t * 3 + k];
                    if (v != va)
                    {
                        neighborScratch.Add(v);
                    }
                }
            }
            foreach (int neighbor in neighborScratch)
            {
                HeapPush(heap, MakeEntry(va, neighbor, pos, quadrics, versions));
            }
        }

        // Write the surviving topology back into the shared lists.
        for (int i = 0; i < vertexCount; i++)
        {
            positions[i] = pos[i];
        }
        indices.Clear();
        for (int t = 0; t < triangleCount; t++)
        {
            if (!triAlive[t])
            {
                continue;
            }
            indices.Add(tris[t * 3]);
            indices.Add(tris[t * 3 + 1]);
            indices.Add(tris[t * 3 + 2]);
        }
    }

    private static bool WouldFlip(List<int> vertexTriangles, int[] tris, bool[] triAlive, Vector3[] pos, int movingVertex, int otherVertex, Vector3 target)
    {
        for (int i = 0; i < vertexTriangles.Count; i++)
        {
            int t = vertexTriangles[i];
            if (!triAlive[t])
            {
                continue;
            }
            int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
            if (i0 == otherVertex || i1 == otherVertex || i2 == otherVertex)
            {
                continue; // dies in the collapse
            }
            Vector3 p0 = pos[i0], p1 = pos[i1], p2 = pos[i2];
            Vector3 before = Vector3.Cross(p1 - p0, p2 - p0);
            if (i0 == movingVertex) p0 = target;
            else if (i1 == movingVertex) p1 = target;
            else if (i2 == movingVertex) p2 = target;
            Vector3 after = Vector3.Cross(p1 - p0, p2 - p0);
            float beforeLength = before.magnitude;
            if (beforeLength < 1e-12f)
            {
                continue;
            }
            if (Vector3.Dot(before / beforeLength, after) < FlipRejectDot * beforeLength)
            {
                return true;
            }
        }
        return false;
    }

    private static HeapEntry MakeEntry(int a, int b, Vector3[] pos, Quadric[] quadrics, int[] versions)
    {
        Quadric combined = quadrics[a];
        combined.Add(in quadrics[b]);
        Vector3 target;
        if (!combined.TrySolveOptimal(out target))
        {
            Vector3 mid = (pos[a] + pos[b]) * 0.5f;
            double costA = combined.Evaluate(pos[a]);
            double costB = combined.Evaluate(pos[b]);
            double costMid = combined.Evaluate(mid);
            target = costMid <= costA && costMid <= costB ? mid : (costA <= costB ? pos[a] : pos[b]);
        }
        return new HeapEntry
        {
            Cost = (float)combined.Evaluate(target),
            VertexA = a,
            VertexB = b,
            VersionA = versions[a],
            VersionB = versions[b],
            Target = target,
        };
    }

    private static long EdgeKey(int a, int b)
    {
        return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }

    private static void CountEdge(Dictionary<long, int> edgeUse, int a, int b)
    {
        long key = EdgeKey(a, b);
        edgeUse.TryGetValue(key, out int count);
        edgeUse[key] = count + 1;
    }

    private static void HeapPush(List<HeapEntry> heap, HeapEntry entry)
    {
        heap.Add(entry);
        int child = heap.Count - 1;
        while (child > 0)
        {
            int parent = (child - 1) >> 1;
            if (heap[parent].Cost <= heap[child].Cost)
            {
                break;
            }
            (heap[parent], heap[child]) = (heap[child], heap[parent]);
            child = parent;
        }
    }

    private static HeapEntry HeapPop(List<HeapEntry> heap)
    {
        HeapEntry top = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        int parent = 0;
        while (true)
        {
            int left = parent * 2 + 1;
            if (left >= heap.Count)
            {
                break;
            }
            int right = left + 1;
            int smallest = right < heap.Count && heap[right].Cost < heap[left].Cost ? right : left;
            if (heap[parent].Cost <= heap[smallest].Cost)
            {
                break;
            }
            (heap[parent], heap[smallest]) = (heap[smallest], heap[parent]);
            parent = smallest;
        }
        return top;
    }

    private static void ClusterFallback(List<Vector3> positions, List<byte> boneA, List<byte> boneB, List<byte> weightA, List<int> indices, int targetTriangles)
    {
        Vector3 min = positions[0];
        Vector3 max = positions[0];
        for (int i = 1; i < positions.Count; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }
        float diag = Mathf.Max((max - min).magnitude, 1e-4f);

        for (int resolution = 48; resolution >= 4; resolution -= 8)
        {
            float cell = diag / resolution;
            Dictionary<Vector3Int, int> cellToVertex = new Dictionary<Vector3Int, int>();
            int[] remap = new int[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 scaled = (positions[i] - min) / cell;
                Vector3Int key = new Vector3Int((int)scaled.x, (int)scaled.y, (int)scaled.z);
                if (cellToVertex.TryGetValue(key, out int existing))
                {
                    remap[i] = existing;
                }
                else
                {
                    cellToVertex[key] = i;
                    remap[i] = i;
                }
            }
            List<int> remapped = new List<int>(indices.Count);
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = remap[indices[i]], b = remap[indices[i + 1]], c = remap[indices[i + 2]];
                if (a == b || b == c || a == c)
                {
                    continue;
                }
                remapped.Add(a);
                remapped.Add(b);
                remapped.Add(c);
            }
            if (remapped.Count / 3 <= targetTriangles || resolution == 4)
            {
                indices.Clear();
                indices.AddRange(remapped);
                return;
            }
        }
    }

    private static void Compact(List<Vector3> positions, List<byte> boneA, List<byte> boneB, List<byte> weightA, List<byte> hiddenFlag, List<int> indices)
    {
        int[] remap = new int[positions.Count];
        for (int i = 0; i < remap.Length; i++)
        {
            remap[i] = -1;
        }
        List<Vector3> newPositions = new List<Vector3>(indices.Count);
        List<byte> newBoneA = new List<byte>(indices.Count);
        List<byte> newBoneB = new List<byte>(indices.Count);
        List<byte> newWeightA = new List<byte>(indices.Count);
        List<byte> newHidden = new List<byte>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
        {
            int old = indices[i];
            if (remap[old] < 0)
            {
                remap[old] = newPositions.Count;
                newPositions.Add(positions[old]);
                newBoneA.Add(boneA[old]);
                newBoneB.Add(boneB[old]);
                newWeightA.Add(weightA[old]);
                newHidden.Add(hiddenFlag[old]);
            }
            indices[i] = remap[old];
        }
        positions.Clear();
        positions.AddRange(newPositions);
        boneA.Clear();
        boneA.AddRange(newBoneA);
        boneB.Clear();
        boneB.AddRange(newBoneB);
        weightA.Clear();
        weightA.AddRange(newWeightA);
        hiddenFlag.Clear();
        hiddenFlag.AddRange(newHidden);
    }
}
