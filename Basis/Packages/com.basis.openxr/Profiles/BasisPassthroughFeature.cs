using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Basis.OpenXR
{
    /// <summary>
    /// Video passthrough via <c>XR_FB_passthrough</c>. Creates a reconstruction passthrough layer
    /// and, while active, submits it as an underlay in <c>xrEndFrame</c> so the projection layer's
    /// transparent pixels reveal the headset's real-world camera feed. Standalone VR (Quest) only.
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Basis Passthrough",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "Basis",
        Desc = "FB passthrough underlay for standalone mixed reality.",
        OpenxrExtensionStrings = ExtensionString,
        Version = "1.0.0",
        FeatureId = FeatureIdString)]
#endif
    public class BasisPassthroughFeature : OpenXRFeature
    {
        public const string FeatureIdString = "com.basis.openxr.feature.passthrough";
        public const string ExtensionString = "XR_FB_passthrough";
        const string Tag = "[BasisPassthrough]";

        /// <summary>True once the runtime reported the passthrough extension enabled this session.</summary>
        public static bool IsSupported { get; private set; }

        const uint XR_TYPE_PASSTHROUGH_CREATE_INFO_FB = 1000118001;
        const uint XR_TYPE_PASSTHROUGH_LAYER_CREATE_INFO_FB = 1000118002;
        const uint XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB = 1000118003;
        const uint XR_TYPE_COMPOSITION_LAYER_PROJECTION = 35;
        const uint XR_PASSTHROUGH_LAYER_PURPOSE_RECONSTRUCTION_FB = 0;
        const long XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT = 0x00000002;
        const long XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT = 0x00000004;
        const long PROJECTION_ALPHA_FLAGS = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT | XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT;
        const int LAYER_FLAGS_OFFSET = 16;
        const int XR_SESSION_STATE_FOCUSED = 5;

        static ulong s_Session;
        static ulong s_Passthrough;
        static ulong s_Layer;
        static bool s_LayerCreated;
        static bool s_Inject;
        static int s_FrameLog;
        static bool s_LoggedFirstFrame;

        static IntPtr s_UnderlayPtr;
        static IntPtr s_FrameEndInfoPtr;
        static IntPtr s_LayersPtr;
        static int s_LayersCapacity;

        [StructLayout(LayoutKind.Sequential)]
        struct XrPassthroughCreateInfoFB
        {
            public uint type;
            public IntPtr next;
            public ulong flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrPassthroughLayerCreateInfoFB
        {
            public uint type;
            public IntPtr next;
            public ulong passthrough;
            public ulong flags;
            public uint purpose;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrCompositionLayerPassthroughFB
        {
            public uint type;
            public IntPtr next;
            public ulong flags;
            public ulong space;
            public ulong layerHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrFrameEndInfo
        {
            public uint type;
            public IntPtr next;
            public long displayTime;
            public uint environmentBlendMode;
            public uint layerCount;
            public IntPtr layers;
        }

        delegate int Type_xrGetInstanceProcAddr(ulong instance, string name, out IntPtr function);
        delegate int Type_xrCreatePassthroughFB(ulong session, ref XrPassthroughCreateInfoFB createInfo, out ulong passthrough);
        delegate int Type_xrDestroyPassthroughFB(ulong passthrough);
        delegate int Type_xrPassthroughStartFB(ulong passthrough);
        delegate int Type_xrPassthroughPauseFB(ulong passthrough);
        delegate int Type_xrCreatePassthroughLayerFB(ulong session, ref XrPassthroughLayerCreateInfoFB createInfo, out ulong layer);
        delegate int Type_xrDestroyPassthroughLayerFB(ulong layer);
        delegate int Type_xrPassthroughLayerResumeFB(ulong layer);
        delegate int Type_xrPassthroughLayerPauseFB(ulong layer);
        delegate int Type_xrEndFrame(ulong session, IntPtr frameEndInfo);

        static Type_xrGetInstanceProcAddr d_getProc;
        static Type_xrGetInstanceProcAddr d_originalGetProc;
        static Type_xrCreatePassthroughFB d_createPassthrough;
        static Type_xrDestroyPassthroughFB d_destroyPassthrough;
        static Type_xrPassthroughStartFB d_startPassthrough;
        static Type_xrPassthroughPauseFB d_pausePassthrough;
        static Type_xrCreatePassthroughLayerFB d_createLayer;
        static Type_xrDestroyPassthroughLayerFB d_destroyLayer;
        static Type_xrPassthroughLayerResumeFB d_resumeLayer;
        static Type_xrPassthroughLayerPauseFB d_pauseLayer;
        static Type_xrEndFrame d_originalEndFrame;

        static readonly Type_xrGetInstanceProcAddr s_getProcHook = HookGetProc;
        static readonly Type_xrEndFrame s_endFrameHook = HookEndFrame;

        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            d_originalGetProc = Marshal.GetDelegateForFunctionPointer<Type_xrGetInstanceProcAddr>(func);
            return Marshal.GetFunctionPointerForDelegate(s_getProcHook);
        }

        [MonoPInvokeCallback(typeof(Type_xrGetInstanceProcAddr))]
        static int HookGetProc(ulong instance, string name, out IntPtr function)
        {
            if (name == "xrEndFrame")
            {
                int r = d_originalGetProc.Invoke(instance, "xrEndFrame", out IntPtr real);
                if (r == 0 && real != IntPtr.Zero)
                {
                    d_originalEndFrame = Marshal.GetDelegateForFunctionPointer<Type_xrEndFrame>(real);
                    function = Marshal.GetFunctionPointerForDelegate(s_endFrameHook);
                    BasisDebug.Log($"{Tag} Installed xrEndFrame interception hook.", BasisDebug.LogTag.Device);
                    return 0;
                }
                function = IntPtr.Zero;
                return r;
            }
            return d_originalGetProc.Invoke(instance, name, out function);
        }

        protected override bool OnInstanceCreate(ulong instance)
        {
            IsSupported = OpenXRRuntime.IsExtensionEnabled(ExtensionString);
            BasisDebug.Log($"{Tag} OnInstanceCreate: {ExtensionString} enabled = {IsSupported}", BasisDebug.LogTag.Device);
            if (!IsSupported)
            {
                BasisDebug.LogWarning($"{Tag} Extension NOT enabled — tick the 'Basis Passthrough' OpenXR feature for Android in Project Settings > XR > OpenXR.", BasisDebug.LogTag.Device);
                return base.OnInstanceCreate(instance);
            }

            d_getProc = Marshal.GetDelegateForFunctionPointer<Type_xrGetInstanceProcAddr>(xrGetInstanceProcAddr);
            d_createPassthrough = Load<Type_xrCreatePassthroughFB>(instance, "xrCreatePassthroughFB");
            d_destroyPassthrough = Load<Type_xrDestroyPassthroughFB>(instance, "xrDestroyPassthroughFB");
            d_startPassthrough = Load<Type_xrPassthroughStartFB>(instance, "xrPassthroughStartFB");
            d_pausePassthrough = Load<Type_xrPassthroughPauseFB>(instance, "xrPassthroughPauseFB");
            d_createLayer = Load<Type_xrCreatePassthroughLayerFB>(instance, "xrCreatePassthroughLayerFB");
            d_destroyLayer = Load<Type_xrDestroyPassthroughLayerFB>(instance, "xrDestroyPassthroughLayerFB");
            d_resumeLayer = Load<Type_xrPassthroughLayerResumeFB>(instance, "xrPassthroughLayerResumeFB");
            d_pauseLayer = Load<Type_xrPassthroughLayerPauseFB>(instance, "xrPassthroughLayerPauseFB");
            return base.OnInstanceCreate(instance);
        }

        static T Load<T>(ulong instance, string name) where T : Delegate
        {
            if (d_getProc.Invoke(instance, name, out IntPtr p) == 0 && p != IntPtr.Zero)
            {
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }
            BasisDebug.LogError($"{Tag} Failed to resolve {name}", BasisDebug.LogTag.Device);
            return null;
        }

        protected override void OnSessionCreate(ulong session)
        {
            s_Session = session;
        }

        protected override void OnSessionStateChange(int oldState, int newState)
        {
            if (newState != XR_SESSION_STATE_FOCUSED || !IsSupported)
            {
                return;
            }
            if (!s_LayerCreated)
            {
                CreatePassthrough();
            }
            else
            {
                BasisPassthroughController.NotifyRuntimeReady();
            }
        }

        static void CreatePassthrough()
        {
            if (d_createPassthrough == null || d_createLayer == null)
            {
                BasisDebug.LogError($"{Tag} Cannot create passthrough — function pointers missing.", BasisDebug.LogTag.Device);
                return;
            }

            XrPassthroughCreateInfoFB createInfo = new XrPassthroughCreateInfoFB
            {
                type = XR_TYPE_PASSTHROUGH_CREATE_INFO_FB,
                next = IntPtr.Zero,
                flags = 0,
            };
            int r = d_createPassthrough.Invoke(s_Session, ref createInfo, out s_Passthrough);
            if (r != 0)
            {
                BasisDebug.LogError($"{Tag} xrCreatePassthroughFB failed: {r}", BasisDebug.LogTag.Device);
                return;
            }

            XrPassthroughLayerCreateInfoFB layerInfo = new XrPassthroughLayerCreateInfoFB
            {
                type = XR_TYPE_PASSTHROUGH_LAYER_CREATE_INFO_FB,
                next = IntPtr.Zero,
                passthrough = s_Passthrough,
                flags = 0,
                purpose = XR_PASSTHROUGH_LAYER_PURPOSE_RECONSTRUCTION_FB,
            };
            r = d_createLayer.Invoke(s_Session, ref layerInfo, out s_Layer);
            if (r != 0)
            {
                BasisDebug.LogError($"{Tag} xrCreatePassthroughLayerFB failed: {r}", BasisDebug.LogTag.Device);
                d_destroyPassthrough?.Invoke(s_Passthrough);
                s_Passthrough = 0;
                return;
            }

            XrCompositionLayerPassthroughFB underlay = new XrCompositionLayerPassthroughFB
            {
                type = XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB,
                next = IntPtr.Zero,
                flags = 0,
                space = 0,
                layerHandle = s_Layer,
            };
            s_UnderlayPtr = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerPassthroughFB>());
            Marshal.StructureToPtr(underlay, s_UnderlayPtr, false);
            s_FrameEndInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<XrFrameEndInfo>());
            EnsureLayersCapacity(8);

            s_LayerCreated = true;
            BasisDebug.Log($"{Tag} Passthrough created (handle={s_Passthrough}, layer={s_Layer}).", BasisDebug.LogTag.Device);
            BasisPassthroughController.NotifyRuntimeReady();
        }

        static void EnsureLayersCapacity(int count)
        {
            if (s_LayersPtr != IntPtr.Zero && count <= s_LayersCapacity)
            {
                return;
            }
            if (s_LayersPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(s_LayersPtr);
            }
            s_LayersCapacity = Mathf.Max(count, 8);
            s_LayersPtr = Marshal.AllocHGlobal(IntPtr.Size * s_LayersCapacity);
        }

        /// <summary>Starts/resumes or pauses passthrough and toggles the per-frame underlay submission.</summary>
        public static void SetActive(bool on)
        {
            if (!IsSupported || !s_LayerCreated)
            {
                s_Inject = false;
                BasisDebug.Log($"{Tag} SetActive({on}) ignored — IsSupported={IsSupported}, layerCreated={s_LayerCreated}.", BasisDebug.LogTag.Device);
                return;
            }
            if (on)
            {
                int startResult = d_startPassthrough?.Invoke(s_Passthrough) ?? -1;
                int resumeResult = d_resumeLayer?.Invoke(s_Layer) ?? -1;
                s_Inject = startResult == 0;
                BasisDebug.Log($"{Tag} SetActive(true): xrPassthroughStartFB={startResult}, xrPassthroughLayerResumeFB={resumeResult} (0 == success, inject={s_Inject}).", BasisDebug.LogTag.Device);
            }
            else
            {
                s_Inject = false;
                d_pauseLayer?.Invoke(s_Layer);
                d_pausePassthrough?.Invoke(s_Passthrough);
                BasisDebug.Log($"{Tag} SetActive(false): passthrough paused.", BasisDebug.LogTag.Device);
            }
        }

        [MonoPInvokeCallback(typeof(Type_xrEndFrame))]
        static int HookEndFrame(ulong session, IntPtr frameEndInfo)
        {
            if (!s_LoggedFirstFrame)
            {
                s_LoggedFirstFrame = true;
                BasisDebug.Log($"{Tag} xrEndFrame hook is being called by the runtime.", BasisDebug.LogTag.Device);
            }
            if (!s_Inject || !s_LayerCreated || frameEndInfo == IntPtr.Zero || d_originalEndFrame == null)
            {
                return d_originalEndFrame != null ? d_originalEndFrame.Invoke(session, frameEndInfo) : 0;
            }

            XrFrameEndInfo info = Marshal.PtrToStructure<XrFrameEndInfo>(frameEndInfo);
            uint count = info.layerCount;
            EnsureLayersCapacity((int)count + 1);

            Marshal.WriteIntPtr(s_LayersPtr, 0, s_UnderlayPtr);
            for (int i = 0; i < count; i++)
            {
                IntPtr layerPtr = info.layers != IntPtr.Zero ? Marshal.ReadIntPtr(info.layers, i * IntPtr.Size) : IntPtr.Zero;
                if (layerPtr != IntPtr.Zero && (uint)Marshal.ReadInt32(layerPtr, 0) == XR_TYPE_COMPOSITION_LAYER_PROJECTION)
                {
                    long flags = Marshal.ReadInt64(layerPtr, LAYER_FLAGS_OFFSET);
                    Marshal.WriteInt64(layerPtr, LAYER_FLAGS_OFFSET, flags | PROJECTION_ALPHA_FLAGS);
                }
                Marshal.WriteIntPtr(s_LayersPtr, (i + 1) * IntPtr.Size, layerPtr);
            }

            if ((s_FrameLog++ % 300) == 0)
            {
                BasisDebug.Log($"{Tag} EndFrame inject: {count} app layers -> submitting {count + 1} (passthrough underlay at index 0).", BasisDebug.LogTag.Device);
            }

            info.layerCount = count + 1;
            info.layers = s_LayersPtr;
            Marshal.StructureToPtr(info, s_FrameEndInfoPtr, false);
            return d_originalEndFrame.Invoke(session, s_FrameEndInfoPtr);
        }

        protected override void OnSessionDestroy(ulong session)
        {
            SetActive(false);
            if (s_LayerCreated)
            {
                d_destroyLayer?.Invoke(s_Layer);
                d_destroyPassthrough?.Invoke(s_Passthrough);
            }
            s_LayerCreated = false;
            s_Layer = 0;
            s_Passthrough = 0;
            s_Session = 0;
            FreeBuffers();
            BasisPassthroughController.NotifyRuntimeLost();
        }

        protected override void OnInstanceDestroy(ulong instance)
        {
            IsSupported = false;
        }

        static void FreeBuffers()
        {
            if (s_UnderlayPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(s_UnderlayPtr);
                s_UnderlayPtr = IntPtr.Zero;
            }
            if (s_FrameEndInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(s_FrameEndInfoPtr);
                s_FrameEndInfoPtr = IntPtr.Zero;
            }
            if (s_LayersPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(s_LayersPtr);
                s_LayersPtr = IntPtr.Zero;
                s_LayersCapacity = 0;
            }
        }
    }
}
