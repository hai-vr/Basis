using UnityEngine;

namespace Basis.ImagePickup
{
	internal static class BasisImagePickupRuntimeUtility
	{
		public static void DestroyObject(Object value)
		{
			if (value == null)
				return;
			if (Application.isPlaying)
				Object.Destroy(value);
			else
				Object.DestroyImmediate(value);
		}

		public static Color ToWorkingStraightColor(Color32 color)
		{
			Color value = color;
			if (QualitySettings.activeColorSpace == ColorSpace.Linear)
			{
				float alpha = value.a;
				value = value.linear;
				value.a = alpha;
			}
			return value;
		}

		public static Color ToWorkingPremultipliedColor(Color32 color)
		{
			Color value = ToWorkingStraightColor(color);
			value.r *= value.a;
			value.g *= value.a;
			value.b *= value.a;
			return value;
		}
	}
}
