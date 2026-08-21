using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using UnityEngine;

public struct BasisCameraSubjectHit
{
    public IBasisPlayer Player;
    public Bounds Bounds;
    public float Entry, Exit, FocusDepth;
    public Vector3 Point;
}

public static class BasisCameraSubjectPicker
{
    public const float MinimumEntryDistance = 0.05f;
    public const float OccluderBias = 0.02f;
    private const int MaxOccluderSteps = 8;
    private const float OccluderStepBias = 0.01f;
    private static readonly List<IBasisPlayer> candidates = new List<IBasisPlayer>(32);

    public static bool IntersectRayBounds(Ray ray, Bounds bounds, out float entry, out float exit)
    {
        entry = 0f;
        exit = 0f;
        Vector3 direction = ray.direction;
        float length = direction.magnitude;
        if (length < 1e-6f) return false;
        direction /= length;

        Vector3 origin = ray.origin, min = bounds.min, max = bounds.max;
        float near = float.NegativeInfinity, far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float slabOrigin = origin[axis], slabDirection = direction[axis], low = min[axis], high = max[axis];
            if (Mathf.Abs(slabDirection) < 1e-8f)
            {
                if (slabOrigin < low || slabOrigin > high) return false;
                continue;
            }
            float inverse = 1f / slabDirection;
            float first = (low - slabOrigin) * inverse, second = (high - slabOrigin) * inverse;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }
            if (first > near) near = first;
            if (second < far) far = second;
            if (near > far) return false;
        }

        if (far < 0f || float.IsInfinity(near) || float.IsInfinity(far)) return false;
        entry = near;
        exit = far;
        return true;
    }

    public static float ResolveFocusDepth(Ray ray, Bounds bounds, float entry, float exit)
    {
        Vector3 direction = ray.direction.normalized;
        float toCentre = Vector3.Dot(bounds.center - ray.origin, direction);
        return Mathf.Clamp(toCentre, Mathf.Max(entry, 0f), Mathf.Max(exit, 0f));
    }

    public static bool TryGetPlayerBounds(IBasisPlayer player, out Bounds bounds)
    {
        bounds = default;
        if (player == null || player.IsDestroyed) return false;
        BasisAvatar avatar = player.BasisAvatar;
        if (avatar == null) return false;

        bool found = false;
        Encapsulate(avatar.Renders, ref bounds, ref found);
        if (!found) Encapsulate(avatar.SkinnedMeshRenderers, ref bounds, ref found);
        return found;
    }

    public static void CollectPlayers(List<IBasisPlayer> into)
    {
        into.Clear();
        BasisLocalPlayer local = BasisLocalPlayer.Instance;
        if (local != null && !local.IsDestroyed) into.Add(local);
        foreach (KeyValuePair<ushort, BasisRemotePlayer> pair in BasisNetworkPlayers.RemotePlayers)
        {
            BasisRemotePlayer remote = pair.Value;
            if (remote == null || remote.IsDestroyed) continue;
            into.Add(remote);
        }
    }

    public static bool TryRaycastWorld(Ray ray, float maxDistance, LayerMask layers, Transform ignoreRoot, out RaycastHit nearest, out float distance)
    {
        nearest = default;
        distance = float.PositiveInfinity;
        Vector3 direction = ray.direction;
        float length = direction.magnitude;
        if (length < 1e-6f) return false;
        direction /= length;

        CollectPlayers(candidates);
        float travelled = 0f;
        for (int step = 0; step < MaxOccluderSteps; step++)
        {
            float remaining = maxDistance - travelled;
            if (remaining <= 0f) return false;

            Ray probe = new Ray(ray.origin + direction * travelled, direction);
            if (!Physics.Raycast(probe, out RaycastHit hit, remaining, layers, QueryTriggerInteraction.Ignore)) return false;

            Transform hitTransform = hit.collider != null ? hit.collider.transform : null;
            bool skip = hitTransform == null
                || (ignoreRoot != null && hitTransform.IsChildOf(ignoreRoot))
                || IsPlayerOwned(hitTransform);
            if (!skip)
            {
                nearest = hit;
                distance = travelled + hit.distance;
                return true;
            }

            travelled += hit.distance + OccluderStepBias;
        }
        return false;
    }

    public static bool TryPickSubject(Ray ray, float maxDistance, float occluderDistance, out BasisCameraSubjectHit hit)
    {
        hit = default;
        Vector3 direction = ray.direction;
        float length = direction.magnitude;
        if (length < 1e-6f) return false;
        direction /= length;

        CollectPlayers(candidates);
        float best = float.PositiveInfinity;
        bool found = false;
        int count = candidates.Count;
        for (int index = 0; index < count; index++)
        {
            IBasisPlayer player = candidates[index];
            if (!TryGetPlayerBounds(player, out Bounds union)) continue;
            if (!IntersectRayBounds(ray, union, out float unionEntry, out float unionExit)) continue;
            if (unionExit <= MinimumEntryDistance || unionEntry > maxDistance) continue;
            if (!TryResolveHitBounds(player, ray, union, out Bounds bounds, out float entry, out float exit)) continue;
            if (entry < MinimumEntryDistance || entry > maxDistance) continue;
            if (entry >= occluderDistance - OccluderBias) continue;
            if (entry >= best) continue;

            float depth = ResolveFocusDepth(ray, bounds, entry, exit);
            best = entry;
            found = true;
            hit = new BasisCameraSubjectHit
            {
                Player = player,
                Bounds = bounds,
                Entry = entry,
                Exit = exit,
                FocusDepth = depth,
                Point = ray.origin + direction * depth,
            };
        }
        return found;
    }

    private static bool TryResolveHitBounds(IBasisPlayer player, Ray ray, Bounds union, out Bounds bounds, out float entry, out float exit)
    {
        bounds = union;
        entry = 0f;
        exit = 0f;
        BasisAvatar avatar = player.BasisAvatar;
        bool found = false;
        if (!TryNearestRendererBounds(avatar.Renders, ray, ref bounds, ref entry, ref exit, ref found))
        {
            TryNearestRendererBounds(avatar.SkinnedMeshRenderers, ray, ref bounds, ref entry, ref exit, ref found);
        }
        return found;
    }

    private static bool TryNearestRendererBounds<T>(T[] renderers, Ray ray, ref Bounds bounds, ref float entry, ref float exit, ref bool found) where T : Renderer
    {
        int count = renderers != null ? renderers.Length : 0;
        for (int index = 0; index < count; index++)
        {
            T renderer = renderers[index];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Bounds candidate = renderer.bounds;
            if (!IntersectRayBounds(ray, candidate, out float candidateEntry, out float candidateExit)) continue;
            if (candidateEntry < MinimumEntryDistance) continue;
            if (found && candidateEntry >= entry) continue;
            bounds = candidate;
            entry = candidateEntry;
            exit = candidateExit;
            found = true;
        }
        return found;
    }

    private static bool IsPlayerOwned(Transform hitTransform)
    {
        int count = candidates.Count;
        for (int index = 0; index < count; index++)
        {
            IBasisPlayer player = candidates[index];
            if (player == null || player.IsDestroyed) continue;
            Transform root = player.Transform;
            if (root != null && hitTransform.IsChildOf(root)) return true;
            Transform avatar = player.AvatarTransform;
            if (avatar != null && hitTransform.IsChildOf(avatar)) return true;
        }
        return false;
    }

    private static void Encapsulate<T>(T[] renderers, ref Bounds bounds, ref bool found) where T : Renderer
    {
        int count = renderers != null ? renderers.Length : 0;
        for (int index = 0; index < count; index++)
        {
            T renderer = renderers[index];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Bounds candidate = renderer.bounds;
            if (!found)
            {
                bounds = candidate;
                found = true;
                continue;
            }
            bounds.Encapsulate(candidate);
        }
    }
}
