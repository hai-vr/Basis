using UnityEngine.InputSystem;

namespace Basis.Scripts.UI
{
    /// <summary>
    /// Maps physical keyboard keys onto characters for platforms where the input backend
    /// reports key presses but never raises <see cref="Keyboard.onTextInput"/>.
    /// </summary>
    public static class BasisPhysicalKeyboardText
    {
        /// <summary>
        /// Keys that can produce a character through <see cref="TryGetCharacter"/>.
        /// </summary>
        public static readonly Key[] TextKeys =
        {
            Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J, Key.K, Key.L, Key.M,
            Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T, Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z,
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0,
            Key.Space, Key.Minus, Key.Equals, Key.LeftBracket, Key.RightBracket, Key.Backslash,
            Key.Semicolon, Key.Quote, Key.Backquote, Key.Comma, Key.Period, Key.Slash,
            Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4,
            Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9,
            Key.NumpadDivide, Key.NumpadMultiply, Key.NumpadMinus, Key.NumpadPlus, Key.NumpadPeriod,
            Key.Backspace,
        };

        /// <summary>
        /// Resolves the character produced by a key using a US layout.
        /// </summary>
        /// <param name="key">Key that is currently down.</param>
        /// <param name="shift">Whether a shift modifier is held.</param>
        /// <param name="capsLock">Whether caps lock is considered active.</param>
        /// <param name="character">Resolved character, or <c>'\0'</c> when the key produces no text.</param>
        /// <returns>True when the key maps to a character.</returns>
        public static bool TryGetCharacter(Key key, bool shift, bool capsLock, out char character)
        {
            bool upper = shift ^ capsLock;

            switch (key)
            {
                case Key.A: character = upper ? 'A' : 'a'; return true;
                case Key.B: character = upper ? 'B' : 'b'; return true;
                case Key.C: character = upper ? 'C' : 'c'; return true;
                case Key.D: character = upper ? 'D' : 'd'; return true;
                case Key.E: character = upper ? 'E' : 'e'; return true;
                case Key.F: character = upper ? 'F' : 'f'; return true;
                case Key.G: character = upper ? 'G' : 'g'; return true;
                case Key.H: character = upper ? 'H' : 'h'; return true;
                case Key.I: character = upper ? 'I' : 'i'; return true;
                case Key.J: character = upper ? 'J' : 'j'; return true;
                case Key.K: character = upper ? 'K' : 'k'; return true;
                case Key.L: character = upper ? 'L' : 'l'; return true;
                case Key.M: character = upper ? 'M' : 'm'; return true;
                case Key.N: character = upper ? 'N' : 'n'; return true;
                case Key.O: character = upper ? 'O' : 'o'; return true;
                case Key.P: character = upper ? 'P' : 'p'; return true;
                case Key.Q: character = upper ? 'Q' : 'q'; return true;
                case Key.R: character = upper ? 'R' : 'r'; return true;
                case Key.S: character = upper ? 'S' : 's'; return true;
                case Key.T: character = upper ? 'T' : 't'; return true;
                case Key.U: character = upper ? 'U' : 'u'; return true;
                case Key.V: character = upper ? 'V' : 'v'; return true;
                case Key.W: character = upper ? 'W' : 'w'; return true;
                case Key.X: character = upper ? 'X' : 'x'; return true;
                case Key.Y: character = upper ? 'Y' : 'y'; return true;
                case Key.Z: character = upper ? 'Z' : 'z'; return true;

                case Key.Digit1: character = shift ? '!' : '1'; return true;
                case Key.Digit2: character = shift ? '@' : '2'; return true;
                case Key.Digit3: character = shift ? '#' : '3'; return true;
                case Key.Digit4: character = shift ? '$' : '4'; return true;
                case Key.Digit5: character = shift ? '%' : '5'; return true;
                case Key.Digit6: character = shift ? '^' : '6'; return true;
                case Key.Digit7: character = shift ? '&' : '7'; return true;
                case Key.Digit8: character = shift ? '*' : '8'; return true;
                case Key.Digit9: character = shift ? '(' : '9'; return true;
                case Key.Digit0: character = shift ? ')' : '0'; return true;

                case Key.Space: character = ' '; return true;
                case Key.Minus: character = shift ? '_' : '-'; return true;
                case Key.Equals: character = shift ? '+' : '='; return true;
                case Key.LeftBracket: character = shift ? '{' : '['; return true;
                case Key.RightBracket: character = shift ? '}' : ']'; return true;
                case Key.Backslash: character = shift ? '|' : '\\'; return true;
                case Key.Semicolon: character = shift ? ':' : ';'; return true;
                case Key.Quote: character = shift ? '"' : '\''; return true;
                case Key.Backquote: character = shift ? '~' : '`'; return true;
                case Key.Comma: character = shift ? '<' : ','; return true;
                case Key.Period: character = shift ? '>' : '.'; return true;
                case Key.Slash: character = shift ? '?' : '/'; return true;

                case Key.Numpad0: character = '0'; return true;
                case Key.Numpad1: character = '1'; return true;
                case Key.Numpad2: character = '2'; return true;
                case Key.Numpad3: character = '3'; return true;
                case Key.Numpad4: character = '4'; return true;
                case Key.Numpad5: character = '5'; return true;
                case Key.Numpad6: character = '6'; return true;
                case Key.Numpad7: character = '7'; return true;
                case Key.Numpad8: character = '8'; return true;
                case Key.Numpad9: character = '9'; return true;
                case Key.NumpadDivide: character = '/'; return true;
                case Key.NumpadMultiply: character = '*'; return true;
                case Key.NumpadMinus: character = '-'; return true;
                case Key.NumpadPlus: character = '+'; return true;
                case Key.NumpadPeriod: character = '.'; return true;

                case Key.Backspace: character = '\b'; return true;

                default: character = '\0'; return false;
            }
        }
    }
}
