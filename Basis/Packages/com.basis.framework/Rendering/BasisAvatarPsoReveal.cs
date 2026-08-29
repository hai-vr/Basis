using System.Collections.Generic;
using Basis.Scripts.Drivers;
using UnityEngine;

namespace Basis.Scripts.Rendering
{
    /// <summary>
    /// A freshly-installed avatar draws every one of its Renderers for the first time on the same
    /// frame it becomes visible. On the explicit-PSO backends (D3D12/Vulkan/Metal,
    /// <see cref="BasisGraphicsStatePrewarm"/>) that means every not-yet-traced shader/material
    /// combination compiles its Pipeline State Object synchronously on that one frame, which is
    /// what turns into a single large hitch. This spreads that same total cost across several
    /// frames instead by holding the avatar's Renderers disabled and re-enabling a handful per
    /// tick, skinned (body) renderers first so the avatar reads as "there" before accessories
    /// fill in. It does not reduce the total PSO-creation cost, only how it is felt.
    /// A renderer authored disabled (e.g. a hidden accessory) is restored to disabled, never
    /// forced on.
    /// </summary>
    public static class BasisAvatarPsoReveal
    {
        public static bool Enabled = false;
        public static int MaxRevealPerTick = 2;

        private class Pending
        {
            public Renderer[] Renderers;
            public bool[] OriginalEnabled;
            public int Next;
        }

        private static readonly List<Pending> sQueue = new List<Pending>();
        private static bool sRunning;

        public static void Apply(bool enabled)
        {
            Enabled = enabled;
            UpdatePump();
        }

        /// <summary>
        /// Queues a freshly-installed avatar's renderers for a staggered reveal. No-ops when
        /// disabled or on a backend that never pays a synchronous PSO-creation cost.
        /// </summary>
        public static void BeginStagedReveal(Renderer[] renders)
        {
            if (!Enabled || renders == null || renders.Length == 0 || !BasisGraphicsStatePrewarm.BackendBenefits())
            {
                return;
            }

            int total = renders.Length;
            Renderer[] ordered = new Renderer[total];
            int index = 0;
            for (int i = 0; i < total; i++)
            {
                if (renders[i] is SkinnedMeshRenderer)
                {
                    ordered[index++] = renders[i];
                }
            }
            for (int i = 0; i < total; i++)
            {
                if (!(renders[i] is SkinnedMeshRenderer))
                {
                    ordered[index++] = renders[i];
                }
            }

            bool[] original = new bool[total];
            for (int i = 0; i < total; i++)
            {
                Renderer renderer = ordered[i];
                if (renderer != null)
                {
                    original[i] = renderer.enabled;
                    renderer.enabled = false;
                }
            }

            sQueue.Add(new Pending { Renderers = ordered, OriginalEnabled = original, Next = 0 });
            UpdatePump();
        }

        private static void Tick()
        {
            int budget = MaxRevealPerTick;
            for (int q = sQueue.Count - 1; q >= 0 && budget > 0; q--)
            {
                Pending pending = sQueue[q];
                while (pending.Next < pending.Renderers.Length && budget > 0)
                {
                    int i = pending.Next++;
                    Renderer renderer = pending.Renderers[i];
                    if (renderer != null)
                    {
                        renderer.enabled = pending.OriginalEnabled[i];
                        budget--;
                    }
                }
                if (pending.Next >= pending.Renderers.Length)
                {
                    sQueue.RemoveAt(q);
                }
            }
            UpdatePump();
        }

        private static void UpdatePump()
        {
            bool shouldRun = Enabled && sQueue.Count > 0;
            if (shouldRun == sRunning)
            {
                return;
            }
            sRunning = shouldRun;
            if (shouldRun)
            {
                BasisFrameClock.OnTick += Tick;
                BasisFrameClock.AddRequest();
            }
            else
            {
                BasisFrameClock.OnTick -= Tick;
                BasisFrameClock.RemoveRequest();
            }
        }
    }
}
