using System;
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

        private static readonly string[] extraVerbs = new string[] { "quit", "save", "load", "restore", "new", "help" };

        public override IEnumerable<string> Input(string prompt)
        {
            var cardinals = Game.CurrentRoom.Spots.Where(kv => !kv.Value.Removed).Select(kv => kv.Key).Intersect(Room.CompassDirections);

            // add shortcuts
            cardinals = cardinals.Concat(cardinals.Select(c => Room.ShortCompassDirections[Array.IndexOf(Room.CompassDirections, c)]));

            return HandleInput(prompt, Game.Verbs.Keys.Concat(Game.VerbShortcuts.Keys).Distinct().Concat(extraVerbs).Concat(cardinals));
        }

        protected override ConsoleAdvancedInput CreateNextInput()
        {
            return new ObjectInput(Game);
        }
    }

    public class ObjectInput : ConsoleChoiceInput
    {
        public RAEGame Game { get; }
        public override bool IsTerminal => false;

        private static readonly string[] extraNouns =
            Enum.GetNames(typeof(Article)).Select(n => n.ToLower())
            .Concat(Enum.GetNames(typeof(RAEGame.HelperVerb)).Select(n => n.ToLower()))
            .Concat(new string[] { "" })
            .ToArray();

        public ObjectInput(RAEGame game)
        {
            Game = game;
        }

        public override IEnumerable<string> Input(string prompt)
        {
            return HandleInput(prompt, Game
                .GetAllTargets()
                .Concat(extraNouns)
                .Distinct())
                .TakeWhile(s => s.Length != 0);
        }

        protected override ConsoleAdvancedInput CreateNextInput()
        {
            return new ObjectInput(Game);
        }
    }
}
