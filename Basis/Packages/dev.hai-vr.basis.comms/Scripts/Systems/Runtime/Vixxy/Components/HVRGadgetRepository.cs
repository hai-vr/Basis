using System.Collections.Generic;
using UnityEngine;

namespace HVR.Vixxy
{
    public class HVRGadgetRepository : MonoBehaviour
    {
        private readonly List<ScriptableObject> _repository = new();

        public event GadgetListChanged OnGadgetListChanged;
        public delegate void GadgetListChanged();

        public void Add(ScriptableObject menuItem)
        {
            if (_repository.Contains(menuItem)) return;

            _repository.Add(menuItem);
            OnGadgetListChanged?.Invoke();
        }

        public void Remove(ScriptableObject menuItem)
        {
            var removalDone = _repository.Remove(menuItem);
            if (removalDone)
            {
                OnGadgetListChanged?.Invoke();
            }
        }

        public IEnumerable<ScriptableObject> GadgetView()
        {
            return _repository;
        }
    }
}
