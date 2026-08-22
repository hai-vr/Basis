using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.Scripts.Virtual_keyboard
{
    /// <summary>
    /// Read-only prediction of whether typing a single character at the caret would be accepted by
    /// a target TMP_InputField/InputField's content type, custom validator, and character limit —
    /// so the virtual keyboard can grey out keys the field would reject anyway. Mirrors each field's
    /// own per-keystroke validation (both engines' real "Validate" method are declared protected, so
    /// they can't be called directly from here) without touching the field itself.
    /// </summary>
    public static class BasisVirtualKeyboardKeyAvailability
    {
        private const string EmailSpecialCharacters = "!#$%&'*+-/=?^_`{|}~";
        private static readonly char[] DecimalSeparatorChars = { '.', ',' };

        public static bool CanInsertCharacter(TMP_InputField tmp, InputField legacy, char ch)
        {
            if (tmp != null) return CanInsertTmp(tmp, ch);
            if (legacy != null) return CanInsertLegacy(legacy, ch);
            return true;
        }

        private static bool CanInsertTmp(TMP_InputField field, char ch)
        {
            string text = field.text ?? string.Empty;
            int anchor = Mathf.Clamp(field.selectionStringAnchorPosition, 0, text.Length);
            int focus = Mathf.Clamp(field.selectionStringFocusPosition, 0, text.Length);
            int pos = Mathf.Max(anchor, focus);

            if (IsAtCharacterLimit(field.characterLimit, text.Length, anchor, focus)) return false;

            if (field.onValidateInput != null) return field.onValidateInput(text, pos, ch) != '\0';

            switch (field.characterValidation)
            {
                case TMP_InputField.CharacterValidation.None:
                    return true;
                case TMP_InputField.CharacterValidation.Digit:
                    return IsDigit(ch);
                case TMP_InputField.CharacterValidation.Integer:
                    return IsIntegerCharTmp(text, pos, anchor, focus, ch);
                case TMP_InputField.CharacterValidation.Decimal:
                    return IsDecimalCharTmp(text, pos, anchor, focus, ch);
                case TMP_InputField.CharacterValidation.Alphanumeric:
                    return IsAlphanumeric(ch);
                case TMP_InputField.CharacterValidation.Name:
                    return IsNameCharTmp(text, pos, ch);
                case TMP_InputField.CharacterValidation.EmailAddress:
                    return IsEmailChar(text, pos, ch);
                case TMP_InputField.CharacterValidation.CustomValidator:
                    if (field.inputValidator == null) return false;
                    string scratchText = text;
                    int scratchPos = pos;
                    return field.inputValidator.Validate(ref scratchText, ref scratchPos, ch) != '\0';
                case TMP_InputField.CharacterValidation.Regex:
                    // m_RegexValue has no public accessor on TMP_InputField in this engine version —
                    // the pattern can't be read from outside the class, so this mode fails open
                    // instead of graying every key on a field we can't actually evaluate.
                    return true;
                default:
                    return true;
            }
        }

        private static bool CanInsertLegacy(InputField field, char ch)
        {
            string text = field.text ?? string.Empty;
            int anchor = Mathf.Clamp(field.selectionAnchorPosition, 0, text.Length);
            int focus = Mathf.Clamp(field.selectionFocusPosition, 0, text.Length);
            int pos = Mathf.Max(anchor, focus);

            if (IsAtCharacterLimit(field.characterLimit, text.Length, anchor, focus)) return false;

            if (field.onValidateInput != null) return field.onValidateInput(text, pos, ch) != '\0';

            switch (field.characterValidation)
            {
                case InputField.CharacterValidation.None:
                    return true;
                case InputField.CharacterValidation.Integer:
                    return IsIntegerCharLegacy(text, pos, anchor, focus, ch);
                case InputField.CharacterValidation.Decimal:
                    return IsDecimalCharLegacy(text, pos, anchor, focus, ch);
                case InputField.CharacterValidation.Alphanumeric:
                    return IsAlphanumeric(ch);
                case InputField.CharacterValidation.Name:
                    return IsNameCharLegacy(text, pos, ch);
                case InputField.CharacterValidation.EmailAddress:
                    return IsEmailChar(text, pos, ch);
                default:
                    return true;
            }
        }

        private static bool IsAtCharacterLimit(int characterLimit, int textLength, int anchor, int focus)
        {
            if (characterLimit <= 0) return false;
            int lengthAfterReplacingSelection = textLength - Mathf.Abs(focus - anchor);
            return lengthAfterReplacingSelection >= characterLimit;
        }

        private static bool IsDigit(char ch) => ch >= '0' && ch <= '9';

        private static bool IsAlphanumeric(char ch) =>
            (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || IsDigit(ch);

        // Shared basis for Integer/Decimal on both engines: digits, plus '-' as a leading sign.
        // A caret sitting directly before an existing leading '-' blocks everything (matches the
        // engines' own "cursorBeforeDash" guard) unless a selection spanning position 0 is about to
        // replace that dash.
        private static bool IsIntegerCore(string text, int pos, int anchor, int focus, char ch)
        {
            bool cursorBeforeDash = pos == 0 && text.Length > 0 && text[0] == '-';
            if (cursorBeforeDash)
            {
                bool dashInSelection = anchor != focus && (anchor == 0 || focus == 0);
                if (!dashInSelection) return false;
            }

            if (IsDigit(ch)) return true;

            bool selectionAtStart = anchor == 0 || focus == 0;
            if (ch == '-' && (pos == 0 || selectionAtStart) && !text.Contains('-')) return true;

            return false;
        }

        private static bool IsIntegerCharTmp(string text, int pos, int anchor, int focus, char ch)
        {
            if (IsIntegerCore(text, pos, anchor, focus, ch)) return true;

            // Some keyboards (e.g. Samsung) require double-tapping '.' to reach '-'; TMP's Integer
            // mode accepts '.' at the leading position and turns it into a dash.
            bool selectionAtStart = anchor == 0 || focus == 0;
            return ch == '.' && (pos == 0 || selectionAtStart) && !text.Contains('-');
        }

        private static bool IsDecimalCharTmp(string text, int pos, int anchor, int focus, char ch)
        {
            if (IsIntegerCore(text, pos, anchor, focus, ch)) return true;

            char separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
            return ch == separator && text.IndexOf(separator) == -1;
        }

        private static bool IsIntegerCharLegacy(string text, int pos, int anchor, int focus, char ch)
        {
            if (IsIntegerCore(text, pos, anchor, focus, ch)) return true;

            bool selectionAtStart = anchor == 0 || focus == 0;
            return ch == '.' && (pos == 0 || selectionAtStart) && !text.Contains('-');
        }

        private static bool IsDecimalCharLegacy(string text, int pos, int anchor, int focus, char ch)
        {
            if (IsIntegerCore(text, pos, anchor, focus, ch)) return true;
            if (ch != '.' && ch != ',') return false;
            return text.IndexOfAny(DecimalSeparatorChars) == -1;
        }

        private static bool IsNameCharTmp(string text, int pos, char ch)
        {
            char prevChar = text.Length > 0 ? text[Mathf.Clamp(pos - 1, 0, text.Length - 1)] : ' ';
            char lastChar = text.Length > 0 ? text[Mathf.Clamp(pos, 0, text.Length - 1)] : ' ';
            char nextChar = text.Length > 0 ? text[Mathf.Clamp(pos + 1, 0, text.Length - 1)] : '\n';

            if (char.IsLetter(ch))
            {
                // Every branch here accepts the keystroke — the engine only ever adjusts its case,
                // except the one explicit reject below (no two capitals back to back).
                if (char.IsUpper(ch) && char.IsUpper(lastChar)) return false;
                return true;
            }

            if (ch == '\'')
            {
                return lastChar != ' ' && lastChar != '\'' && nextChar != '\'' && !text.Contains('\'');
            }

            if (char.IsLetter(prevChar) && ch == '-' && lastChar != '-') return true;

            if ((ch == ' ' || ch == '-') && pos != 0)
            {
                return prevChar != ' ' && prevChar != '\'' && prevChar != '-' &&
                       lastChar != ' ' && lastChar != '\'' && lastChar != '-' &&
                       nextChar != ' ' && nextChar != '\'' && nextChar != '-';
            }

            return false;
        }

        private static bool IsNameCharLegacy(string text, int pos, char ch)
        {
            // Legacy's Name mode never rejects a letter outright — it only ever adjusts case.
            if (char.IsLetter(ch)) return true;

            if (ch == '\'')
            {
                if (text.Contains('\'')) return false;
                bool blockedByPrev = pos > 0 && (text[pos - 1] == ' ' || text[pos - 1] == '\'' || text[pos - 1] == '-');
                bool blockedByNext = pos < text.Length && (text[pos] == ' ' || text[pos] == '\'' || text[pos] == '-');
                return !(blockedByPrev || blockedByNext);
            }

            if (ch == ' ' || ch == '-')
            {
                if (pos == 0) return false;

                // Mirrors the engine's own asymmetric check verbatim (the "next" clause reads
                // text[pos - 1] rather than text[pos]) so this predicts what the field will
                // actually do, quirk included, instead of a "corrected" rule that would disagree
                // with it.
                bool blockedByPrev = pos > 0 && (text[pos - 1] == ' ' || text[pos - 1] == '\'' || text[pos - 1] == '-');
                bool blockedByNext = pos < text.Length && (text[pos] == ' ' || text[pos] == '\'' || text[pos - 1] == '-');
                return !(blockedByPrev || blockedByNext);
            }

            return false;
        }

        // Identical on both engines: same allowed character set, same '@'/'.' placement rules.
        private static bool IsEmailChar(string text, int pos, char ch)
        {
            if (IsAlphanumeric(ch)) return true;
            if (ch == '@') return text.IndexOf('@') == -1;
            if (EmailSpecialCharacters.IndexOf(ch) != -1) return true;

            if (ch == '.')
            {
                char lastChar = text.Length > 0 ? text[Mathf.Clamp(pos, 0, text.Length - 1)] : ' ';
                char nextChar = text.Length > 0 ? text[Mathf.Clamp(pos + 1, 0, text.Length - 1)] : '\n';
                return lastChar != '.' && nextChar != '.';
            }

            return false;
        }
    }
}
