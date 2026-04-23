using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HVR.Vixxy
{
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization/vixxy")]
    public partial class HVRVixxyControl
    {
        /// The orchestrator defines the context that the subjects of this control will affect (e.g. Recursive Search).
        /// Vixxy is not an avatar-specific component, so it needs that limited context.
        [SerializeField] internal HVRVixxyOrchestrator orchestrator;

        /// An address is not necessary, but if one is provided, then we will be using that provided address.
        /// If not, we will generate one at runtime.
        [SerializeField] internal string address = "";

        [SerializeField] public bool hasThreeOrMoreChoices;
        [SerializeField] public int numberOfChoices = 3;

        [SerializeField] internal HVRVixxyActivation[] activations = Array.Empty<HVRVixxyActivation>();
        [SerializeField] internal HVRVixxySubject[] subjects = Array.Empty<HVRVixxySubject>();

        /// The value that is considered to be OFF. This may be larger than upperBound. (Irrelevant when there are more than two choices)
        [SerializeField] internal float lowerBound = 0f;
        /// The value that is considered to be ON. (Irrelevant when there are more than two choices)
        [SerializeField] internal float upperBound = 1f;

        // The number of seconds it takes to go from 0.0 to 1.0. If there are more than two choices: The number of seconds it takes to go from one state to another.
        [SerializeField] internal float interpolationDurationSeconds = 0f;
        [SerializeField] internal AnimationCurve interpolationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [SerializeField] internal bool networked = true;
        [SerializeField] internal HVRVixxyNetworkingType advancedNetworking = HVRVixxyNetworkingType.Automatic;

        /// If true, we only run the logic of this control if it's enabled. By default, this is false, so that users can put a toggle control
        /// directly inside the component hierarchy that is being toggled OFF.
        [SerializeField] internal bool onlyExecuteWhenEnabled = false;
    }

    [Serializable]
    public enum HVRVixxyControlType
    {
        Menu,
        Variable,
    }

    [Serializable]
    public enum HVRVixxyControlPresentation
    {
        Default,
        Slider,
    }

    [Serializable]
    public enum HVRVixxyTitleSelection
    {
        UseObjectName,
        UseCustomTitle,
        UseCustomTitleAndChoices,
        UseChoicesOnly,
    }

    [Serializable]
    public class HVRVixxyActivation
    {
        public Component component; // To toggle a GameObject, provide the Transform instead. It makes things easier as GameObject is not a component.
        public ActivationThreshold threshold;
        public bool[] choices;

        [NonSerialized] internal bool IsApplicable;
        [NonSerialized] internal HVRVixxyActivationBakeResult BakeResult;
    }

    [Serializable]
    public enum ActivationThreshold
    {
        /// When there's a transition, it is ON during that transition.<br/>
        /// In technical terms, it is considered to be ON when the absolute difference to the target is strictly smaller than 1.
        /// This is the best choice for stuff like material dissolves, where the object appears before it is even complete, and therefore the default.
        Blended,
        /// Is considered to be ON when the current value is equal to the target value.
        Strict,
    }

    [Serializable]
    public class HVRVixxySubject
    {
        public HVRVixxySelection selection;

        // TODO: It may be relevant to create a MonoBehaviour that represents groups of objects that can be referenced multiple times throughout.
        public GameObject[] targets;
        public GameObject[] childrenOf;
        public GameObject[] exceptions;

        // Note: The list of properties may sometimes contain properties that are not shown in the UI,
        // because the first target does not contain the component type referenced by that property.
        //
        // In that case, when the Processor runs, these properties are NOT applied, even if the actual
        // objects being changed do contain the component type.
        // We don't want to apply "ghost" properties that are not visible to the user in the UI.
        //
        // In the case of Vixxy (and not Vixen), we should just prune these properties at runtime.
        [SerializeReference] public List<HVRVixxyPropertyBase> properties;

        // Runtime only
        [NonSerialized] internal List<GameObject> BakedObjects;
        [NonSerialized] internal bool IsApplicable;
        [NonSerialized] internal HVRVixxySubjectsBakeResult BakeResult;
    }

    [Serializable]
    public enum HVRVixxySelection
    {
        Normal,
        RecursiveSearch,
        Everything
    }

    [Serializable]
    public enum HVRVixxyRememberScope
    {
        /// When the avatar loads, the value is always the default.
        DoNotRemember,
        /// We remember the value for this address, only in this specific avatar. The name of the avatar is used to determine if it's the same avatar.
        RememberInThisAvatar,
        /// We remember the value for this address, only across controls which share the same rememberTag value.
        RememberInThisTag,
        /// We remember the value for this address across all avatars.
        RememberAcrossAvatars
    }

    [Serializable]
    public enum HVRVixxyNetworkingType
    {
        Automatic,
        ContinuousAutomatedDataStream
    }

    [Serializable]
    public class HVRVixxyProperty<T> : HVRVixxyPropertyBase
    {
        public T[] choices = new T[2];

        public T InactiveValue => choices[InactiveIndex];
        public T ActiveValue => choices[ActiveIndex];

        public override bool ValidateBasedOnNumberOfChoices(int actualNumberOfChoices) => choices.Length >= actualNumberOfChoices;

        public override void PruneArrays(int actualNumberOfChoices)
        {
            var newChoices = new T[actualNumberOfChoices];
            for (var i = 0; i < actualNumberOfChoices; i++)
            {
                if (i < choices.Length)
                {
                    newChoices[i] = choices[i];
                }
                else
                {
                    var k = choices.Length - 1;
                    if (k <= 0)
                    {
                        newChoices[i] = choices[k];
                    }
                }
            }

            choices = newChoices;
        }
    }

    [Serializable]
    public class HVRVixxyPropertyBase : IHVRVixxyProperty
    {
        public const int InactiveIndex = 0;
        public const int ActiveIndex = 1;

        // TODO: It might be relevant to use another approach than getting animatable properties,
        // since we have control over the system. It doesn't have to piggyback on the animation APIs.
        public string fullClassName;
        public HVRVixxyPropertyVariant variant;
        public string propertyName;

        // Runtime only
        [NonSerialized] internal bool IsApplicable;
        [NonSerialized] internal HVRVixxyPropertyBakeResult BakeResult;
        [NonSerialized] internal Type FoundType;
        [NonSerialized] internal List<Component> FoundComponents;
        [NonSerialized] internal HVRKindMarker KindMarker;
        [NonSerialized] internal int ShaderMaterialProperty;
        [NonSerialized] internal FieldInfo FieldIfMarkedAsFieldAccess; // null if SpecialMarker is not FieldAccess
        [NonSerialized] internal PropertyInfo TPropertyIfMarkedAsTPropertyAccess; // null if SpecialMarker is not PropertyAccess
        [NonSerialized] internal Dictionary<SkinnedMeshRenderer, int> SmrToBlendshapeIndex; // null if SpecialMarker is not BlendShape

        public virtual bool ValidateBasedOnNumberOfChoices(int actualNumberOfChoices) => true;
        public virtual void PruneArrays(int actualNumberOfChoices) {}
    }

    [Serializable]
    public enum HVRVixxyPropertyVariant
    {
        Standard,
        MaterialProperty,
        BlendShape
    }

    interface IHVRVixxyProperty
    {
    }

    [Serializable] public class HVRVixxyPropertyFloat : HVRVixxyProperty<float> { }
    [Serializable] public class HVRVixxyPropertyVector4 : HVRVixxyProperty<Vector4> { }
    [Serializable] public class HVRVixxyPropertyVector3 : HVRVixxyProperty<Vector3> { }
    [Serializable] public class HVRVixxyPropertyMaterial : HVRVixxyProperty<Material> { }
    [Serializable] public class HVRVixxyPropertyMesh : HVRVixxyProperty<Mesh> { }
    [Serializable] public class HVRVixxyPropertyColor : HVRVixxyProperty<Color> { public HVRVixxyPropertyColorInterpolation interpolation; }
    [Serializable] public class HVRVixxyPropertyBool : HVRVixxyProperty<bool> { public float threshold; }
    [Serializable] public class HVRVixxyPropertyQuaternion : HVRVixxyProperty<Quaternion> { public HVRVixxyPropertyQuaternionInterpolation interpolation; }

    [Serializable]
    public enum HVRVixxyPropertyColorInterpolation
    {
        Oklab,
        Unity
    }

    [Serializable]
    public enum HVRVixxyPropertyQuaternionInterpolation
    {
        Spherical,
    }

    [Serializable]
    public class HVRVixxyChoice
    {
        public string title;
        public Texture2D icon;
    }
}
