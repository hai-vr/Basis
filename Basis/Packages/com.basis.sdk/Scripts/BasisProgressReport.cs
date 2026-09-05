using System;

public class BasisProgressReport
{
    // Delegate definitions for various stages of progress
    public delegate void ProgressReportState(string UniqueID, float progress, string eventDescription);
    public event ProgressReportState OnProgressReport;
    public const float MaxValue = 100f;
    public const float MinValue = 0f;
    private readonly BasisProgressReport parent;
    private readonly string parentKey;
    private readonly float rangeStart;
    private readonly float rangeEnd;

    public BasisProgressReport()
    {
    }

    private BasisProgressReport(BasisProgressReport parent, string parentKey, float rangeStart, float rangeEnd)
    {
        this.parent = parent;
        this.parentKey = parentKey;
        this.rangeStart = rangeStart;
        this.rangeEnd = rangeEnd;
    }

    public BasisProgressReport Stage(string key, float rangeStart, float rangeEnd)
    {
        return new BasisProgressReport(this, key, Math.Clamp(rangeStart, MinValue, MaxValue), Math.Clamp(rangeEnd, MinValue, MaxValue));
    }

    /// <summary>
    /// Reports the current progress along with an event description.
    /// Ensures that progress is between 0 and 100.
    /// </summary>
    /// <param name="UniqueID">A Unique ID.</param>
    /// <param name="progress">A float value between 0 and 100 representing the progress.</param>
    /// <param name="eventDescription">A string describing the current event or stage.</param>
    public void ReportProgress(string UniqueID, float progress, string eventDescription)
    {
        progress = Math.Clamp(progress, MinValue, MaxValue); // Ensuring progress is within bounds
        if (parent != null)
        {
            parent.ReportProgress(parentKey, rangeStart + (rangeEnd - rangeStart) * (progress / MaxValue), eventDescription);
            return;
        }

        // BasisDebug.LogError("Current Progress is " + progress);
        OnProgressReport?.Invoke(UniqueID, progress, eventDescription);
    }
}
