using System;
using System.Collections.Generic;
using System.Text;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;

public static class BasisHandHeldCameraPhotoMetadata
{
    public static List<string> CollectTaggedNames(Camera captureCamera)
    {
        var names = new List<string>();
        if (captureCamera == null)
            return names;

        string mode = BasisSettingsDefaults.PhotoMetadataTagging.RawValue;

        if (string.IsNullOrEmpty(mode) || mode == BasisSettingsDefaults.PhotoTagging_NoOne)
            return names;

        if (mode == BasisSettingsDefaults.PhotoTagging_JustMe)
        {
            AddName(names, BasisLocalPlayer.Instance);
            return names;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(captureCamera);
        BasisNetworkPlayer[] players = BasisNetworkPlayer.GetAllPlayers();
        for (int i = 0; i < players.Length; i++)
        {
            BasisNetworkPlayer networkPlayer = players[i];
            if (networkPlayer == null || !networkPlayer.TryGetPlayer(out BasisPlayer player) || player == null)
                continue;

            if (IsInsideFrustum(player, planes))
                AddName(names, player);
        }
        return names;
    }

    private static void AddName(List<string> names, BasisPlayer player)
    {
        if (player == null)
            return;

        string name = player.SafeDisplayName;
        if (string.IsNullOrEmpty(name))
            name = player.DisplayName;

        if (!string.IsNullOrEmpty(name) && !names.Contains(name))
            names.Add(name);
    }

    private static bool IsInsideFrustum(BasisPlayer player, Plane[] planes)
    {
        var avatar = player.BasisAvatar;
        if (avatar != null && avatar.Renders != null && avatar.Renders.Length > 0)
        {
            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < avatar.Renders.Length; i++)
            {
                Renderer renderer = avatar.Renders[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            if (hasBounds)
                return GeometryUtility.TestPlanesAABB(planes, bounds);
        }

        Transform avatarTransform = player.AvatarTransform != null ? player.AvatarTransform : player.transform;
        if (avatarTransform == null)
            return false;

        Vector3 point = avatarTransform.position;
        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i].GetDistanceToPoint(point) < 0f)
                return false;
        }
        return true;
    }

    public static byte[] Embed(byte[] imageData, string captureFormat, IReadOnlyList<string> names)
    {
        if (imageData == null || names == null || names.Count == 0)
            return imageData;

        string people = "People in photo: " + JoinNames(names);
        string json = BuildPeopleJson(names);

        try
        {
            if (string.Equals(captureFormat, "EXR", StringComparison.OrdinalIgnoreCase))
                return EmbedExr(imageData, people, json);

            return EmbedPng(imageData, people, json);
        }
        catch (Exception ex)
        {
            BasisDebug.LogWarning($"[PhotoMetadata] Could not embed photo metadata: {ex.Message}");
            return imageData;
        }
    }

    private static string JoinNames(IReadOnlyList<string> names)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < names.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(names[i]);
        }
        return sb.ToString();
    }

    private static string BuildPeopleJson(IReadOnlyList<string> names)
    {
        var sb = new StringBuilder();
        sb.Append("{\"people\":[");
        for (int i = 0; i < names.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append('"').Append(EscapeJson(names[i])).Append('"');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string EscapeJson(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // ---------------- PNG ----------------

    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private static byte[] EmbedPng(byte[] png, string people, string json)
    {
        if (png.Length < PngSignature.Length + 12)
            return png;
        for (int i = 0; i < PngSignature.Length; i++)
        {
            if (png[i] != PngSignature[i])
                return png;
        }

        int iendStart = -1;
        int pos = PngSignature.Length;
        while (pos + 8 <= png.Length)
        {
            long length = ReadUInt32BE(png, pos);
            if (length < 0 || pos + 12 + length > png.Length)
                break;

            if (png[pos + 4] == 'I' && png[pos + 5] == 'E' && png[pos + 6] == 'N' && png[pos + 7] == 'D')
            {
                iendStart = pos;
                break;
            }
            pos += (int)(12 + length);
        }
        if (iendStart < 0)
            return png;

        byte[] descChunk = BuildITxtChunk("Description", people);
        byte[] jsonChunk = BuildITxtChunk("BasisPeople", json);

        byte[] result = new byte[png.Length + descChunk.Length + jsonChunk.Length];
        int o = 0;
        Buffer.BlockCopy(png, 0, result, o, iendStart); o += iendStart;
        Buffer.BlockCopy(descChunk, 0, result, o, descChunk.Length); o += descChunk.Length;
        Buffer.BlockCopy(jsonChunk, 0, result, o, jsonChunk.Length); o += jsonChunk.Length;
        Buffer.BlockCopy(png, iendStart, result, o, png.Length - iendStart);
        return result;
    }

    // iTXt data: keyword + 0x00 + compFlag(0) + compMethod(0) + langTag + 0x00 + transKeyword + 0x00 + UTF-8 text
    private static byte[] BuildITxtChunk(string keyword, string text)
    {
        byte[] keywordBytes = Encoding.ASCII.GetBytes(keyword);
        byte[] textBytes = Encoding.UTF8.GetBytes(text);

        byte[] data = new byte[keywordBytes.Length + 5 + textBytes.Length];
        int p = 0;
        Buffer.BlockCopy(keywordBytes, 0, data, p, keywordBytes.Length); p += keywordBytes.Length;
        data[p++] = 0; // keyword terminator
        data[p++] = 0; // compression flag
        data[p++] = 0; // compression method
        data[p++] = 0; // empty language tag terminator
        data[p++] = 0; // empty translated keyword terminator
        Buffer.BlockCopy(textBytes, 0, data, p, textBytes.Length);

        byte[] type = { (byte)'i', (byte)'T', (byte)'X', (byte)'t' };

        byte[] chunk = new byte[12 + data.Length];
        int c = 0;
        WriteUInt32BE(chunk, c, (uint)data.Length); c += 4;
        Buffer.BlockCopy(type, 0, chunk, c, 4); c += 4;
        Buffer.BlockCopy(data, 0, chunk, c, data.Length); c += data.Length;
        WriteUInt32BE(chunk, c, Crc32(type, data));
        return chunk;
    }

    private static long ReadUInt32BE(byte[] data, int offset)
    {
        return ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | data[offset + 3];
    }

    private static void WriteUInt32BE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private static uint[] _crcTable;

    private static uint Crc32(byte[] type, byte[] data)
    {
        if (_crcTable == null)
        {
            _crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                _crcTable[n] = c;
            }
        }

        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < type.Length; i++)
            crc = _crcTable[(crc ^ type[i]) & 0xFF] ^ (crc >> 8);
        for (int i = 0; i < data.Length; i++)
            crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    // ---------------- EXR ----------------

    private static byte[] EmbedExr(byte[] exr, string people, string json)
    {
        if (exr.Length < 8)
            return exr;
        if (exr[0] != 0x76 || exr[1] != 0x2f || exr[2] != 0x31 || exr[3] != 0x01)
            return exr;

        int flags = exr[5] | (exr[6] << 8) | (exr[7] << 16);
        const int Tiled = 0x200;
        const int Deep = 0x800;
        const int Multipart = 0x1000;
        if ((flags & (Tiled | Deep | Multipart)) != 0)
            return exr;

        int p = 8;
        int compression = -1;
        int yMin = 0, yMax = -1;
        bool haveDataWindow = false;

        while (true)
        {
            if (p >= exr.Length)
                return exr;
            if (exr[p] == 0) { p++; break; }

            int nameStart = p;
            while (p < exr.Length && exr[p] != 0) p++;
            if (p >= exr.Length) return exr;
            string attrName = Encoding.ASCII.GetString(exr, nameStart, p - nameStart);
            p++;

            int typeStart = p;
            while (p < exr.Length && exr[p] != 0) p++;
            if (p >= exr.Length) return exr;
            string attrType = Encoding.ASCII.GetString(exr, typeStart, p - typeStart);
            p++;

            if (p + 4 > exr.Length) return exr;
            int attrSize = exr[p] | (exr[p + 1] << 8) | (exr[p + 2] << 16) | (exr[p + 3] << 24);
            p += 4;
            if (attrSize < 0 || p + attrSize > exr.Length) return exr;

            if (attrName == "compression" && attrSize >= 1)
            {
                compression = exr[p];
            }
            else if (attrName == "dataWindow" && attrType == "box2i" && attrSize >= 16)
            {
                yMin = exr[p + 4] | (exr[p + 5] << 8) | (exr[p + 6] << 16) | (exr[p + 7] << 24);
                yMax = exr[p + 12] | (exr[p + 13] << 8) | (exr[p + 14] << 16) | (exr[p + 15] << 24);
                haveDataWindow = true;
            }

            p += attrSize;
        }

        int headerTerminator = p - 1;
        int offsetTableStart = p;

        if (!haveDataWindow || compression < 0)
            return exr;

        int linesPerBlock = LinesPerBlock(compression);
        if (linesPerBlock <= 0)
            return exr;

        int height = yMax - yMin + 1;
        if (height <= 0)
            return exr;

        int chunkCount = (height + linesPerBlock - 1) / linesPerBlock;
        long offsetTableBytes = (long)chunkCount * 8;
        if (offsetTableStart + offsetTableBytes > exr.Length)
            return exr;

        // The first chunk must start immediately after the offset table; if it
        // doesn't, our derived chunk count is wrong, so leave the file untouched.
        long firstOffset = ReadInt64LE(exr, offsetTableStart);
        if (firstOffset != offsetTableStart + offsetTableBytes)
            return exr;

        byte[] commentsAttr = BuildExrStringAttribute("comments", people);
        byte[] jsonAttr = BuildExrStringAttribute("BasisPeople", json);
        int delta = commentsAttr.Length + jsonAttr.Length;

        byte[] result = new byte[exr.Length + delta];
        int o = 0;
        Buffer.BlockCopy(exr, 0, result, o, headerTerminator); o += headerTerminator;
        Buffer.BlockCopy(commentsAttr, 0, result, o, commentsAttr.Length); o += commentsAttr.Length;
        Buffer.BlockCopy(jsonAttr, 0, result, o, jsonAttr.Length); o += jsonAttr.Length;
        Buffer.BlockCopy(exr, headerTerminator, result, o, exr.Length - headerTerminator);

        int newOffsetTableStart = offsetTableStart + delta;
        for (int i = 0; i < chunkCount; i++)
        {
            int entryPos = newOffsetTableStart + i * 8;
            WriteInt64LE(result, entryPos, ReadInt64LE(result, entryPos) + delta);
        }
        return result;
    }

    private static int LinesPerBlock(int compression)
    {
        switch (compression)
        {
            case 0: return 1;   // none
            case 1: return 1;   // RLE
            case 2: return 1;   // ZIPS
            case 3: return 16;  // ZIP
            case 4: return 32;  // PIZ
            case 5: return 16;  // PXR24
            case 6: return 32;  // B44
            case 7: return 32;  // B44A
            case 8: return 32;  // DWAA
            case 9: return 256; // DWAB
            default: return 0;
        }
    }

    // EXR attribute: name + 0x00 + "string" + 0x00 + int32 size + UTF-8 value
    private static byte[] BuildExrStringAttribute(string name, string value)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] typeBytes = Encoding.ASCII.GetBytes("string");
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);

        byte[] attr = new byte[nameBytes.Length + 1 + typeBytes.Length + 1 + 4 + valueBytes.Length];
        int p = 0;
        Buffer.BlockCopy(nameBytes, 0, attr, p, nameBytes.Length); p += nameBytes.Length;
        attr[p++] = 0;
        Buffer.BlockCopy(typeBytes, 0, attr, p, typeBytes.Length); p += typeBytes.Length;
        attr[p++] = 0;
        attr[p++] = (byte)(valueBytes.Length & 0xFF);
        attr[p++] = (byte)((valueBytes.Length >> 8) & 0xFF);
        attr[p++] = (byte)((valueBytes.Length >> 16) & 0xFF);
        attr[p++] = (byte)((valueBytes.Length >> 24) & 0xFF);
        Buffer.BlockCopy(valueBytes, 0, attr, p, valueBytes.Length);
        return attr;
    }

    private static long ReadInt64LE(byte[] data, int offset)
    {
        long v = 0;
        for (int i = 0; i < 8; i++)
            v |= (long)data[offset + i] << (8 * i);
        return v;
    }

    private static void WriteInt64LE(byte[] data, int offset, long value)
    {
        for (int i = 0; i < 8; i++)
            data[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
    }
}
