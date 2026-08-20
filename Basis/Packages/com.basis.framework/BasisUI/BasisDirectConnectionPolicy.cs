namespace Basis.BasisUI
{
    public enum BasisDirectConnectionPolicy
    {
        /// <summary>Prompt the user each time (default for unknown people).</summary>
        Ask,
        /// <summary>Auto-accept without prompting.</summary>
        AlwaysAccept,
        /// <summary>Auto-decline without prompting.</summary>
        AlwaysDecline,
    }
}
