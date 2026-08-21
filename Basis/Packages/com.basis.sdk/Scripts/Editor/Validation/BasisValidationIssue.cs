using System;

/// <summary>
/// What kind of problem a validation issue is. Warnings are grouped under this in the inspector,
/// so the category is what decides which panel a message lands in.
/// </summary>
public enum ValidationCategory
{
    None,
    Configuration,
    GameObject,
    Performance,
    Security,
    MissingReference
}

/// <summary>
/// One thing a validator found. <see cref="Fix"/> is optional — when present the inspector grows a
/// button for it, labelled with <see cref="FixLabel"/>.
///
/// <para>Fixes are held as closures that run long after the pass that produced them, so a fix must
/// not capture anything that goes stale — an <c>AssetImporter</c> instance in particular. Capture
/// the asset path and re-resolve at click time instead.</para>
/// </summary>
public class BasisValidationIssue
{
    public ValidationCategory Category { get; }
    public string Message { get; }
    public string FixLabel { get; }
    public Action Fix { get; }
    public UnityEngine.Object RelatedObject { get; }

    public BasisValidationIssue(string message, ValidationCategory category = ValidationCategory.None,
                            Action fix = null, string fixLabel = "", UnityEngine.Object relatedObject = null)
    {
        Category = category;
        Message = message;
        Fix = fix;
        FixLabel = fixLabel;
        RelatedObject = relatedObject;
    }
}
