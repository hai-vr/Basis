using System;
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
        private static LoadedItemsContainer items = new LoadedItemsContainer
        {
            Data = Array.Empty<LoadedItem>()
        };

        public static void Add(
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
        }

        public static void Remove(GameObject go)
        {
            if (go == null)
                return;

            Remove(go.GetInstanceID());
        }

        public static void Remove(int instanceId)
        {
            EnsureInit();

            int index = IndexOf(instanceId);
            if (index < 0)
                return;

            int oldLen = items.Data.Length;

            if (oldLen == 1)
            {
                items.Data = Array.Empty<LoadedItem>();
                return;
            }

            var newArr = new LoadedItem[oldLen - 1];

            if (index > 0)
                Array.Copy(items.Data, 0, newArr, 0, index);

            if (index < oldLen - 1)
                Array.Copy(items.Data, index + 1, newArr, index, oldLen - index - 1);

            items.Data = newArr;
        }

        public static (bool found, BasisDataStoreItemKeys.ItemKey key, GameObject go) TryGet(int instanceId)
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

        public static LoadedItem[] GetAll()
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
            var data = items.Data;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].InstanceId == instanceId)
                    return i;
            }

            return -1;
        }
    }
}