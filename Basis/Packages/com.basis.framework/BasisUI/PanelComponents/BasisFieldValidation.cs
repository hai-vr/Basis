using System;

namespace Basis.BasisUI
{
    /// <summary>
    /// The engine-free half of panel field validation: what counts as a problem, and when a field is
    /// allowed to say so. Kept apart from the components that draw it so the rules can be reasoned
    /// about and tested on their own.
    /// </summary>
    public static class BasisFieldValidation
    {
        /// <summary>
        /// Rejects text that is null, empty, or only whitespace. A field of spaces is empty as far as
        /// anyone reading it is concerned, so it fails the same way a blank one does.
        /// </summary>
        public static Func<string, string> Required(string message)
        {
            return text => string.IsNullOrWhiteSpace(text) ? message : null;
        }

        /// <summary>
        /// Whether a field showing <paramref name="problem"/> should display it yet.
        /// <para>Eager fields back a live setting, so blank means something is already broken and the
        /// complaint belongs on screen straight away. Lazy fields are entry boxes the user may never
        /// intend to use; they hold the complaint until the user has typed in one, or until a submit
        /// handler asks, so a freshly opened page is not covered in red.</para>
        /// </summary>
        public static string ResolveShown(string problem, bool gradeImmediately, bool revealed)
        {
            if (string.IsNullOrEmpty(problem))
            {
                return null;
            }

            return gradeImmediately || revealed ? problem : null;
        }

        /// <summary>Normalises a validator result so empty and null both mean "no problem".</summary>
        public static string NormalizeProblem(string problem) =>
            string.IsNullOrEmpty(problem) ? null : problem;
    }
}
