#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Basis.ImagePickup
{
    /// <summary>
    /// Windows-only OS file-drop bridge. Subclasses the player window to intercept WM_DROPFILES and forwards
    /// dropped .png paths to the image pickup manager on the Unity main thread.
    /// </summary>
    public class BasisDesktopImageDropHook : MonoBehaviour
    {
        private const int GWLP_WNDPROC = -4;
        private const uint WM_DROPFILES = 0x0233;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumThreadWindowProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);
        [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("shell32.dll")] private static extern void DragAcceptFiles(IntPtr hWnd, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)] private static extern uint DragQueryFile(IntPtr hDrop, uint file, StringBuilder buffer, uint length);
        [DllImport("shell32.dll")] private static extern void DragFinish(IntPtr hDrop);

        private IntPtr _windowHandle = IntPtr.Zero;
        private IntPtr _previousWndProc = IntPtr.Zero;
        private WndProcDelegate _wndProcDelegate;

        private readonly List<string> _pending = new();
        private readonly object _pendingLock = new();

        private void OnEnable()
        {
            _windowHandle = FindPlayerWindow();
            if (_windowHandle == IntPtr.Zero)
            {
                BasisDebug.LogWarning("Image pickup: could not find the player window; file drop disabled.");
                enabled = false;
                return;
            }

            DragAcceptFiles(_windowHandle, true);
            _wndProcDelegate = HookedWndProc;
            _previousWndProc = SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }

        private void OnDisable()
        {
            if (_windowHandle != IntPtr.Zero && _previousWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, _previousWndProc);
                DragAcceptFiles(_windowHandle, false);
            }
            _windowHandle = IntPtr.Zero;
            _previousWndProc = IntPtr.Zero;
            _wndProcDelegate = null;
        }

        private void Update()
        {
            if (BasisImagePickupManager.Instance == null) return;

            lock (_pendingLock)
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    BasisImagePickupManager.Instance.SpawnFromFile(_pending[i]);
                }
                _pending.Clear();
            }
        }

        private IntPtr HookedWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DROPFILES)
            {
                CollectDroppedFiles(wParam);
            }
            return CallWindowProc(_previousWndProc, hWnd, msg, wParam, lParam);
        }

        private void CollectDroppedFiles(IntPtr hDrop)
        {
            try
            {
                uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    var builder = new StringBuilder(1024);
                    DragQueryFile(hDrop, i, builder, (uint)builder.Capacity);
                    string path = builder.ToString();
                    if (BasisImageSecurity.HasPngExtension(path))
                    {
                        lock (_pendingLock) _pending.Add(path);
                    }
                }
            }
            finally
            {
                DragFinish(hDrop);
            }
        }

        private static IntPtr FindPlayerWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumThreadWindows(GetCurrentThreadId(), (hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
#endif
