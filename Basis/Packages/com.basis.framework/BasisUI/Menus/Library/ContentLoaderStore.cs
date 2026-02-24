using System;
using System.Threading.Tasks;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;

namespace Basis.BasisUI
{
    public static class ContentLoaderStore
    {
        [Serializable]
        public class LoadedItem
        {
            public int InstanceId;
            public BasisDataStoreItemKeys.ItemKey ItemKey;
            public GameObject GameObject;
        }

        [Serializable]
        private class LoadedItemsContainer
        {
            [SerializeField]
            public LoadedItem[] Data;
        }

        [SerializeField]
        private static LoadedItemsContainer items = new LoadedItemsContainer { Data = Array.Empty<LoadedItem>() };

        public static async Task Add(
            BasisDataStoreItemKeys.ItemKey key,
            GameObject go)
        {
            EnsureInit();

            if (go == null)
                return;

            int id = go.GetInstanceID();

            int index = IndexOf(id);
            if (index >= 0)
            {
                items.Data[index].ItemKey = key;
                items.Data[index].GameObject = go;
                return;
            }

            int oldLen = items.Data.Length;
            Array.Resize(ref items.Data, oldLen + 1);

            items.Data[oldLen] = new LoadedItem
            {
                InstanceId = id,
                ItemKey = key,
                GameObject = go
            };

            await Task.CompletedTask;
        }

        public static async Task Remove(GameObject go)
        {
            if (go == null)
                return;

            await Remove(go.GetInstanceID());
        }

        public static async Task Remove(int instanceId)
        {
            EnsureInit();

            int index = IndexOf(instanceId);
            if (index < 0)
                return;

            int oldLen = items.Data.Length;
            var newArr = new LoadedItem[oldLen - 1];

            if (index > 0)
                Array.Copy(items.Data, 0, newArr, 0, index);

            if (index < oldLen - 1)
                Array.Copy(items.Data, index + 1, newArr, index, oldLen - index - 1);

            items.Data = newArr;

            await Task.CompletedTask;
        }

        public static async Task<(bool found, BasisDataStoreItemKeys.ItemKey key, GameObject go)> TryGet(int instanceId)
        {
            EnsureInit();

            int index = IndexOf(instanceId);

            if (index >= 0)
            {
                var entry = items.Data[index];
                return (true, entry.ItemKey, entry.GameObject);
            }

            return (false, default, null);
        }

        public static async Task<LoadedItem[]> GetAll()
        {
            EnsureInit();
            return items.Data;
        }

        private static void EnsureInit()
        {
            items ??= new LoadedItemsContainer();
            items.Data ??= Array.Empty<LoadedItem>();
        }

        private static int IndexOf(int instanceId)
        {
            for (int i = 0; i < items.Data.Length; i++)
            {
                if (items.Data[i].InstanceId == instanceId)
                    return i;
            }
            return -1;
        }
    }
}