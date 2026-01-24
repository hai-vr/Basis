using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Basis.Scripts.UI.UI_Panels
{
    /// <summary>
    /// Separate keystore for PROPS (so props don’t collide with avatar keys).
    /// Writes to: Application.persistentDataPath/PropKeyStore.json
    /// </summary>
    public static class BasisDataStorePropKeys
    {
        [System.Serializable]
        public class PropKey
        {
            public string Url;
            public string Pass;
            public bool Persistent;
        }

        [System.Serializable]
        public class PropKeys
        {
            [SerializeField]
            public PropKey[] Data;
        }

        public static string FilePath = Path.Combine(Application.persistentDataPath, "PropKeyStore.json");

        [SerializeField]
        private static PropKeys keys = new PropKeys { Data = System.Array.Empty<PropKey>() };

        public static async Task AddNewKey(PropKey newKey)
        {
            EnsureInit();

            if (!ContainsKey(newKey))
            {
                int oldLen = keys.Data.Length;
                System.Array.Resize(ref keys.Data, oldLen + 1);
                keys.Data[oldLen] = newKey;

                await SaveKeysToFile();
                BasisDebug.Log($"Prop key added: {newKey.Url}");
            }
        }

        public static async Task RemoveKey(PropKey keyToRemove)
        {
            EnsureInit();

            int index = IndexOfKey(keyToRemove);
            if (index < 0)
            {
                BasisDebug.Log("Prop key not found.");
                return;
            }

            int oldLen = keys.Data.Length;
            var newArr = new PropKey[oldLen - 1];

            if (index > 0)
                System.Array.Copy(keys.Data, 0, newArr, 0, index);

            if (index < oldLen - 1)
                System.Array.Copy(keys.Data, index + 1, newArr, index, oldLen - index - 1);

            keys.Data = newArr;

            await SaveKeysToFile();
            BasisDebug.Log($"Prop key removed: {keyToRemove.Url}");
        }

        public static async Task LoadKeys()
        {
            BasisDebug.Log($"Loading prop keys from file at path: {FilePath}");

            EnsureInit();

            if (!File.Exists(FilePath))
            {
                BasisDebug.Log("No prop key file found. Starting fresh.");
                keys.Data = System.Array.Empty<PropKey>();
                return;
            }

            try
            {
                byte[] byteData = await File.ReadAllBytesAsync(FilePath);

                // Deserialize the container (which contains a PropKey[]).
                keys = BasisSerialization.DeserializeValue<PropKeys>(byteData);

                if (keys == null)
                    keys = new PropKeys();

                if (keys.Data == null)
                    keys.Data = System.Array.Empty<PropKey>();

                BasisDebug.Log("Prop keys loaded successfully. Count: " + keys.Data.Length);
            }
            catch (System.Exception e)
            {
                BasisDebug.LogError($"Failed to load prop keys: {e.Message}");
                keys = new PropKeys { Data = System.Array.Empty<PropKey>() };
            }
        }

        private static async Task SaveKeysToFile()
        {
            EnsureInit();

            try
            {
                byte[] byteData = BasisSerialization.SerializeValue(keys);
                await File.WriteAllBytesAsync(FilePath, byteData);

                BasisDebug.Log($"Prop keys saved to file at: {FilePath}");
            }
            catch (System.Exception e)
            {
                BasisDebug.LogError($"Failed to save prop keys: {e.Message}");
            }
        }

        public static PropKey[] DisplayKeys()
        {
            EnsureInit();
            return keys.Data;
        }

        // ---------- Helpers (array-only) ----------

        private static void EnsureInit()
        {
            if (keys == null)
                keys = new PropKeys();

            if (keys.Data == null)
                keys.Data = System.Array.Empty<PropKey>();
        }

        private static bool ContainsKey(PropKey k) => IndexOfKey(k) >= 0;

        private static int IndexOfKey(PropKey k)
        {
            if (k == null) return -1;

            // Compare by Url+Pass (value equality) rather than reference equality.
            for (int i = 0; i < keys.Data.Length; i++)
            {
                var cur = keys.Data[i];
                if (cur != null && cur.Url == k.Url && cur.Pass == k.Pass)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
