using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.BasisSdk.Players;

/// <summary>
/// Answers "would resizing the player right now be rude?".
///
/// A better body measurement can arrive at any moment, but applying it is not always welcome: the
/// scale change moves the viewpoint, and doing that while someone is holding an object, sitting in a
/// seat, or aiming at a menu turns a quiet correction into something that feels broken. None of these
/// states last long, and the measurement is not going anywhere, so the fit simply waits for a quiet
/// moment rather than compromising on accuracy.
/// </summary>
public static class BasisCalibrationRefitGate
{
    /// <summary>
    /// Interactables the local player currently has hold of. Maintained by
    /// <see cref="BasisInteractableObject"/>'s interact hooks, and pruned on read so a missed release
    /// (a subclass that overrides without calling base, an object destroyed mid-grab) can only ever
    /// delay one refit rather than block every future one.
    /// </summary>
    static readonly HashSet<BasisInteractableObject> s_held = new();

    public static void MarkInteracting(BasisInteractableObject interactable)
    {
        if (interactable != null)
        {
            s_held.Add(interactable);
        }
    }

    public static void MarkReleased(BasisInteractableObject interactable)
    {
        if (interactable != null)
        {
            s_held.Remove(interactable);
        }
    }

    static readonly List<BasisInteractableObject> s_stale = new();

    /// <summary>Whether the player is holding anything, pruning entries that quietly went away.</summary>
    public static bool IsHoldingSomething()
    {
        if (s_held.Count == 0)
        {
            return false;
        }

        s_stale.Clear();
        bool holding = false;
        foreach (BasisInteractableObject held in s_held)
        {
            if (held == null || !held.Inputs.AnyInteracting(false))
            {
                s_stale.Add(held);
                continue;
            }
            holding = true;
        }
        for (int Index = 0; Index < s_stale.Count; Index++)
        {
            s_held.Remove(s_stale[Index]);
        }
        s_stale.Clear();
        return holding;
    }

    /// <summary>
    /// True while a resize would land badly. Deliberately conservative: every one of these clears on
    /// its own within seconds, and the pending measurement is held rather than discarded.
    /// </summary>
    public static bool ShouldHoldRefit(out string reason)
    {
        reason = null;

        BasisLocalPlayer player = BasisLocalPlayer.Instance;
        if (player == null)
        {
            reason = "no local player";
            return true;
        }

        // Sitting in a seat: the player is positioned relative to the seat, so changing their scale
        // slides them out of it.
        if (player.LocalSeatDriver != null && player.LocalSeatDriver.IsSeated)
        {
            reason = "seated";
            return true;
        }

        // Holding something: the held object would shift in the hand as the scale moves.
        if (IsHoldingSomething())
        {
            reason = "holding something";
            return true;
        }

        // Aiming at a menu: the panel is anchored in the play space, so the target moves under the ray.
        if (!string.IsNullOrEmpty(Basis.BasisUI.BasisMainMenu.ActiveMenuTitle))
        {
            reason = "menu open";
            return true;
        }

        return false;
    }
}
