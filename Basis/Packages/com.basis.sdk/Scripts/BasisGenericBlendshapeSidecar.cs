using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Blendshape payload for the Generic (glTF) avatar section. glTFast's exporter writes no
/// morph targets, so the GLB alone loses every blendshape; this sidecar carries the shapes the
/// avatar actually actuates (visemes, blink, laughter) as sparse deltas and is appended after
/// the GLB inside the encrypted section — the GLB itself stays a pristine, standard file. On
/// import the shapes are rebuilt onto the (still readable) glTFast meshes with
/// <c>Mesh.AddBlendShapeFrame</c> at frame weight 100, so Basis drivers write 0..100 weights
/// exactly like they do on AssetBundle avatars. Deltas are stored pre-scaled to full
/// application at weight 100, which flattens multi-frame source shapes to their final frame.
///
/// The payload arrives over the network, so parsing is defensive: hard caps on every count and
/// any violation aborts the parse — a bad sidecar costs the shapes, never the avatar.
/// </summary>
public class BasisGenericBlendshapeSidecar
{
    public const uint MagicValue = 0x53424742; // "BGBS" little-endian
    public const ushort CurrentVersion = 1;

    public const int MaxMeshes = 32;
    public const int MaxShapesPerMesh = 256;
    public const int MaxPathBytes = 1024;
    public const int MaxNameBytes = 512;

    [Serializable]
    public class SidecarShape
    {
        public string Name;
        public int[] SparseIndices;
        public Vector3[] DeltaPositions;
        public Vector3[] DeltaNormals;
    }

    [Serializable]
    public class SidecarMesh
    {
        public string Path; // relative to the avatar root, Transform.Find syntax
        public int VertexCount;
        public List<SidecarShape> Shapes = new List<SidecarShape>();
    }

    public List<SidecarMesh> Meshes = new List<SidecarMesh>();

    public bool HasContent
    {
        get
        {
            for (int Index = 0; Index < Meshes.Count; Index++)
            {
                if (Meshes[Index].Shapes.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public byte[] Serialize()
    {
        using MemoryStream stream = new MemoryStream(256 * 1024);
        using BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(MagicValue);
        writer.Write(CurrentVersion);
        writer.Write((ushort)0);
        writer.Write((byte)Meshes.Count);
        for (int meshIndex = 0; meshIndex < Meshes.Count; meshIndex++)
        {
            SidecarMesh mesh = Meshes[meshIndex];
            WriteString(writer, mesh.Path);
            writer.Write(mesh.VertexCount);
            writer.Write((ushort)mesh.Shapes.Count);
            for (int shapeIndex = 0; shapeIndex < mesh.Shapes.Count; shapeIndex++)
            {
                SidecarShape shape = mesh.Shapes[shapeIndex];
                WriteString(writer, shape.Name);
                int sparseCount = shape.SparseIndices.Length;
                writer.Write(sparseCount);
                bool wideIndices = mesh.VertexCount > ushort.MaxValue;
                writer.Write((byte)(wideIndices ? 4 : 2));
                for (int i = 0; i < sparseCount; i++)
                {
                    if (wideIndices)
                    {
                        writer.Write(shape.SparseIndices[i]);
                    }
                    else
                    {
                        writer.Write((ushort)shape.SparseIndices[i]);
                    }
                }
                for (int i = 0; i < sparseCount; i++)
                {
                    WriteVector3(writer, shape.DeltaPositions[i]);
                }
                for (int i = 0; i < sparseCount; i++)
                {
                    WriteVector3(writer, shape.DeltaNormals[i]);
                }
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Parses a sidecar starting at <paramref name="offset"/>. Returns null on anything
    /// suspicious — magic mismatch, newer version, or any count outside its cap.
    /// </summary>
    public static BasisGenericBlendshapeSidecar TryParse(byte[] bytes, int offset)
    {
        if (bytes == null || offset < 0 || bytes.Length - offset < 16)
        {
            return null;
        }
        try
        {
            using MemoryStream stream = new MemoryStream(bytes, offset, bytes.Length - offset, false);
            using BinaryReader reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != MagicValue)
            {
                return null;
            }
            ushort version = reader.ReadUInt16();
            if (version == 0 || version > CurrentVersion)
            {
                BasisDebug.LogError($"Blendshape sidecar version {version} is newer than supported {CurrentVersion}", BasisDebug.LogTag.Avatar);
                return null;
            }
            reader.ReadUInt16();

            BasisGenericBlendshapeSidecar sidecar = new BasisGenericBlendshapeSidecar();
            int meshCount = reader.ReadByte();
            if (meshCount > MaxMeshes)
            {
                return null;
            }
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                SidecarMesh mesh = new SidecarMesh
                {
                    Path = ReadString(reader, MaxPathBytes),
                    VertexCount = reader.ReadInt32(),
                };
                if (mesh.Path == null || mesh.VertexCount <= 0)
                {
                    return null;
                }
                int shapeCount = reader.ReadUInt16();
                if (shapeCount > MaxShapesPerMesh)
                {
                    return null;
                }
                for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
                {
                    SidecarShape shape = new SidecarShape { Name = ReadString(reader, MaxNameBytes) };
                    if (shape.Name == null)
                    {
                        return null;
                    }
                    int sparseCount = reader.ReadInt32();
                    if (sparseCount < 0 || sparseCount > mesh.VertexCount)
                    {
                        return null;
                    }
                    byte indexWidth = reader.ReadByte();
                    if (indexWidth != 2 && indexWidth != 4)
                    {
                        return null;
                    }
                    shape.SparseIndices = new int[sparseCount];
                    for (int i = 0; i < sparseCount; i++)
                    {
                        shape.SparseIndices[i] = indexWidth == 4 ? reader.ReadInt32() : reader.ReadUInt16();
                        if (shape.SparseIndices[i] < 0 || shape.SparseIndices[i] >= mesh.VertexCount)
                        {
                            return null;
                        }
                    }
                    shape.DeltaPositions = ReadVector3Array(reader, sparseCount);
                    shape.DeltaNormals = ReadVector3Array(reader, sparseCount);
                    mesh.Shapes.Add(shape);
                }
                sidecar.Meshes.Add(mesh);
            }
            return sidecar;
        }
        catch (Exception e)
        {
            BasisDebug.LogError($"Blendshape sidecar parse failed: {e.Message}", BasisDebug.LogTag.Avatar);
            return null;
        }
    }

    /// <summary>
    /// Rebuilds the carried shapes onto the imported hierarchy's meshes. Shared meshes are only
    /// processed once (multiple nodes can reference one glTF mesh), and a mesh whose vertex
    /// count doesn't match is skipped with a log rather than corrupted.
    /// </summary>
    public void ApplyTo(Transform root)
    {
        HashSet<Mesh> processed = new HashSet<Mesh>();
        for (int meshIndex = 0; meshIndex < Meshes.Count; meshIndex++)
        {
            SidecarMesh entry = Meshes[meshIndex];
            Transform node = BasisGenericAvatarData.ResolveByPath(root, entry.Path);
            if (node == null || !node.TryGetComponent(out SkinnedMeshRenderer renderer) || renderer.sharedMesh == null)
            {
                BasisDebug.LogError($"Blendshape sidecar: mesh node '{entry.Path}' not found on imported avatar.", BasisDebug.LogTag.Avatar);
                continue;
            }
            Mesh mesh = renderer.sharedMesh;
            if (!processed.Add(mesh))
            {
                continue;
            }
            if (mesh.vertexCount != entry.VertexCount)
            {
                BasisDebug.LogError($"Blendshape sidecar: '{entry.Path}' vertex count {mesh.vertexCount} != recorded {entry.VertexCount}; shapes skipped.", BasisDebug.LogTag.Avatar);
                continue;
            }

            int vertexCount = mesh.vertexCount;
            Vector3[] deltaPositions = new Vector3[vertexCount];
            Vector3[] deltaNormals = new Vector3[vertexCount];
            for (int shapeIndex = 0; shapeIndex < entry.Shapes.Count; shapeIndex++)
            {
                SidecarShape shape = entry.Shapes[shapeIndex];
                if (mesh.GetBlendShapeIndex(shape.Name) >= 0)
                {
                    continue;
                }
                Array.Clear(deltaPositions, 0, vertexCount);
                Array.Clear(deltaNormals, 0, vertexCount);
                int sparseCount = shape.SparseIndices.Length;
                for (int i = 0; i < sparseCount; i++)
                {
                    int vertex = shape.SparseIndices[i];
                    deltaPositions[vertex] = shape.DeltaPositions[i];
                    deltaNormals[vertex] = shape.DeltaNormals[i];
                }
                try
                {
                    mesh.AddBlendShapeFrame(shape.Name, 100f, deltaPositions, deltaNormals, null);
                }
                catch (Exception e)
                {
                    BasisDebug.LogError($"Blendshape sidecar: AddBlendShapeFrame '{shape.Name}' failed: {e.Message}", BasisDebug.LogTag.Avatar);
                }
            }
        }
    }

    /// <summary>
    /// Length in bytes of the GLB the section starts with (its own header's total-length
    /// field), or -1 when the bytes aren't a GLB. Everything after that is sidecar territory.
    /// </summary>
    public static int GetGlbLength(byte[] sectionBytes)
    {
        if (sectionBytes == null || sectionBytes.Length < 12)
        {
            return -1;
        }
        if (sectionBytes[0] != 0x67 || sectionBytes[1] != 0x6C || sectionBytes[2] != 0x54 || sectionBytes[3] != 0x46)
        {
            return -1;
        }
        uint length = BitConverter.ToUInt32(sectionBytes, 8);
        if (length < 12 || length > sectionBytes.Length)
        {
            return -1;
        }
        return (int)length;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader, int maxBytes)
    {
        int length = reader.ReadUInt16();
        if (length > maxBytes)
        {
            return null;
        }
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            return null;
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    private static Vector3[] ReadVector3Array(BinaryReader reader, int count)
    {
        Vector3[] values = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        return values;
    }
}
