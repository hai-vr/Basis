using System;
using System.Text;
using UnityEngine;

public static class basisSerialize
{
    public static byte[] SerializeValue<T>(T value, DataFormat format)
    {
        switch (format)
        {
            case DataFormat.JSON:
                {
                    // Unity JsonUtility needs a wrapper for non-class roots sometimes
                    var json = JsonUtility.ToJson(new Wrapper<T> { Value = value }, prettyPrint: false);
                    return Encoding.UTF8.GetBytes(json);
                }

            case DataFormat.Binary:
                // You choose what "binary" means.
                // Common choice: UTF8 JSON bytes (still "binary" bytes), or MessagePack, etc.
                // For now, do JSON bytes to keep it simple and deterministic.
                var jsonBytes = SerializeValue(value, DataFormat.JSON);
                return jsonBytes;

            default:
                throw new NotSupportedException($"Unsupported format: {format}");
        }
    }

    public static T DeserializeValue<T>(byte[] data, DataFormat format)
    {
        if (data == null || data.Length == 0) return default;

        switch (format)
        {
            case DataFormat.JSON:
                {
                    var json = Encoding.UTF8.GetString(data);
                    var wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
                    return wrapper != null ? wrapper.Value : default;
                }

            case DataFormat.Binary:
                // Must match SerializeValue's Binary branch.
                return DeserializeValue<T>(data, DataFormat.JSON);

            default:
                throw new NotSupportedException($"Unsupported format: {format}");
        }
    }
    public enum DataFormat
    {
        Binary,
        JSON
    }
    [Serializable]
    private class Wrapper<TW>
    {
        public TW Value;
    }
}
