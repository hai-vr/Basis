namespace UnityEngine.Animations.Rigging
{
    public struct BasisIKSpine
    {
        public int Count;
        public ReadWriteTransformHandle J0;
        public ReadWriteTransformHandle J1;
        public ReadWriteTransformHandle J2;
        public ReadWriteTransformHandle J3;
        public ReadWriteTransformHandle J4;
        public ReadWriteTransformHandle J5;
        public static BasisIKSpine PackChain(
AnimationStream stream,
ReadWriteTransformHandle hips,
ReadWriteTransformHandle spine,
ReadWriteTransformHandle chest,
ReadWriteTransformHandle upperChest,
ReadWriteTransformHandle neck,
ReadWriteTransformHandle head)
        {
            BasisIKSpine c = default;
            c.Count = 0;

            // Hips
            if (hips.IsValid(stream))
            {
                if (c.Count == 0) c.J0 = hips;
                else if (c.Count == 1) c.J1 = hips;
                else if (c.Count == 2) c.J2 = hips;
                else if (c.Count == 3) c.J3 = hips;
                else if (c.Count == 4) c.J4 = hips;
                else if (c.Count == 5) c.J5 = hips;
                c.Count++;
            }

            // Spine
            if (spine.IsValid(stream))
            {
                if (c.Count == 0) c.J0 = spine;
                else if (c.Count == 1) c.J1 = spine;
                else if (c.Count == 2) c.J2 = spine;
                else if (c.Count == 3) c.J3 = spine;
                else if (c.Count == 4) c.J4 = spine;
                else if (c.Count == 5) c.J5 = spine;
                c.Count++;
            }

            // Chest
            if (chest.IsValid(stream))
            {
                if (c.Count == 0) c.J0 = chest;
                else if (c.Count == 1) c.J1 = chest;
                else if (c.Count == 2) c.J2 = chest;
                else if (c.Count == 3) c.J3 = chest;
                else if (c.Count == 4) c.J4 = chest;
                else if (c.Count == 5) c.J5 = chest;
                c.Count++;
            }

            // UpperChest
            if (upperChest.IsValid(stream))
            {
                if (c.Count == 0) c.J0 = upperChest;
                else if (c.Count == 1) c.J1 = upperChest;
                else if (c.Count == 2) c.J2 = upperChest;
                else if (c.Count == 3) c.J3 = upperChest;
                else if (c.Count == 4) c.J4 = upperChest;
                else if (c.Count == 5) c.J5 = upperChest;
                c.Count++;
            }

            // Neck
            if (neck.IsValid(stream))
            {
                if (c.Count == 0) c.J0 = neck;
                else if (c.Count == 1) c.J1 = neck;
                else if (c.Count == 2) c.J2 = neck;
                else if (c.Count == 3) c.J3 = neck;
                else if (c.Count == 4) c.J4 = neck;
                else if (c.Count == 5) c.J5 = neck;
                c.Count++;
            }

            // Head
            if (head.IsValid(stream))
            {
                if (c.Count == 0) c.J0 = head;
                else if (c.Count == 1) c.J1 = head;
                else if (c.Count == 2) c.J2 = head;
                else if (c.Count == 3) c.J3 = head;
                else if (c.Count == 4) c.J4 = head;
                else if (c.Count == 5) c.J5 = head;
                c.Count++;
            }

            // Clamp to max 6 just in case
            if (c.Count > 6) c.Count = 6;

            return c;
        }
    }
}
