using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Basis.Scripts.Avatar
{
    /// <summary>
    /// Main-thread admission budget for the post-load avatar setup tail — the trim +
    /// calibration + bone-system registration that runs once a bundle has finished loading.
    /// </summary>
    /// <remarks>
    /// The concurrency gates in <see cref="BasisAvatarFactory"/> only throttle the async bundle
    /// I/O and are released before <c>InitializePlayerAvatar</c> runs, so a bulk in-range
    /// transition (the View Range slider 0->100 at 1k players) would dump hundreds of
    /// calibrations onto a single frame even though the downloads themselves were paced. This caps
    /// how many setups start per frame; over-budget loads yield to the next frame and resume.
    /// Pure main-thread frame pacing (no locks): every awaiter resumes on Unity's synchronization
    /// context, so the counter is only ever touched by one thread.
    /// </remarks>
    public static class BasisAvatarSetupBudget
    {
        /// <summary>Max post-load avatar setups admitted per frame. Tunable; treated as at least 1.</summary>
        public static int BudgetPerFrame = 4;

        static int sFrame = -1;
        static int sUsedThisFrame;

        /// <summary>
        /// Awaits a setup slot for the current frame. Returns synchronously while the frame still has
        /// budget, otherwise yields frame-by-frame until one frees up. Honors <paramref name="token"/>
        /// so a disconnect mid-wait cancels the pending load like any other await in the load path.
        /// Always makes progress (admits within <see cref="BudgetPerFrame"/> frames), so it can never hang.
        /// </summary>
        public static async Task WaitForSetupSlot(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                int frame = Time.frameCount;
                if (frame != sFrame)
                {
                    sFrame = frame;
                    sUsedThisFrame = 0;
                }

                int budget = BudgetPerFrame < 1 ? 1 : BudgetPerFrame;
                if (sUsedThisFrame < budget)
                {
                    sUsedThisFrame++;
                    return;
                }

                // Frame's budget is spent — resume next frame and re-check.
                await Task.Yield();
            }
        }
    }
}
