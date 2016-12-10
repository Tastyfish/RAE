using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace RAE.Game
{
    internal static class GameConsole
    {
        private const long STD_OUTPUT_HANDLE = -11;
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(long nStdHandle);
        private const long ENABLE_PROCESSED_OUTPUT = 1;
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, long dwMode);

        public static void Init()
        {
            // init console
            Console.SetWindowPosition(0, 0);
            Console.SetWindowSize(1, 1);

            Console.SetBufferSize(80, 25);
            Console.SetWindowPosition(0, 0);
            Console.SetWindowSize(80, 25);
            // disable automatic scrolling
            SetConsoleMode(GetStdHandle(STD_OUTPUT_HANDLE), ENABLE_PROCESSED_OUTPUT);
            winStack.Push(new WindowRect()
            {
                Left = 0,
                Top = 0,
                Width = Console.WindowWidth,
                Height = Console.WindowHeight,
                CX = 0,
                CY = 0
            });
        }

        private static void InternalInputSetPos(int scroll, int pos)
        {
            XY(pos - scroll, 0);
        }

        private static void InternalRefreshInput(int scroll, int pos, string s)
        {
            int w = winStack.Peek().Width;

            Clear();
            XY(0, 0);
            Print(s.Substring(scroll, Math.Min(w, s.Length - scroll)));
            InternalInputSetPos(scroll, pos);
        }

        private static List<string> inputHistory = new List<string>();

        public static string InputLine(string prompt)
        {
            Print("\n" + prompt);

            WindowRect r = winStack.Peek();
            Window(r.Left + CursorX, r.Top + CursorY,
                r.Width - CursorX, 1);
            r = winStack.Peek();

            StringBuilder input = new StringBuilder();
            int pos = 0, scroll = 0;
            int historyPos = inputHistory.Count;

            ConsoleKeyInfo k = new ConsoleKeyInfo();
            while (k.Key != ConsoleKey.Enter)
            {
                k = InputKey();
                switch (k.Key)
                {
                    case ConsoleKey.LeftArrow:
                        if (pos <= 0)
                            break;

                        if (--pos < scroll)
                        {
                            scroll = pos;
                            InternalRefreshInput(scroll, pos, input.ToString());
                        }
                        else
                        {
                            InternalInputSetPos(scroll, pos);
                        }
                        break;
                    case ConsoleKey.RightArrow:
                        if (pos >= input.Length)
                            break;

                        if (++pos >= scroll + r.Width)
                        {
                            scroll = pos - r.Width + 1;
                            InternalRefreshInput(scroll, pos, input.ToString());
                        }
                        else
                        {
                            InternalInputSetPos(scroll, pos);
                        }
                        break;
                    case ConsoleKey.UpArrow:
                        if (historyPos <= 0)
                            break;

                        historyPos--;

                        // replace text and refresh
                        input.Clear();
                        input.Append(inputHistory[historyPos]);
                        pos = input.ToString().Length;
                        scroll = Math.Max(0, pos - r.Width + 1);
                        InternalRefreshInput(scroll, pos, input.ToString());
                        break;
                    case ConsoleKey.DownArrow:
                        if (historyPos >= inputHistory.Count - 1)
                            break;

                        historyPos++;

                        // replace text and refresh
                        input.Clear();
                        input.Append(inputHistory[historyPos]);
                        pos = input.ToString().Length;
                        scroll = Math.Max(0, pos - r.Width + 1);
                        InternalRefreshInput(scroll, pos, input.ToString());
                        break;
                    case ConsoleKey.Home:
                        pos = 0;

                        if (scroll != 0)
                        {
                            scroll = 0;
                            InternalRefreshInput(scroll, pos, input.ToString());
                        }
                        else
                        {
                            InternalInputSetPos(scroll, pos);
                        }
                        break;
                    case ConsoleKey.End:
                        pos = input.Length;

                        int newScroll = Math.Max(0, pos - r.Width + 1);

                        if (scroll != newScroll)
                        {
                            scroll = newScroll;
                            InternalRefreshInput(scroll, pos, input.ToString());
                        }
                        else
                        {
                            InternalInputSetPos(scroll, pos);
                        }
                        break;
                    case ConsoleKey.Backspace:
                        // back up through characters until more choices come up
                        if (pos == 0)
                            break;

                        input.Remove(--pos, 1);

                        if (pos < scroll)
                        {
                            scroll = pos;
                        }
                        else if (pos == input.Length)
                        {
                            // the special case of backspacing without scrolling
                            // or anything, remove the one new char to reduce flicker
                            InternalInputSetPos(scroll, pos);
                            Print(" \b");
                            break;
                        }

                        InternalRefreshInput(scroll, pos, input.ToString());
                        break;
                    case ConsoleKey.Enter:
                        inputHistory.Add(input.ToString());
                        Window();
                        PrintChar('\n');
                        break;
                    default:
                        if (k.KeyChar == '\0')
                            break;

                        // fill in a key at pos
                        input.Insert(pos++, k.KeyChar);

                        if (pos >= scroll + r.Width)
                        {
                            scroll = pos - r.Width + 1;
                        }
                        else if (pos == input.Length)
                        {
                            // the special case of typing without and scrolling
                            // or anything, draw the one new char to reduce flicker
                            PrintChar(k.KeyChar);
                            break;
                        }

                        InternalRefreshInput(scroll, pos, input.ToString());

                        break;
                }
            }
            return input.ToString();
        }

        public static string XXInputChoices(string prompt, string[] choices)
        {
            Print(prompt);

            choices = choices.OrderBy(a => a.ToLower()).ToArray();
            StringBuilder input = new StringBuilder();
            WindowRect r = winStack.Peek();
            Window(r.Left + CursorX, r.Top + CursorY,
                r.Width - CursorX, 1);

            ConsoleKeyInfo k = new ConsoleKeyInfo();
            while (k.Key != ConsoleKey.Enter)
            {
                k = InputKey();
                switch (k.Key)
                {
                    case ConsoleKey.Enter:
                        if (!choices.Contains(input.ToString()))
                        {
                            // tell user they haven't filled something valid yet and force failure
                            XXInternalInputChoicesError(input.ToString());
                            k = new ConsoleKeyInfo();
                            break;
                        }
                        Window();
                        PrintChar('\n');
                        break;
                    case ConsoleKey.Backspace:
                        // back up through characters until more choices come up
                        if (input.Length == 0)
                            break;

                        int num = XXInternalInputChoicesGetNumFiltered(choices, input.ToString());
                        do
                        {
                            input.Remove(input.Length - 1, 1);
                            Print("\b \b");
                        } while (num == XXInternalInputChoicesGetNumFiltered(choices, input.ToString()));
                        break;
                    default:
                        // fill in a key at pos
                        input.Append(k.KeyChar);
                        // get possible choices
                        string inputi = input.ToString();
                        IEnumerable<string> matches = choices.Where(a => a.StartsWith(inputi, StringComparison.InvariantCultureIgnoreCase));
                        // invalid key
                        if (matches.Count() == 0)
                        {
                            XXInternalInputChoicesError(inputi);
                            input.Remove(input.Length - 1, 1);
                            Print("\b \b");
                            break;
                        }

                        // handle wrong case
                        input.Remove(input.Length - 1, 1);
                        string added = matches.First().Substring(input.Length, 1);
                        input.Append(added);
                        Print(added);

                        if (matches.Count() == 1)
                        {
                            // done
                            input.Remove(0, input.Length);
                            input.Append(matches.First());
                            XY(0, 0);
                            Print(input.ToString());
                            break;
                        }

                        // otherwise, fill chars until we get to unique fork
                        int pos = input.Length;
                        do
                        {
                            added = matches.First().Substring(pos++, 1);
                            input.Append(added);
                            Print(added);
                        } while (matches.All(a => a.StartsWith(input.ToString(), StringComparison.InvariantCultureIgnoreCase)));
                        Print("\b \b");
                        input.Remove(input.Length - 1, 1);
                        break;
                }
            }
            return input.ToString();
        }

        private static int XXInternalInputChoicesGetNumFiltered(string[] choices, string input)
        {
            return choices.Where(a => a.StartsWith(input, StringComparison.InvariantCultureIgnoreCase)).Count();
        }

        private static void XXInternalInputChoicesError(string input)
        {
            XY(0, 0);
            Print(CC("red") + input + "?" + CC("gray"));
            Wait(250);
            XY(0, 0);
            Print(input + " ");
            InternalInputSetPos(0, input.Length);
        }

        public static ConsoleKeyInfo InputKey()
        {
            return Console.ReadKey(true);
        }

        public static void PrintChar(char c)
        {
            WindowRect w = winStack.Peek();

            switch (c)
            {
                case '\t':
                    int destX = CursorX + 8 - (CursorX % 8);
                    if (destX >= w.Width)
                    {
                        PrintChar('\n');
                    }
                    else
                    {
                        while (CursorX < destX)
                            PrintChar(' ');
                    }
                    break;
                case '\n':
                    CursorX = 0;
                    if (CursorY + 1 >= w.Height)
                    {
                        // scroll
                        Console.MoveBufferArea(w.Left, w.Top + 1, w.Width, w.Height - 1,
                            w.Left, w.Top,
                            ' ', ConsoleColor.Gray, ConsoleColor.Black);
                    }
                    else
                    {
                        CursorY++;
                    }
                    break;
                case '\b':
                    CursorX--;
                    if (CursorX < 0)
                    {
                        CursorX = w.Width - 1;
                        CursorY--;
                        if (CursorY < 0)
                        {
                            CursorX = 0;
                            CursorY = 0;
                        }
                    }
                    Console.Write(" \b");
                    break;
                default:
                    if (CursorX == w.Width - 1)
                    {
                        int cx = CursorX;
                        int cy = CursorY;
                        Console.Write(c);
                        XY(cx, cy);
                        PrintChar('\n');
                    }
                    else
                    {
                        Console.Write(c);
                    }
                    break;
            }
        }

        public static void Print(string text)
        {
            Console.ForegroundColor = ConsoleColor.Gray;

            while (true)
            {
                int idx = text.IndexOf('\x1B');

                if (idx >= 0)
                {
                    for (int i = 0; i < idx; i++)
                    {
                        PrintChar(text[i]);
                    }

                    ConsoleColor c = (ConsoleColor)int.Parse(text.Substring(idx + 1, 1), System.Globalization.NumberStyles.HexNumber);
                    Console.ForegroundColor = c;
                    text = text.Substring(idx + 2);
                }
                else
                {
                    for (int i = 0; i < text.Length; i++)
                    {
                        PrintChar(text[i]);
                    }
                    return;
                }
            }
        }

        public static void TypeOut(string text, int delay)
        {
            Console.ForegroundColor = ConsoleColor.Gray;

            while (true)
            {
                int idx = text.IndexOf('\x1B');

                if (idx >= 0)
                {
                    foreach (char ch in text.Substring(0, idx).ToCharArray())
                    {
                        PrintChar(ch);
                        Wait(delay);
                    }

                    ConsoleColor c = (ConsoleColor)int.Parse(text.Substring(idx + 1, 1), System.Globalization.NumberStyles.HexNumber);
                    Console.ForegroundColor = c;
                    text = text.Substring(idx + 2);
                }
                else
                {
                    foreach (char ch in text.ToCharArray())
                    {
                        PrintChar(ch);
                        Wait(delay);
                    }
                    return;
                }
            }
        }

        public static string CC(string color)
        {
            ConsoleColor cc;

            if (!Enum.TryParse(color, true, out cc))
                cc = ConsoleColor.Gray;

            return "\x1B" + ((int)cc).ToString("X0");
        }

        public static int CursorX
        {
            get
            {
                return Console.CursorLeft - winStack.Peek().Left;
            }
            set
            {
                WindowRect r = winStack.Peek();

                Console.CursorLeft = r.Left
                    + Math.Min(Math.Max(0, value), r.Width - 1);
            }
        }

        public static int CursorY
        {
            get
            {
                return Console.CursorTop - winStack.Peek().Top;
            }
            set
            {
                WindowRect r = winStack.Peek();

                Console.CursorTop = r.Top
                    + Math.Min(Math.Max(0, value), r.Height - 1);
            }
        }

        public static void XY(int x, int y)
        {
            CursorX = x;
            CursorY = y;
        }

        public static void Clear()
        {
            WindowRect r = winStack.Peek();
            string lineChunk = new string(' ', r.Width);

            for (int y = 0; y < r.Height; y++)
            {
                CursorX = 0;
                CursorY = y;
                Print(lineChunk);
            }

            CursorX = 0;
            CursorY = 0;
        }

        private struct WindowRect
        {
            public int Left, Top, Width, Height;
            public int CX, CY;
        }

        private static Stack<WindowRect> winStack = new Stack<WindowRect>();

        public static void Window(int x, int y, int w, int h)
        {
            if (x < 0)
                x = Console.BufferWidth + x;
            if (y < 0)
                y = Console.BufferHeight + y;
            if (w <= 0)
                w = Console.BufferWidth + w;
            if (h <= 0)
                h = Console.BufferHeight + h;

            if (x < 0 || x > Console.BufferWidth - 1)
                throw new ArgumentException("Must be within window.", "x");
            if (y < 0 || y > Console.BufferHeight - 1)
                throw new ArgumentException("Must be within window.", "y");
            if (w <= 0 || (x + w) > Console.BufferWidth)
                throw new ArgumentException("Must be within window.", "w");
            if (h <= 0 || (y + h) > Console.BufferHeight)
                throw new ArgumentException("Must be within window.", "h");

            winStack.Push(new WindowRect()
            {
                Left = x,
                Top = y,
                Width = w,
                Height = h,
                CX = CursorX,
                CY = CursorY
            });

            CursorX = CursorX;
            CursorY = CursorY;
        }

        public static void Window()
        {
            if (winStack.Count == 1)
                throw new InvalidOperationException("No more windows to pop.");

            winStack.Pop();

            CursorX = CursorX;
            CursorY = CursorY;
        }

        public static void WindowInput()
        {
            WindowRect r = winStack.Peek();
            Window(r.Left + CursorX, r.Top + CursorY,
                r.Width - CursorX, 1);
        }

        public static void FullClear()
        {
            while (winStack.Count > 1)
                Window();

            Clear();
        }

        public static int ConsoleWidth
        {
            get { return winStack.Peek().Width; }
        }
        public static int ConsoleHeight
        {
            get { return winStack.Peek().Height; }
        }

        public static void Wait(int ms)
        {
            Thread.Sleep(ms);
        }
    }
}
