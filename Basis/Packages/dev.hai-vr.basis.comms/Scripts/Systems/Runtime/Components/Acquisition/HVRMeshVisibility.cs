using System;
using System.Collections.Generic;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization/mesh-visibility")]
    [AddComponentMenu("HVR.Basis/HVR Mesh Visibility")]
    public class HVRMeshVisibility : MonoBehaviour
    {
        public event MeshVisibilityChanged OnMeshVisibilityChanged;
        public delegate void MeshVisibilityChanged(HVRVixxyMeshVisibilityEffect effect, float value);
        
        [SerializeField] internal HVRVixxyMeshVisibilityEffect fallbackEffect = HVRVixxyMeshVisibilityEffect.DoNotOverride;
        [SerializeField] internal float fallbackOutput = 0f;
        
        [SerializeField] internal HVRMeshVisibilityPriority[] priorities = {
            new()
            {
                subjects = Array.Empty<Transform>(),
                condition = HVRVixxyMeshVisibilityCondition.IsAnyVisible,
                effect = HVRVixxyMeshVisibilityEffect.OverrideValue,
                output = 1f,
            }
        };

        private readonly List<HVRMeshVisibilityPriorityRuntime> _runtimePriorities = new();
        private float _previousValue = float.MinValue;
        private HVRVixxyMeshVisibilityEffect _previousEffect = HVRVixxyMeshVisibilityEffect.DoNotOverride;

        private void Awake()
        {
            foreach (var priority in priorities)
            {
                if (priority.subjects.Length > 0)
                {
                    var renderers = new List<Renderer>();
                    var nonRenderers = new List<GameObject>();
                    
                    foreach (var priorityTransform in priority.subjects)
                    {
                        if (null != priorityTransform)
                        {
                            var rendererNullable = priorityTransform.GetComponent<Renderer>();
                            if (rendererNullable != null) renderers.Add(rendererNullable);
                            else nonRenderers.Add(priorityTransform.gameObject);
                        }
                    }

                    if (renderers.Count > 0 || nonRenderers.Count > 0)
                    {
                        _runtimePriorities.Add(new HVRMeshVisibilityPriorityRuntime
                        {
                            renderers = renderers,
                            nonRenderers = nonRenderers,
                            condition = priority.condition,
                            effect = priority.effect,
                            output = priority.output,
                        });
                    }
                }
            }
        }

        public bool Evaluate()
        {
            var (newEffect, newValue) = Calculate();
            if (!Mathf.Approximately(newValue, _previousValue) || newEffect != _previousEffect)
            {
                _previousValue = newValue;
                _previousEffect = newEffect;
                OnMeshVisibilityChanged?.Invoke(newEffect, newEffect == HVRVixxyMeshVisibilityEffect.OverrideValue ? newValue : 0f);
                return true;
            }

            return false;
        }

        private (HVRVixxyMeshVisibilityEffect, float) Calculate()
        {
            foreach (var priority in _runtimePriorities)
            {
                var condition = priority.condition;
                if (condition == HVRVixxyMeshVisibilityCondition.AreAllVisible || condition == HVRVixxyMeshVisibilityCondition.AreAllHidden)
                {
                    var isPassing = true;
                    foreach (var nonRenderer in priority.nonRenderers)
                    {
                        var isVisible = null != nonRenderer && nonRenderer.activeInHierarchy;
                        if (condition == HVRVixxyMeshVisibilityCondition.AreAllVisible && !isVisible) { isPassing = false; break; }
                        if (condition == HVRVixxyMeshVisibilityCondition.AreAllHidden && isVisible) { isPassing = false; break; }
                    }
                    if (!isPassing) continue;
                    foreach (var renderer in priority.renderers)
                    {
                        var isVisible = null != renderer && renderer.gameObject.activeInHierarchy && renderer.enabled;
                        if (condition == HVRVixxyMeshVisibilityCondition.AreAllVisible && !isVisible) { isPassing = false; break; }
                        if (condition == HVRVixxyMeshVisibilityCondition.AreAllHidden && isVisible) { isPassing = false; break; }
                    }
                    if (isPassing) return (priority.effect, priority.output);
                }
                else
                {
                    foreach (var nonRenderer in priority.nonRenderers)
                    {
                        var isVisible = null != nonRenderer && nonRenderer.activeInHierarchy;
                        if (condition == HVRVixxyMeshVisibilityCondition.IsAnyVisible && isVisible) return (priority.effect, priority.output);
                        if (condition == HVRVixxyMeshVisibilityCondition.IsAnyHidden && !isVisible) return (priority.effect, priority.output);
                    }
                    foreach (var renderer in priority.renderers)
                    {
                        var isVisible = null != renderer && renderer.gameObject.activeInHierarchy && renderer.enabled;
                        if (condition == HVRVixxyMeshVisibilityCondition.IsAnyVisible && isVisible) return (priority.effect, priority.output);
                        if (condition == HVRVixxyMeshVisibilityCondition.IsAnyHidden && !isVisible) return (priority.effect, priority.output);
                    }
                }
            }
            
            return (fallbackEffect, fallbackEffect == HVRVixxyMeshVisibilityEffect.OverrideValue ? fallbackOutput : 0f);
        }

        private class HVRMeshVisibilityPriorityRuntime
        {
            public List<Renderer> renderers;
            public List<GameObject> nonRenderers;
            public HVRVixxyMeshVisibilityCondition condition;
            public float output;
            public HVRVixxyMeshVisibilityEffect effect;
        }
    }

    [Serializable]
    public class HVRMeshVisibilityPriority
    {
        public Transform[] subjects;
        public HVRVixxyMeshVisibilityCondition condition;
        public HVRVixxyMeshVisibilityEffect effect;
        public float output;
    }

    [Serializable]
    public enum HVRVixxyMeshVisibilityCondition
    {
        IsAnyVisible,
        AreAllVisible,
        IsAnyHidden,
        AreAllHidden,
    }

    [Serializable]
    public enum HVRVixxyMeshVisibilityEffect
    {
        OverrideValue,
        DoNotOverride,
    }
}