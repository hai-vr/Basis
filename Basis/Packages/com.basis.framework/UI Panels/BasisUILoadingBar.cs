
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Basis.Scripts.UI.UI_Panels
{
    [Serializable]
    public class LoadingOperationData
    {
        public string Key;
        public float Percentage;
        public string Display;

        public LoadingOperationData(string key, float percentage, string display)
        {
            Key = key;
            Percentage = percentage;
            Display = display;
        }
    }

    public class BasisUILoadingBar : BasisUIBase
    {
        public TextMeshPro TextMeshPro;
        public SpriteRenderer Renderer;
        public static BasisUILoadingBar Instance;
        public const string LoadingBar = "Packages/com.basis.sdk/Prefabs/UI/Loading Bar.prefab";

        public Vector3 Position = new Vector3(12, -1.6f, 0);
        public Quaternion Rotation;
        public Vector3 Scale = new Vector3(4, 4, 4);

        public static event Action<string, float, bool> OnDisplayChanged;

        public static string CurrentDisplay { get; private set; } = string.Empty;
        public static float CurrentPercentage { get; private set; }
        public static bool HasDisplay { get; private set; }

        private static readonly List<LoadingOperationData> loadingOperations = new List<LoadingOperationData>();
        private static bool hudSuppressed;

        private static bool IsRoutedElsewhere => hudSuppressed && OnDisplayChanged != null;

        private static Coroutine autoDestroyCoroutine;
        private static MonoBehaviour autoDestroyHost;
        private const float AutoDestroyTimeout = 1.5f;

        public static void Initialize()
        {
            BasisSceneLoad.progressCallback.OnProgressReport += ProgressReport;
            BasisLocalPlayer.Instance.ProgressReportAvatarLoad.OnProgressReport += ProgressReport;
        }

        public static void DeInitialize()
        {
            BasisSceneLoad.progressCallback.OnProgressReport -= ProgressReport;
            BasisLocalPlayer.Instance.ProgressReportAvatarLoad.OnProgressReport -= ProgressReport;
        }

        // Cached delegate + queue avoids per-call closure allocation (~80 bytes GC per call)
        static readonly ConcurrentQueue<(string UniqueID, float Progress, string Info)> _pendingReports = new();
        static readonly Action _processPendingReports = ProcessPendingReports;

        public static void ProgressReport(string UniqueID, float progress, string info)
        {
            _pendingReports.Enqueue((UniqueID, progress, info));
            BasisDeviceManagement.EnqueueOnMainThread(_processPendingReports);
        }

        static void ProcessPendingReports()
        {
            while (_pendingReports.TryDequeue(out var report))
            {
                if (report.Progress == 100)
                {
                    RemoveDisplay(report.UniqueID);
                }
                else
                {
                    AddOrUpdateDisplay(report.UniqueID, report.Progress, report.Info);
                }
            }
        }

        public static void SetHudSuppressed(bool suppressed)
        {
            if (hudSuppressed == suppressed)
            {
                return;
            }
            hudSuppressed = suppressed;
            BasisDebug.Log($"[LoadingBarRoute] SetHudSuppressed {suppressed} hasDisplay:{HasDisplay} listeners:{OnDisplayChanged?.GetInvocationList().Length ?? 0}");

            if (suppressed)
            {
                DestroyHud();
            }
            else if (HasDisplay)
            {
                ProcessQueue();
            }
        }

        public static void CloseLoadingBar()
        {
            BasisDeviceManagement.EnqueueOnMainThread(() =>
            {
                StopAutoDestroyCoroutine();
                loadingOperations.Clear();
                DestroyHud();
                SetDisplayState(string.Empty, 0f, false);
            });
        }

        public static void AddOrUpdateDisplay(string key, float percentage, string display)
        {
            LoadingOperationData operation = loadingOperations.Find(op => op.Key == key);
            if (operation != null)
            {
                operation.Percentage = percentage;
                operation.Display = display;
            }
            else
            {
                loadingOperations.Add(new LoadingOperationData(key, percentage, display));
            }
            ProcessQueue();

            // Reset the auto-destroy coroutine
            ResetAutoDestroyCoroutine();
        }

        public static void RemoveDisplay(string key)
        {
            BasisDeviceManagement.EnqueueOnMainThread(() =>
            {
                LoadingOperationData operation = loadingOperations.Find(op => op.Key == key);
                if (operation != null)
                {
                    loadingOperations.Remove(operation);
                }

                if (loadingOperations.Count > 0)
                {
                    ProcessQueue();
                }
                else
                {
                    CloseLoadingBar();
                }
            });
        }

        private static void ProcessQueue()
        {
            LoadingOperationData operation = GetFirstLoadingOperation();
            if (operation == null)
            {
                return;
            }

            if (!IsRoutedElsewhere && Instance == null)
            {
                BasisUIBase.OpenMenuNow(LoadingBar);
            }

            SetDisplayState(operation.Display, operation.Percentage, true);
        }

        private static LoadingOperationData GetFirstLoadingOperation()
        {
            return loadingOperations.FirstOrDefault(op => op.Percentage > 0);
        }

        private static void SetDisplayState(string display, float percentage, bool active)
        {
            CurrentDisplay = display ?? string.Empty;
            CurrentPercentage = percentage;
            HasDisplay = active;

            BasisDebug.Log($"[LoadingBarRoute] SetDisplayState '{CurrentDisplay}' {percentage} active:{active} suppressed:{hudSuppressed} listeners:{OnDisplayChanged?.GetInvocationList().Length ?? 0} hud:{Instance != null}");

            if (active && Instance != null)
            {
                Instance.UpdateDisplay(percentage, CurrentDisplay);
            }

            OnDisplayChanged?.Invoke(CurrentDisplay, percentage, active);
        }

        private static void DestroyHud()
        {
            if (Instance != null)
            {
                Instance.CloseThisMenu();
                Instance = null;
            }
        }

        private void UpdateDisplay(float percentage, string display)
        {
            if (TextMeshPro == null || Renderer == null)
            {
                return;
            }
            TextMeshPro.text = FormatDisplay(percentage, display);
            float value = percentage / 4f;
            Renderer.size = new Vector2(value, 2);
        }

        public static string FormatDisplay(float percentage, string display)
        {
            return $"{display}  {Mathf.RoundToInt(percentage)}%";
        }

        public override void InitializeEvent()
        {
            Instance = this;
            if (BasisLocalCameraDriver.HasInstance)
            {
                InstanceExists();
            }
            BasisLocalCameraDriver.InstanceExists += InstanceExists;

            if (HasDisplay)
            {
                UpdateDisplay(CurrentPercentage, CurrentDisplay);
            }
        }

        private void InstanceExists()
        {
            this.transform.parent = BasisLocalCameraDriver.Instance.ParentOfUI;
            this.transform.SetLocalPositionAndRotation(Position, Rotation);
            this.transform.localScale = Scale;
        }

        public override void DestroyEvent()
        {
        }

        public void OnDestroy()
        {
            BasisLocalCameraDriver.InstanceExists -= InstanceExists;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private static void ResetAutoDestroyCoroutine()
        {
            StopAutoDestroyCoroutine();

            MonoBehaviour host = BasisDeviceManagement.Instance != null ? BasisDeviceManagement.Instance : (MonoBehaviour)Instance;
            if (host == null || !host.isActiveAndEnabled)
            {
                return;
            }

            autoDestroyHost = host;
            autoDestroyCoroutine = host.StartCoroutine(AutoDestroyAfterTimeout());
        }

        private static void StopAutoDestroyCoroutine()
        {
            if (autoDestroyCoroutine != null && autoDestroyHost != null)
            {
                autoDestroyHost.StopCoroutine(autoDestroyCoroutine);
            }
            autoDestroyCoroutine = null;
            autoDestroyHost = null;
        }

        private static System.Collections.IEnumerator AutoDestroyAfterTimeout()
        {
            yield return new WaitForSeconds(AutoDestroyTimeout);
            autoDestroyCoroutine = null;
            autoDestroyHost = null;
            CloseLoadingBar();
        }
    }
}
