namespace Basis.BasisUI
{
    public enum BasisRecordingConsentPolicy
    {
        /// <summary>Prompt the user each time (default for unknown people).</summary>
        Ask,
        /// <summary>Auto-grant without prompting.</summary>
        AlwaysAllow,
        /// <summary>Auto-deny without prompting.</summary>
        AlwaysDeny,
    }
}
