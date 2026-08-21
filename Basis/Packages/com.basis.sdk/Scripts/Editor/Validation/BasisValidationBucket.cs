using System;
using System.Collections.Generic;

/// <summary>
/// Results for one validation group.
///
/// <para>Buckets are reused between passes: <see cref="Clear"/> keeps the backing arrays, so
/// re-validating does not allocate three fresh lists per group every time.</para>
/// </summary>
public sealed class BasisValidationBucket
{
    public readonly List<BasisValidationIssue> Errors = new List<BasisValidationIssue>();
    public readonly List<BasisValidationIssue> Warnings = new List<BasisValidationIssue>();
    public readonly List<string> Passes = new List<string>();

    /// <summary>
    /// Content hash of the last run. The panels are expensive to rebuild — buttons are recreated
    /// and text is re-laid-out — so a pass that finds exactly what the previous pass found is
    /// allowed to leave the UI alone.
    /// </summary>
    public int Signature;

    public void Clear()
    {
        Errors.Clear();
        Warnings.Clear();
        Passes.Clear();
    }

    public void Error(string message, ValidationCategory category = ValidationCategory.None,
        Action fix = null, string fixLabel = "", UnityEngine.Object relatedObject = null)
    {
        Errors.Add(new BasisValidationIssue(message, category, fix, fixLabel, relatedObject));
    }

    public void Warn(string message, ValidationCategory category = ValidationCategory.None,
        Action fix = null, string fixLabel = "", UnityEngine.Object relatedObject = null)
    {
        Warnings.Add(new BasisValidationIssue(message, category, fix, fixLabel, relatedObject));
    }

    public void Pass(string message)
    {
        Passes.Add(message);
    }

    public void AddTo(BasisValidationBucket destination)
    {
        destination.Errors.AddRange(Errors);
        destination.Warnings.AddRange(Warnings);
        destination.Passes.AddRange(Passes);
    }

    /// <summary>
    /// Hashes what the pass found. Cheaper than the string signature this replaced, which built a
    /// joined message list on every comparison — the thing it was meant to avoid doing.
    /// </summary>
    public int ComputeSignature()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Errors.Count;
            hash = hash * 31 + Warnings.Count;
            hash = hash * 31 + Passes.Count;

            int count = Errors.Count;
            for (int Index = 0; Index < count; Index++)
            {
                hash = hash * 31 + IssueHash(Errors[Index]);
            }

            count = Warnings.Count;
            for (int Index = 0; Index < count; Index++)
            {
                hash = hash * 31 + IssueHash(Warnings[Index]);
            }

            count = Passes.Count;
            for (int Index = 0; Index < count; Index++)
            {
                string pass = Passes[Index];
                hash = hash * 31 + (pass != null ? pass.GetHashCode() : 0);
            }

            return hash;
        }
    }

    private static int IssueHash(BasisValidationIssue issue)
    {
        unchecked
        {
            int hash = (int)issue.Category;
            hash = hash * 31 + (issue.Message != null ? issue.Message.GetHashCode() : 0);
            hash = hash * 31 + (issue.FixLabel != null ? issue.FixLabel.GetHashCode() : 0);
            hash = hash * 31 + (issue.Fix != null ? 1 : 0);
            return hash;
        }
    }
}
