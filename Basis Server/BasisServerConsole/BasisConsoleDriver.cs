using System.Text;

namespace BasisNetworkConsole
{
    /// <summary>
    /// Keeps the command being typed intact while the server logs from its own threads.
    /// Console output is funnelled through here, so a log line erases the input line, prints
    /// above it, then the input line is redrawn underneath with the caret back where it was.
    /// Without this a log arriving mid-keystroke lands in the middle of the typed text and the
    /// terminal's own echo is left in pieces.
    /// Redirected stdin/stdout (docker -d, pipes, service hosts) keeps plain Console behaviour.
    /// Positioning uses the Console cursor API on Windows and relative ANSI moves elsewhere,
    /// because a Unix cursor query is answered through stdin and the reader thread is parked
    /// on stdin inside ReadKey.
    /// </summary>
    public static class BasisConsoleDriver
    {
        private const string Prompt = "> ";
        private const ConsoleColor PromptColor = ConsoleColor.DarkGreen;
        private const int HistoryLimit = 100;
        private const int FallbackWidth = 80;

        private static readonly object Gate = new object();
        private static readonly StringBuilder Line = new StringBuilder();
        private static readonly List<string> History = new List<string>();

        [ThreadStatic] private static List<(string Text, ConsoleColor Color)>? Pending;

        private static TextWriter Raw = Console.Out;
        private static bool UseAnsi = !OperatingSystem.IsWindows();

        private static bool Installed;
        private static bool InputActive;
        private static bool LineShown;
        private static int Caret;
        private static int HistoryCursor;
        private static string HistoryDraft = string.Empty;

        /// <summary>False when the console is redirected, which leaves every path here a plain Console passthrough.</summary>
        public static bool Interactive { get; private set; }

        /// <summary>
        /// Takes over stdout. Call once the interactive console is wanted, and after any plain
        /// Console prompting (the first boot wizard) has finished.
        /// </summary>
        public static void Initialize()
        {
            if (Installed) return;

            Interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
            if (!Interactive) return;

            Raw = Console.Out;
            Console.SetOut(new InterceptingWriter());
            Installed = true;
        }

        /// <summary>
        /// Reads one command, redrawing the input line whenever the server logs underneath it.
        /// Returns null at end of input.
        /// </summary>
        public static string? ReadLine()
        {
            if (!Interactive) return Console.ReadLine();

            lock (Gate)
            {
                InputActive = true;
                HistoryCursor = History.Count;
                Draw();
            }

            while (true)
            {
                ConsoleKeyInfo key;
                try
                {
                    key = Console.ReadKey(true);
                }
                catch (InvalidOperationException)
                {
                    lock (Gate)
                    {
                        Erase();
                        InputActive = false;
                        Interactive = false;
                    }
                    return Console.ReadLine();
                }

                lock (Gate)
                {
                    if (Handle(key, out string? entered)) return entered;
                }
            }
        }

        /// <summary>Clears the screen and puts the input line back.</summary>
        public static void Clear()
        {
            if (!Interactive)
            {
                BNL.ClearConsole();
                return;
            }

            lock (Gate)
            {
                LineShown = false;
            }

            try
            {
                Console.Clear();
            }
            catch (IOException)
            {
            }

            lock (Gate)
            {
                LineShown = false;
                Draw();
            }
        }

        private static bool Handle(ConsoleKeyInfo key, out string? entered)
        {
            entered = null;

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    MoveCaret(Line.Length);
                    Raw.Write(Environment.NewLine);
                    LineShown = false;
                    InputActive = false;
                    entered = Line.ToString();
                    Remember(entered);
                    Line.Clear();
                    Caret = 0;
                    return true;

                case ConsoleKey.Backspace:
                    if (Caret > 0)
                    {
                        Erase();
                        Line.Remove(--Caret, 1);
                        Draw();
                    }
                    return false;

                case ConsoleKey.Delete:
                    if (Caret < Line.Length)
                    {
                        Erase();
                        Line.Remove(Caret, 1);
                        Draw();
                    }
                    return false;

                case ConsoleKey.LeftArrow:
                    if (Caret > 0) MoveCaret(Caret - 1);
                    return false;

                case ConsoleKey.RightArrow:
                    if (Caret < Line.Length) MoveCaret(Caret + 1);
                    return false;

                case ConsoleKey.Home:
                    MoveCaret(0);
                    return false;

                case ConsoleKey.End:
                    MoveCaret(Line.Length);
                    return false;

                case ConsoleKey.UpArrow:
                    Recall(-1);
                    return false;

                case ConsoleKey.DownArrow:
                    Recall(1);
                    return false;

                case ConsoleKey.Escape:
                    if (Line.Length != 0)
                    {
                        Erase();
                        Line.Clear();
                        Caret = 0;
                        Draw();
                    }
                    return false;

                default:
                    if (char.IsControl(key.KeyChar)) return false;

                    if (Caret == Line.Length)
                    {
                        Line.Append(key.KeyChar);
                        Caret++;
                        Raw.Write(key.KeyChar);
                        SettleWrap(Prompt.Length + Caret);
                    }
                    else
                    {
                        Erase();
                        Line.Insert(Caret++, key.KeyChar);
                        Draw();
                    }
                    return false;
            }
        }

        private static void Recall(int direction)
        {
            int target = HistoryCursor + direction;
            if (target < 0 || target > History.Count) return;

            if (HistoryCursor == History.Count) HistoryDraft = Line.ToString();

            Erase();
            Line.Clear();
            Line.Append(target == History.Count ? HistoryDraft : History[target]);
            Caret = Line.Length;
            HistoryCursor = target;
            Draw();
        }

        private static void Remember(string line)
        {
            if (line.Length != 0 && (History.Count == 0 || History[History.Count - 1] != line))
            {
                History.Add(line);
                if (History.Count > HistoryLimit) History.RemoveAt(0);
            }

            HistoryCursor = History.Count;
            HistoryDraft = string.Empty;
        }

        private static void Draw()
        {
            if (!InputActive || LineShown) return;

            WriteColored(Prompt, PromptColor);
            if (Line.Length != 0) Raw.Write(Line.ToString());

            int painted = Prompt.Length + Line.Length;
            SettleWrap(painted);
            LineShown = true;
            MoveBetween(painted, Prompt.Length + Caret);
        }

        private static void Erase()
        {
            if (!LineShown) return;

            int painted = Prompt.Length + Line.Length;
            MoveBetween(Prompt.Length + Caret, 0);

            if (UseAnsi)
            {
                Raw.Write("\u001b[J");
            }
            else
            {
                Raw.Write(new string(' ', painted));
                MoveBetween(painted, 0);
            }

            LineShown = false;
        }

        private static void MoveCaret(int target)
        {
            if (LineShown) MoveBetween(Prompt.Length + Caret, Prompt.Length + target);
            Caret = target;
        }

        /// <summary>
        /// Walks the cursor between two offsets, both counted in characters from the first cell of
        /// the prompt so that wrapping falls out of the arithmetic. The Windows path reads the
        /// position back rather than tracking it, so a scroll at the bottom of the buffer cannot
        /// desync it.
        /// </summary>
        private static void MoveBetween(int from, int to)
        {
            if (from == to) return;
            int width = Width();

            if (UseAnsi)
            {
                int rows = from / width - to / width;
                if (rows > 0) Raw.Write($"\u001b[{rows}A");
                else if (rows < 0) Raw.Write($"\u001b[{-rows}B");

                Raw.Write('\r');
                int column = to % width;
                if (column > 0) Raw.Write($"\u001b[{column}C");
            }
            else
            {
                int absolute = Console.CursorTop * width + Console.CursorLeft + (to - from);
                if (absolute < 0) absolute = 0;
                SetCursor(absolute % width, absolute / width);
            }
        }

        /// <summary>
        /// Terminals hold the wrap until the next character is written, so text ending exactly at
        /// the margin leaves the cursor ambiguous and every later relative move a row out. Windows
        /// wraps immediately and reads the cursor back anyway, so it needs none of this.
        /// </summary>
        private static void SettleWrap(int offset)
        {
            if (!UseAnsi || offset == 0 || offset % Width() != 0) return;
            Raw.Write(" \r");
        }

        private static void WriteColored(string text, ConsoleColor color)
        {
            if (text.Length == 0) return;

            ConsoleColor previous = Foreground();
            SetForeground(color);
            Raw.Write(text);
            SetForeground(previous);
        }

        private static int Width()
        {
            try
            {
                int width = Console.BufferWidth;
                return width > 1 ? width : FallbackWidth;
            }
            catch (IOException)
            {
                return FallbackWidth;
            }
            catch (ArgumentOutOfRangeException)
            {
                return FallbackWidth;
            }
        }

        private static void SetCursor(int left, int top)
        {
            try
            {
                Console.SetCursorPosition(left, top);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (IOException)
            {
            }
        }

        private static ConsoleColor Foreground()
        {
            try
            {
                return Console.ForegroundColor;
            }
            catch (IOException)
            {
                return ConsoleColor.Gray;
            }
        }

        private static void SetForeground(ConsoleColor color)
        {
            try
            {
                Console.ForegroundColor = color;
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Buffers what a thread writes and hands each finished line to the drawing code, so a line
        /// assembled from several Console.Write calls is still drawn as one piece.
        /// </summary>
        private static void Append(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;

            List<(string Text, ConsoleColor Color)> pending = Pending ??= new List<(string, ConsoleColor)>();
            ConsoleColor color = Foreground();
            int start = 0;

            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] != '\n') continue;

                pending.Add((text.Substring(start, index - start + 1), color));
                start = index + 1;
                Commit(pending);
            }

            if (start < text.Length) pending.Add((text.Substring(start), color));
        }

        private static void Commit(List<(string Text, ConsoleColor Color)> pending)
        {
            lock (Gate)
            {
                Erase();
                foreach ((string text, ConsoleColor color) in pending) WriteColored(text, color);
                pending.Clear();
                Draw();
            }
        }

        /// <summary>
        /// Stands in for stdout. Drawing writes to <see cref="Raw"/> rather than Console.Out, which
        /// would come straight back through here.
        /// </summary>
        private sealed class InterceptingWriter : TextWriter
        {
            public override Encoding Encoding => Raw.Encoding;

            public override void Write(char value) => Append(value.ToString());

            public override void Write(string? value) => Append(value);

            public override void Write(char[] buffer, int index, int count) => Append(new string(buffer, index, count));

            public override void Write(ReadOnlySpan<char> buffer) => Append(new string(buffer));

            public override void Flush()
            {
                List<(string Text, ConsoleColor Color)>? pending = Pending;
                if (pending != null && pending.Count != 0) Commit(pending);
                Raw.Flush();
            }
        }
    }
}
