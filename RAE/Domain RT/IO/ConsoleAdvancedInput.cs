using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RAE.Game.IO
{
    public abstract class ConsoleAdvancedInput
    {
        protected StringBuilder CurrentInput { get; } = new StringBuilder();
        protected ConsoleAdvancedInput PreviousInput { get; private set; }

        public string Color { get; set; } = "gray";

        public abstract IEnumerable<string> Input(string prompt);
        public abstract bool IsTerminal { get; }
        protected abstract ConsoleAdvancedInput CreateNextInput();

        protected static readonly string[] BackspaceResult = new string[0];

        protected virtual void PrintPrompt(string prompt)
        {
            GameConsole.Print(prompt);
        }

        protected IEnumerable<string> HandleNext()
        {
            if (IsTerminal)
                return null;

            var input = CreateNextInput();
            if (input == null)
                return null;

            GameConsole.PrintChar(' ');
            input.PreviousInput = this;
            return input.Input("");
        }
        
        protected IEnumerable<string> CurrentInputList
        {
            get
            {
                var thisList = new string[] { CurrentInput.ToString() };

                if (PreviousInput != null)
                    return PreviousInput.CurrentInputList.Concat(thisList);
                else
                    return thisList;
            }
        }

        protected void Backspace()
        {
            CurrentInput.Remove(CurrentInput.Length - 1, 1);
            GameConsole.Print("\b \b");
        }
    }

    public abstract class ConsoleChoiceInput : ConsoleAdvancedInput
    {
        protected IEnumerable<string> HandleInput(string prompt, IEnumerable<string> choices)
        {
            PrintPrompt(prompt);

            //choices = choices.OrderBy(a => a.ToLower()).ToArray();
            GameConsole.WindowInput();
            GameConsole.Print(GameConsole.CC(Color));
            if (CurrentInput.Length > 0)
            {
                GameConsole.Print(CurrentInput.ToString());
            }

            ConsoleKeyInfo k = new ConsoleKeyInfo();
            while (k.Key != ConsoleKey.Enter)
            {
                k = GameConsole.InputKey();
                switch (k.Key)
                {
                    case ConsoleKey.Enter:
                        if (!choices.Contains(CurrentInput.ToString()))
                        {
                            // tell user they haven't filled something valid yet and force failure
                            Error(CurrentInput.ToString());
                            k = new ConsoleKeyInfo();
                            break;
                        }
                        GameConsole.Window();
                        GameConsole.PrintChar('\n');
                        break;
                    case ConsoleKey.Backspace:
                        // back up through characters until more choices come up
                        if (CurrentInput.Length == 0)
                        {
                            // in this case, back up to the previous input if there is one
                            if (PreviousInput != null)
                            {
                                GameConsole.Window();
                                GameConsole.Print("\b \b");
                                return BackspaceResult;
                            }
                            break;
                        }

                        int num = GetNumFiltered(choices, CurrentInput.ToString());
                        do
                        {
                            Backspace();
                        } while (num == GetNumFiltered(choices, CurrentInput.ToString()));
                        break;
                    default:
                        // fill in a key at pos
                        CurrentInput.Append(k.KeyChar);
                        // get possible choices
                        string inputi = CurrentInput.ToString();
                        IEnumerable<string> matches = choices.Where(a => a.StartsWith(inputi, StringComparison.InvariantCultureIgnoreCase));
                        // invalid key
                        if (matches.Count() == 0)
                        {
                            Error(inputi);
                            Backspace();
                            break;
                        }

                        // handle wrong case
                        CurrentInput.Remove(CurrentInput.Length - 1, 1);
                        string added = matches.First().Substring(CurrentInput.Length, 1);
                        CurrentInput.Append(added);
                        GameConsole.Print(added);

                        if (matches.Count() == 1)
                        {
                            // done
                            CurrentInput.Remove(0, CurrentInput.Length);
                            CurrentInput.Append(matches.First());
                            GameConsole.XY(0, 0);
                            GameConsole.Print(CurrentInput.ToString());

                            // if non-terminal, we branch to the next one
                            var result = HandleNext();
                            if(result == BackspaceResult)
                            {
                                // next backspaced back into here
                                int bnum = GetNumFiltered(choices, CurrentInput.ToString());
                                do
                                {
                                    Backspace();
                                } while (bnum == GetNumFiltered(choices, CurrentInput.ToString()));
                            }
                            else if (result != null)
                            {
                                GameConsole.Window();
                                GameConsole.PrintChar('\n');
                                return result;
                            }
                            break;
                        }

                        // otherwise, fill chars until we get to unique fork
                        int pos = CurrentInput.Length;
                        do
                        {
                            added = matches.First().Substring(pos++, 1);
                            CurrentInput.Append(added);
                            GameConsole.Print(added);
                        } while (matches.All(a => a.StartsWith(CurrentInput.ToString(), StringComparison.InvariantCultureIgnoreCase)));
                        Backspace();
                        break;
                }
            }
            return CurrentInputList;
        }

        protected void Error(string input)
        {
            GameConsole.XY(0, 0);
            GameConsole.Print(GameConsole.CC("red") + input + "?" + GameConsole.CC(Color));
            GameConsole.Wait(250);
            GameConsole.XY(0, 0);
            GameConsole.Print(input + " ");
            GameConsole.XY(input.Length, 0);
        }

        private int GetNumFiltered(IEnumerable<string> choices, string input)
        {
            return choices.Where(a => a.StartsWith(input, StringComparison.InvariantCultureIgnoreCase)).Count();
        }
    }

    public class GenericConsoleChoiceInput : ConsoleChoiceInput
    {
        public IEnumerable<string> Choices { get; }

        public GenericConsoleChoiceInput(IEnumerable<string> choices)
        {
            Choices = choices;
        }

        public override bool IsTerminal { get; } = true;

        public override IEnumerable<string> Input(string prompt)
        {
            return HandleInput(prompt, Choices);
        }

        protected override ConsoleAdvancedInput CreateNextInput()
        {
            throw new NotImplementedException();
        }
    }
    
    public class ConsoleAlphaInput : ConsoleAdvancedInput
    {
        public override bool IsTerminal { get; } = true;

        public override IEnumerable<string> Input(string prompt)
        {
            PrintPrompt(prompt);

            //choices = choices.OrderBy(a => a.ToLower()).ToArray();
            GameConsole.WindowInput();
            GameConsole.Print(GameConsole.CC(Color));
            if (CurrentInput.Length > 0)
            {
                GameConsole.Print(CurrentInput.ToString());
            }

            ConsoleKeyInfo k = new ConsoleKeyInfo();
            while (k.Key != ConsoleKey.Enter)
            {
                k = GameConsole.InputKey();
                switch (k.Key)
                {
                    case ConsoleKey.Enter:
                        GameConsole.Window();
                        GameConsole.PrintChar('\n');
                        break;
                    case ConsoleKey.Backspace:
                        // back up through characters until more choices come up
                        if (CurrentInput.Length == 0)
                        {
                            // in this case, back up to the previous input if there is one
                            if (PreviousInput != null)
                            {
                                GameConsole.Window();
                                GameConsole.Print("\b \b");
                                return BackspaceResult;
                            }
                        }
                        else
                        {
                            Backspace();
                        }
                        break;
                    default:
                        // fill in a key at pos
                        CurrentInput.Append(k.KeyChar);
                        GameConsole.PrintChar(k.KeyChar);
                        break;
                }
            }
            return CurrentInputList;
        }

        protected override ConsoleAdvancedInput CreateNextInput()
        {
            throw new NotImplementedException();
        }
    }
}
