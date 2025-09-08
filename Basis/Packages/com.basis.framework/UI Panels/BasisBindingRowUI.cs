using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace Basis.Scripts.UI.UI_Panels
{
    public class BasisBindingRowUI : MonoBehaviour
    {
        [SerializeField] public TextMeshProUGUI label;
        [SerializeField] public TMP_Dropdown dropdown;
        private Action<int> _onValueChanged;
        private void Awake()
        {
            dropdown.onValueChanged.AddListener(OnDropdownChanged);
        }
        private void OnDestroy()
        {
            dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        }
        public void SetLabel(string text)
        {
            label.text = text;
        }
        public void SetOptions(IList<string> options)
        {
            dropdown.ClearOptions();

            var list = new List<TMP_Dropdown.OptionData>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                list.Add(new TMP_Dropdown.OptionData(options[i]));
            }

            dropdown.AddOptions(list);
        }
        public void SetOnValueChanged(Action<int> onChanged)
        {
            _onValueChanged = onChanged;
        }
        public void SetValueWithoutNotify(int index)
        {
            dropdown.SetValueWithoutNotify(index);
        }
        private void OnDropdownChanged(int index)
        {
            _onValueChanged?.Invoke(index);
        }
    }
}
