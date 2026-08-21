using System;
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// Scheduling shell shared by the Avatar, Prop and World validators.
///
/// <para><b>What this replaced.</b> Each validator subscribed to
/// <see cref="EditorApplication.update"/> and re-ran every check it had on every tick — a full
/// hierarchy walk, an AssetDatabase round trip per texture, a mesh index-buffer copy per skinned
/// mesh, sixty times a second, for as long as the inspector was open, whether or not anything had
/// changed. That is what made the SDK inspectors feel heavy.</para>
///
/// <para><b>What happens now.</b> Checks are declared as groups and a pass runs all of them, but a
/// pass only happens when there is a reason for one: the inspector opening, an undo, an import, or
/// an actual edit to something in the editor. Idle costs nothing. Bursts of edits — dragging a
/// slider, scrubbing a value — are coalesced by <see cref="MinimumSecondsBetweenPasses"/> so the
/// worst case is a few passes a second rather than a pass a frame.</para>
///
/// <para><b>Play mode.</b> Nothing is authored in play mode and the editor has better things to do,
/// so the triggers are dropped entirely and a Validate button takes their place.</para>
///
/// <para><b>Uploads.</b> Builds call <see cref="RunAllGroups"/> directly. An upload is never allowed
/// to read whatever the panels happen to be showing — it runs the complete suite, every group, on
/// the spot.</para>
/// </summary>
public abstract class BasisValidationRunner
{
    /// <summary>One group of checks. Everything it finds goes into the bucket it is handed.</summary>
    protected delegate void BasisValidationCheck(BasisValidationBucket bucket);

    /// <summary>
    /// Floor on how often edits can force a pass. A drag publishes a change event per frame, and
    /// without this the event-driven path would be exactly as expensive as the per-frame one it
    /// replaced.
    /// </summary>
    private const double MinimumSecondsBetweenPasses = 0.25d;

    private BasisValidationCheck[] _groups = Array.Empty<BasisValidationCheck>();
    private BasisValidationBucket[] _buckets = Array.Empty<BasisValidationBucket>();
    private readonly BasisValidationBucket _merged = new BasisValidationBucket();

    private Button _validateButton;
    private bool _pendingPass;
    private bool _watchingEdits;
    private bool _waitingOnThrottle;
    private bool _hasRefreshed;
    private double _lastPassTime = double.NegativeInfinity;

    public VisualElement Root;

    /// <summary>Number of check groups this validator runs.</summary>
    public int GroupCount => _groups.Length;

    /// <summary>
    /// Wires up the groups and starts watching for changes. Call at the end of the concrete
    /// validator's constructor, once its panels exist — the first pass runs immediately and needs
    /// somewhere to put its results.
    /// </summary>
    protected void BeginValidation(VisualElement root, params BasisValidationCheck[] groups)
    {
        Root = root;
        _groups = groups ?? Array.Empty<BasisValidationCheck>();
        _buckets = new BasisValidationBucket[_groups.Length];
        for (int Index = 0; Index < _buckets.Length; Index++)
        {
            _buckets[Index] = new BasisValidationBucket();
        }

        _validateButton = BasisValidatorUI.CreateValidateButton(root, RunManualPass);

        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        ApplyMode();
    }

    public virtual void OnDestroy()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        StopWatchingEdits();
        CancelThrottleWait();
    }

    /// <summary>
    /// Runs every group in one pass and returns the merged result.
    ///
    /// <para>This is the upload path: a build must see the complete picture, not whatever the last
    /// throttled pass happened to catch.</para>
    /// </summary>
    public BasisValidationBucket RunAllGroups()
    {
        RefreshScan();

        _merged.Clear();
        for (int Index = 0; Index < _groups.Length; Index++)
        {
            BasisValidationBucket bucket = _buckets[Index];
            bucket.Clear();
            _groups[Index](bucket);
            bucket.Signature = bucket.ComputeSignature();
            bucket.AddTo(_merged);
        }

        _lastPassTime = EditorApplication.timeSinceStartup;
        _pendingPass = false;
        return _merged;
    }

    /// <summary>Forces the next opportunity to be taken, whatever the throttle would have said.</summary>
    public void Invalidate()
    {
        _lastPassTime = double.NegativeInfinity;
        RequestPass();
    }

    /// <summary>
    /// Rebuilds whatever the groups share — the hierarchy walk, mostly. Runs once per pass so every
    /// group in that pass sees the same snapshot.
    /// </summary>
    protected virtual void RefreshScan()
    {
    }

    /// <summary>Pushes a completed pass into the panels.</summary>
    protected abstract void Refresh(BasisValidationBucket results);

    /// <summary>
    /// Runs the suite and updates the panels, but only rebuilds them when something actually
    /// changed — the panels recreate their fix buttons and re-lay-out their text, so repainting an
    /// unchanged result is pure waste.
    /// </summary>
    private void RunPass()
    {
        if (Root == null)
        {
            return;
        }

        RefreshScan();

        bool changed = false;
        _merged.Clear();
        for (int Index = 0; Index < _groups.Length; Index++)
        {
            BasisValidationBucket bucket = _buckets[Index];
            bucket.Clear();
            _groups[Index](bucket);

            int signature = bucket.ComputeSignature();
            if (signature != bucket.Signature)
            {
                bucket.Signature = signature;
                changed = true;
            }

            bucket.AddTo(_merged);
        }

        _lastPassTime = EditorApplication.timeSinceStartup;
        _pendingPass = false;

        if (changed || !_hasRefreshed)
        {
            _hasRefreshed = true;
            Refresh(_merged);
        }
    }

    private void RunManualPass()
    {
        // The button exists to say "look now", so it bypasses both the throttle and the
        // did-anything-change shortcut.
        Refresh(RunAllGroups());
        _hasRefreshed = true;
    }

    private void ApplyMode()
    {
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;

        if (_validateButton != null)
        {
            _validateButton.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (playing)
        {
            StopWatchingEdits();
            CancelThrottleWait();
            _pendingPass = false;
            return;
        }

        StartWatchingEdits();
        Invalidate();
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        ApplyMode();
    }

    private void StartWatchingEdits()
    {
        if (_watchingEdits)
        {
            return;
        }
        _watchingEdits = true;

        ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
        Undo.undoRedoPerformed += RequestPass;
        EditorApplication.hierarchyChanged += RequestPass;
        BasisValidationAssetCache.Invalidated += RequestPass;
    }

    private void StopWatchingEdits()
    {
        if (!_watchingEdits)
        {
            return;
        }
        _watchingEdits = false;

        ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
        Undo.undoRedoPerformed -= RequestPass;
        EditorApplication.hierarchyChanged -= RequestPass;
        BasisValidationAssetCache.Invalidated -= RequestPass;
    }

    private void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
    {
        // Any published change is a reason to look again. Working out whether a given change was
        // ours would mean resolving instance ids per event, which costs more than the throttled
        // pass it would occasionally save.
        if (stream.length > 0)
        {
            RequestPass();
        }
    }

    /// <summary>
    /// Asks for a pass. Runs it straight away if the throttle allows, otherwise waits on the editor
    /// tick until it does — and unsubscribes again the moment it has, so an idle inspector is not
    /// on the update list at all.
    /// </summary>
    private void RequestPass()
    {
        if (_groups.Length == 0 || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        _pendingPass = true;

        if (EditorApplication.timeSinceStartup - _lastPassTime >= MinimumSecondsBetweenPasses)
        {
            CancelThrottleWait();
            RunPass();
            return;
        }

        if (!_waitingOnThrottle)
        {
            _waitingOnThrottle = true;
            EditorApplication.update += OnThrottleTick;
        }
    }

    private void OnThrottleTick()
    {
        if (!_pendingPass || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            CancelThrottleWait();
            return;
        }

        if (EditorApplication.timeSinceStartup - _lastPassTime < MinimumSecondsBetweenPasses)
        {
            return;
        }

        CancelThrottleWait();
        RunPass();
    }

    private void CancelThrottleWait()
    {
        if (!_waitingOnThrottle)
        {
            return;
        }
        _waitingOnThrottle = false;
        EditorApplication.update -= OnThrottleTick;
    }
}
