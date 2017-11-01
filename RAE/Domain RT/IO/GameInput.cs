using System.Linq;
using System.Collections.Generic;

namespace RAE.Game.IO
{
    public class VerbInput : ConsoleChoiceInput
    {
        public RAEGame Game { get; }
        public override bool IsTerminal { get; } = false;

        public VerbInput(RAEGame game)
        {
            Game = game;
        }

        public override IEnumerable<string> Input(string prompt)
        {
            return HandleInput(prompt, Game.Verbs.Keys.Concat(Game.VerbShortcuts.Values).Distinct());
        }

        protected override ConsoleAdvancedInput CreateNextInput()
        {
            return new ConsoleAlphaInput();
        }
    }
}
