using Basis.Scripts.BasisSdk;
using HVR.Basis.Comms;
using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// Routes MediaPipe ARKit-52 face blendshapes and derived eye gaze into Basis through
    /// HVR.Basis.Comms, the same way OSC face tracking does: submit FT/v2/* values to the
    /// scene AcquisitionService and notify the avatar's activity relay. The avatar's
    /// AutomaticFaceTracking applies them to the mesh/eye bones and networks them to remotes.
    /// </summary>
    public sealed class MediaPipeFaceConverter
    {
        public float EyeGainX = 1f;
        public float EyeGainY = 1f;
        public bool EyeLidIsOpenness = true;
        public float Smoothing = 0.5f;
        public float TongueGain = 1f;

        private float[] _smoothed;
        private float _tongueSmoothed;

        private static readonly (string addr, MediaPipeArkitBlendshape src)[] Direct =
        {
            ("FT/v2/BrowDownLeft", MediaPipeArkitBlendshape.BrowDownLeft),
            ("FT/v2/BrowDownRight", MediaPipeArkitBlendshape.BrowDownRight),
            ("FT/v2/BrowInnerUp", MediaPipeArkitBlendshape.BrowInnerUp),
            ("FT/v2/BrowOuterUpLeft", MediaPipeArkitBlendshape.BrowOuterUpLeft),
            ("FT/v2/BrowOuterUpRight", MediaPipeArkitBlendshape.BrowOuterUpRight),
            ("FT/v2/CheekSquintLeft", MediaPipeArkitBlendshape.CheekSquintLeft),
            ("FT/v2/CheekSquintRight", MediaPipeArkitBlendshape.CheekSquintRight),
            ("FT/v2/EyeSquintLeft", MediaPipeArkitBlendshape.EyeSquintLeft),
            ("FT/v2/EyeSquintRight", MediaPipeArkitBlendshape.EyeSquintRight),
            ("FT/v2/JawOpen", MediaPipeArkitBlendshape.JawOpen),
            ("FT/v2/MouthClosed", MediaPipeArkitBlendshape.MouthClose),
            ("FT/v2/LipSuckUpper", MediaPipeArkitBlendshape.MouthRollUpper),
            ("FT/v2/LipSuckLower", MediaPipeArkitBlendshape.MouthRollLower),
            ("FT/v2/LipFunnel", MediaPipeArkitBlendshape.MouthFunnel),
            ("FT/v2/LipPucker", MediaPipeArkitBlendshape.MouthPucker),
            ("FT/v2/MouthUpperUpLeft", MediaPipeArkitBlendshape.MouthUpperUpLeft),
            ("FT/v2/MouthUpperUpRight", MediaPipeArkitBlendshape.MouthUpperUpRight),
            ("FT/v2/MouthLowerDownLeft", MediaPipeArkitBlendshape.MouthLowerDownLeft),
            ("FT/v2/MouthLowerDownRight", MediaPipeArkitBlendshape.MouthLowerDownRight),
            ("FT/v2/MouthSmileLeft", MediaPipeArkitBlendshape.MouthSmileLeft),
            ("FT/v2/MouthSmileRight", MediaPipeArkitBlendshape.MouthSmileRight),
            ("FT/v2/MouthFrownLeft", MediaPipeArkitBlendshape.MouthFrownLeft),
            ("FT/v2/MouthFrownRight", MediaPipeArkitBlendshape.MouthFrownRight),
            ("FT/v2/MouthStretchLeft", MediaPipeArkitBlendshape.MouthStretchLeft),
            ("FT/v2/MouthStretchRight", MediaPipeArkitBlendshape.MouthStretchRight),
            ("FT/v2/MouthDimpleLeft", MediaPipeArkitBlendshape.MouthDimpleLeft),
            ("FT/v2/MouthDimpleRight", MediaPipeArkitBlendshape.MouthDimpleRight),
            ("FT/v2/MouthPressLeft", MediaPipeArkitBlendshape.MouthPressLeft),
            ("FT/v2/MouthPressRight", MediaPipeArkitBlendshape.MouthPressRight),
            ("FT/v2/MouthRaiserUpper", MediaPipeArkitBlendshape.MouthShrugUpper),
            ("FT/v2/MouthRaiserLower", MediaPipeArkitBlendshape.MouthShrugLower),
            ("FT/v2/NoseSneerLeft", MediaPipeArkitBlendshape.NoseSneerLeft),
            ("FT/v2/NoseSneerRight", MediaPipeArkitBlendshape.NoseSneerRight),
        };

        private readonly int[] _directIds;
        private readonly int _idEyeLeftX, _idEyeRightX, _idEyeY;
        private readonly int _idEyeLidLeft, _idEyeLidRight;
        private readonly int _idJawX, _idJawZ, _idCheekPuffSuck;
        private readonly int _idTongueOut;

        private BasisAvatar _relayAvatar;
        private FaceTrackingActivityRelay _relay;

        public MediaPipeFaceConverter()
        {
            _directIds = new int[Direct.Length];
            for (int i = 0; i < Direct.Length; i++)
            {
                _directIds[i] = HVRAddress.AddressToId(Direct[i].addr);
            }

            _idEyeLeftX = HVRAddress.AddressToId("FT/v2/EyeLeftX");
            _idEyeRightX = HVRAddress.AddressToId("FT/v2/EyeRightX");
            _idEyeY = HVRAddress.AddressToId("FT/v2/EyeY");
            _idEyeLidLeft = HVRAddress.AddressToId("FT/v2/EyeLidLeft");
            _idEyeLidRight = HVRAddress.AddressToId("FT/v2/EyeLidRight");
            _idJawX = HVRAddress.AddressToId("FT/v2/JawX");
            _idJawZ = HVRAddress.AddressToId("FT/v2/JawZ");
            _idCheekPuffSuck = HVRAddress.AddressToId("FT/v2/CheekPuffSuck");
            _idTongueOut = HVRAddress.AddressToId("FT/v2/TongueOut");
        }

        public void Apply(in BasisMediaPipeResult result, BasisAvatar avatar)
        {
            float[] bs = result.FaceBlendshapes;
            if (avatar == null || bs == null || bs.Length < (int)MediaPipeArkitBlendshape.Count) return;

            AcquisitionService acquisition = AcquisitionService.SceneInstance;
            if (acquisition == null) return;

            if (_smoothed == null || _smoothed.Length != bs.Length)
            {
                _smoothed = (float[])bs.Clone();
            }
            float st = 1f - Mathf.Clamp01(Smoothing);
            for (int i = 0; i < bs.Length; i++)
            {
                _smoothed[i] = Mathf.Lerp(_smoothed[i], bs[i], st);
            }
            bs = _smoothed;

            ResolveRelay(avatar)?.NotifySourceSample(_idEyeLidLeft);

            for (int i = 0; i < Direct.Length; i++)
            {
                acquisition.Submit(_directIds[i], Mathf.Clamp01(bs[(int)Direct[i].src]));
            }

            acquisition.Submit(_idJawX, Get(bs, MediaPipeArkitBlendshape.JawRight) - Get(bs, MediaPipeArkitBlendshape.JawLeft));
            acquisition.Submit(_idJawZ, Mathf.Clamp01(Get(bs, MediaPipeArkitBlendshape.JawForward)));
            acquisition.Submit(_idCheekPuffSuck, Mathf.Clamp01(Get(bs, MediaPipeArkitBlendshape.CheekPuff)));

            float eyeLeftX = Get(bs, MediaPipeArkitBlendshape.EyeLookOutLeft) - Get(bs, MediaPipeArkitBlendshape.EyeLookInLeft);
            float eyeRightX = Get(bs, MediaPipeArkitBlendshape.EyeLookInRight) - Get(bs, MediaPipeArkitBlendshape.EyeLookOutRight);
            float eyeY = 0.5f * (Get(bs, MediaPipeArkitBlendshape.EyeLookUpLeft) + Get(bs, MediaPipeArkitBlendshape.EyeLookUpRight))
                       - 0.5f * (Get(bs, MediaPipeArkitBlendshape.EyeLookDownLeft) + Get(bs, MediaPipeArkitBlendshape.EyeLookDownRight));

            acquisition.Submit(_idEyeLeftX, Mathf.Clamp(eyeLeftX * EyeGainX, -1f, 1f));
            acquisition.Submit(_idEyeRightX, Mathf.Clamp(eyeRightX * EyeGainX, -1f, 1f));
            acquisition.Submit(_idEyeY, Mathf.Clamp(eyeY * EyeGainY, -1f, 1f));

            acquisition.Submit(_idEyeLidLeft, EyeLid(Get(bs, MediaPipeArkitBlendshape.EyeBlinkLeft)));
            acquisition.Submit(_idEyeLidRight, EyeLid(Get(bs, MediaPipeArkitBlendshape.EyeBlinkRight)));

            float tongue = Mathf.Clamp01(result.TongueOut * TongueGain);
            _tongueSmoothed = Mathf.Lerp(_tongueSmoothed, tongue, st);
            acquisition.Submit(_idTongueOut, _tongueSmoothed);
        }

        private float EyeLid(float blink) => EyeLidIsOpenness ? Mathf.Clamp01(1f - blink) : Mathf.Clamp01(blink);

        private static float Get(float[] bs, MediaPipeArkitBlendshape s) => bs[(int)s];

        private FaceTrackingActivityRelay ResolveRelay(BasisAvatar avatar)
        {
            if (_relay != null && _relayAvatar == avatar) return _relay;
            _relayAvatar = avatar;
            _relay = FaceTrackingActivityRelay.GetOrCreate(avatar);
            return _relay;
        }
    }
}
