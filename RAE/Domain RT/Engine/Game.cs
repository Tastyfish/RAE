using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.ComponentModel;
using RAE.Game.IO;

namespace RAE.Game
{
    public abstract class RAEGame
    {
        public delegate void GlobalVerb(Verbable target, string[] line, string[] fullLine);

        public Dictionary<string, Room> Rooms { get; private set; }
        public Dictionary<string, Item> Items { get; private set; }
        public Dictionary<string, NPC> NPCs { get; private set; }
        public Player Player { get; private set; }
        public Room CurrentRoom { get { return Player != null ? (Room)Player.Location : null; } }
        public Dictionary<string, GlobalVerb> Verbs { get; private set; }
        public Dictionary<string, string> VerbShortcuts { get; private set; }
        private static Random mRandomizer = new Random();
        private string mName;
        private List<int> mThenHashes = new List<int>();
        private Dictionary<Verbable, int> VTickers = new Dictionary<Verbable, int>();
        public string Name
        {
            get { return mName; }
            set
            {
                mName = value;
                UpdateTitle();
            }
        }

        public RAEGame()
        {
            mName = GetType().Name;
            UpdateTitle();
            GameConsole.Init();
        }

        public virtual void Init(Dictionary<string, GlobalVerb> verbs, Dictionary<string, string> verbShortcuts)
        {
            Verbs = verbs;
            VerbShortcuts = verbShortcuts;

            Rooms = Verbable.GetNestedTypeList<Room>(GetType(), this);
            Items = Verbable.GetNestedTypeList<Item>(GetType(), this);
            NPCs = Verbable.GetNestedTypeList<NPC>(GetType(), this);
            Player = new Player(this);
            RegisterInstance(Player);
        }

        public static string[] Input()
        {
            return Input("> ");
        }

        public static string[] Input(string prompt)
        {
            return InputLine(prompt).Split(new char[] { ' ' });
        }

        public static string InputLine()
        {
            return InputLine("> ");
        }

        public static string InputLine(string prompt)
        {
            return GameConsole.InputLine(prompt);
        }

        public static int InputInt()
        {
            return InputInt("#> ");
        }

        public static int InputInt(string prompt)
        {
            string[] s;
            int value;
            do
            {
                s = Input(prompt);
            } while (s.Length != 1 || !int.TryParse(s[0], out value));

            return value;
        }

        public static bool InputBool()
        {
            return InputBool("T/F> ");
        }

        public static bool InputBool(string prompt)
        {
            string result = InputChoices(prompt,
                new string[] { "true", "false", "yes", "no", "right", "wrong" });

            return result == "true" || result == "yes" || result == "right";
        }

        public static string InputChoices(string[] choices)
        {
            return InputChoices(".> ", choices);
        }

        public static string InputChoices(string prompt, string[] choices)
        {
            return new GenericConsoleChoiceInput(choices)
                .Input(prompt).First();
        }

        public static char Pause()
        {
            return Pause(true);
        }

        public static char Pause(bool showPrompt)
        {
            if (showPrompt)
                Print("\n...>");
            ConsoleKeyInfo ki = GameConsole.InputKey();
            PrintLine("");
            return ki.KeyChar;
        }

        public static void Print(string format, params string[] args)
        {
            Print(string.Format(format, args));
        }

        public static void Print(string text)
        {
            GameConsole.Print(text);
        }

        public static string Capitalize(string text)
        {
            int first = 0;
            while (first < text.Length && text.Skip(first).First() == '\x1B')
                first += 2;

            if (first >= text.Length)
                return text;

            return text.Substring(0, first)
                + text.Substring(first, 1).ToUpper()
                + text.Substring(first + 1);
        }

        public static void PrintLine(string format, params string[] args)
        {
            Print(Capitalize(format) + "\n", args);
        }

        public static void PrintLine(string text)
        {
            Print(Capitalize(text) + "\n");
        }

        public static void TypeOut(string text, int delay)
        {
            GameConsole.TypeOut(text, delay);
        }

        public static void TypeOutLine(string text, int delay)
        {
            TypeOut(Capitalize(text) + "\n", delay);
        }

        public static int Menu(string[] items)
        {
            return Menu(items, null);
        }

        public static int Menu(string[] items, string escapeItem)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null)
                    PrintLine(" " + Bold("*") + " " + items[i]);
            }
            if (escapeItem != null)
                PrintLine(" " + Bold("<-") + " " + escapeItem);

            // sanitize items of their formatting
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null)
                    continue;

                int idx = -1;
                while ((idx = items[i].IndexOf('\x1B')) >= 0)
                {
                    items[i] = items[i].Substring(0, idx) + items[i].Substring(idx + 2, items[i].Length - idx - 2);
                }
            }

            string choice;
            if (escapeItem == null)
                choice = InputChoices(items.Where(s => s != null).ToArray());
            else
                choice = InputChoices(items.Where(s => s != null).Union(new string[] { "", escapeItem }).ToArray());

            return Array.IndexOf(items, choice) + 1;
        }

        public static string CC(string color)
        {
            return GameConsole.CC(color);
        }

        public static string Colorize(string text, string color)
        {
            return CC(color) + text + CC("gray");
        }

        public static string Bold(string text)
        {
            return Colorize(text, "white");
        }

        public static int CursorX
        {
            get
            {
                return GameConsole.CursorX;
            }
            set
            {
                GameConsole.CursorX = value;
            }
        }

        public static int CursorY
        {
            get
            {
                return GameConsole.CursorY;
            }
            set
            {
                GameConsole.CursorY = value;
            }
        }

        public static void XY(int x, int y)
        {
            GameConsole.XY(x, y);
        }

        public static void Clear()
        {
            GameConsole.Clear();
        }

        public static void FullClear()
        {
            GameConsole.FullClear();
        }

        public static void Window(int x, int y, int w, int h)
        {
            GameConsole.Window(x, y, w, h);
        }

        public static void Window()
        {
            GameConsole.Window();
        }

        public static int ConsoleWidth
        {
            get { return GameConsole.ConsoleWidth; }
        }
        public static int ConsoleHeight
        {
            get { return GameConsole.ConsoleHeight; }
        }

        enum HelperVerb
        {
            For,
            To,
            With,
            Using,
            On,
            At,
            Into,
            Through
        }

        public string[] SanitizeLine(string[] line)
        {
            if (line.Length == 0)
                return line;

            List<string> newline = new List<string>();
            newline.Add(line[0]);
            string part = "";
            Article a; HelperVerb h;
            for (int i = 1; i < line.Length; i++)
            {
                string word = line[i].ToLower();

                // ignore articles
                if (!Enum.TryParse(word, true, out a) && !Enum.TryParse(word, true, out h))
                {
                    part += (part.Length > 0 ? " " : "")
                        + word.Trim(new char[] { ' ', '\t', '.', ',', ':', ';', '\'', '"', '?', '!' });
                }
                else if (part != "")
                {
                    newline.Add(part);
                    part = "";
                }
            }
            if (part != "")
                newline.Add(part);
            return newline.ToArray();
        }

        public bool TryVerb(Verbable.Verb verb, string[] line)
        {
            if (verb != null)
            {
                verb(line, TryGetTool(line));
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool TryVerb(Verbable target, string name, string[] line)
        {
            if (Verbs.ContainsKey(name))
                return TryVerb(target.Verbs[name], line);
            else if (VerbShortcuts.ContainsKey(name))
                return TryVerb(target.Verbs[VerbShortcuts[name]], line);
            else if (target.Verbs.ContainsKey(name))    // for hidden custom verbs
                return TryVerb(target.Verbs[name], line);
            else
                throw new ArgumentOutOfRangeException("name");
        }

        public void TryLook(Verbable target, string[] line)
        {
            if (!TryVerb(target, "look", line))
                target.Describe();
        }

        public void TryEnter(Verbable target, string[] line)
        {
            if (target == CurrentRoom)
                RAEGame.PrintLine("Enter what?");
            else
            {
                try
                {
                    if (!TryVerb(target.Verbs["enter"], line))
                    {
                        if (target is NPC)
                            RAEGame.PrintLine("?!?");
                        else
                            RAEGame.PrintLine("You don't know you'd go into {0}.", target.ToString());
                    }
                }
                catch
                {
                    if (line.Length < 2)
                        RAEGame.PrintLine("Could not enter.");
                    else
                        RAEGame.PrintLine("Could not find {0} to go into.", line[1]);
                }
            }
        }

        public void TryPickup(Verbable target, string[] line)
        {
            if (!TryVerb(target, "pickup", line))
            {
                if (target is Item && !Player.Contents.Contains(target))
                {
                    Verbable oldOther = null;
                    if (target.Location is Item)
                        oldOther = target.Location;

                    target.Location = Player;

                    if (oldOther != null)
                        RAEGame.PrintLine("You take {0} out of {1}.", target.ToTheString(), oldOther.ToTheString());
                    else
                        RAEGame.PrintLine("You pick up {0}.", target.ToTheString());
                }
                else
                {
                    RAEGame.PrintLine("You're not sure how to pick up {0}.", target.ToString());
                }
            }
        }

        public void TryDrop(Verbable target, string[] line)
        {
            if (!TryVerb(target, "drop", line))
            {
                if (target is Item && Player.Contents.Contains(target))
                {
                    target.Location = CurrentRoom;
                    RAEGame.PrintLine("You drop {0}.", target.ToTheString());
                }
                else
                {
                    RAEGame.PrintLine("You're not sure how to drop {0}.", target.ToString());
                }
            }
        }

        public void TryInventory(string[] line)
        {
            RAEGame.PrintLine("Your inventory: ");
            foreach (Item item in Player.Contents)
            {
                WriteInventoryItem(item, 2);
            }
        }

        void WriteInventoryItem(Item item, int tab)
        {
            RAEGame.PrintLine(new string(' ', tab) + "* " + item);
            if (item.InventoryVisible)
                foreach (Item subitem in item.Contents)
                {
                    WriteInventoryItem(subitem, tab + 2);
                }
        }

        public Verbable TryGetTool(string[] line)
        {
            if (line != null && line.Length > 2)
            {
                Verbable i;
                i = FindTarget(line[2]);

                if (i is Item)
                {
                    // items must be held
                    if (Player.Contents.Contains(i) || Player.Contents.Contains(i.Location))
                        return (Item)i;
                    else
                        throw new ArgumentOutOfRangeException();
                }
                else if (i is Spot || i is NPC)
                {
                    // spots and NPC's are fine
                    return i;
                }
                else
                {
                    // tool invalid
                    RAEGame.PrintLine("You do not have {0}.", i.ToString());
                    throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                // no tool
                return null;
            }
        }

        bool MatchesTarget(Verbable v, string name)
        {
            return v.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)
                || v.AKA.Contains(name.ToLower());
        }

        public Verbable FindTarget(string name)
        {
            if (MatchesTarget(CurrentRoom, name))
                return CurrentRoom;
            foreach (Spot s in CurrentRoom.Spots.Values)
            {
                if (!s.Removed && MatchesTarget(s, name))
                    return s;
            }
            foreach (Item i in Player.Contents)
            {
                if (i.InventoryVisible)
                    foreach (Verbable sv in i.Contents)
                        if (MatchesTarget(sv, name))
                            return sv;
                if (MatchesTarget(i, name))
                    return i;
            }
            foreach (Verbable i in CurrentRoom.Contents)
            {
                if (MatchesTarget(i, name))
                    return i;
            }
            if (Room.CompassDirections.Contains(name)
                && CurrentRoom.Spots.ContainsKey(name)
                && !CurrentRoom.Spots[name].Removed)
            {
                return CurrentRoom.Spots[name];
            }
            else if (Room.ShortCompassDirections.Contains(name))
            {
                string card = Room.CompassDirections[Room.ShortCompassDirections.ToList().IndexOf(name)];
                if (CurrentRoom.Spots.ContainsKey(card)
                    && !CurrentRoom.Spots[card].Removed)
                    return CurrentRoom.Spots[card];
            }
            else if (MatchesTarget(Player, name))
                return Player;

            throw new ArgumentOutOfRangeException();
        }

        public void TryHelp(string[] line)
        {
            PrintLine("Valid commands include: ");
            PrintLine(" * \x001BEnew\x001B7\n * \x001BEsave\x001B7\n * \x001BEload\x001B7 (\x001BErestore\x001B7)\n * \x001BEquit\x001B7\n * \x001BEhelp\x001B7");

            int linesLeft = ConsoleHeight - 8;

            foreach (string v in Verbs.Keys)
            {
                Print(" * " + Colorize(v, "yellow"));
                IEnumerable<string> shorts = (from kv in VerbShortcuts
                                              where kv.Value == v
                                              select kv.Key);
                if (shorts.Count() > 0)
                {
                    Print(" (");
                    bool first = true;
                    foreach (string vs in shorts)
                    {
                        Print((!first ? "," : "") + Colorize(vs, "yellow"));
                        first = false;
                    }
                    PrintLine(")");
                }
                else
                {
                    PrintLine("");
                }

                linesLeft--;
                if (linesLeft <= 0)
                {
                    Pause();
                    linesLeft = ConsoleHeight - 2;
                }
            }

            if(linesLeft != ConsoleHeight - 2)
                Pause();
            PrintLine("\nOne can move in compass directions by just typing the direction.");
            
            for (int i = 0; i < Room.CompassDirections.Length; i++)
            {
                PrintLine(" * "
                    + Colorize(Room.CompassDirections[i], "yellow") + " ("
                    + Colorize(Room.ShortCompassDirections[i], "yellow") + ")");
            }
        }
        
        public void ParseLine()
        {
            string[] fullLine = Input();
            string[] line = SanitizeLine(fullLine);
            //RAEGame.PrintLine("< " + line.Length);
            if (line.Length == 0 || (line.Length == 1 && line[0] == ""))
                return;

            string verb = line[0].ToLower();

            // attempt to access internal verb
            if (verb.StartsWith("$"))
                return;

            Verbable target;
            if (line.Length >= 2)
            {
                try
                {
                    target = FindTarget(line[1]);
                }
                catch
                {
                    target = null;
                }
            }
            else if (CurrentRoom == null)
            {
                PrintLine("You are lost.");
                return;
            }
            else
                target = CurrentRoom;

            switch (verb)
            {
                case "quit":
                    Quit();
                    break;
                case "save":
                    try
                    {
                        if (line.Length == 1)
                            SaveGame();
                        else
                            SaveGame(line[1] + "." + GetType().Name + "-sav");
                    }
                    catch (Exception e)
                    {
                        PrintLine("Could not save file:\n\t" + e.Message);
                    }
                    break;
                case "load":
                case "restore":
                    try
                    {
                        if (line.Length == 1)
                            LoadGame();
                        else
                            LoadGame(line[1] + "." + GetType().Name + "-sav");
                    }
                    catch (Exception e)
                    {
                        PrintLine("Invalid save file:\n\t" + e.Message);
                    }
                    break;
                case "new":
                    PrintLine("Start a new game?");
                    if (Menu(new string[] { "New Game" }, "No") == 1)
                    {
                        NewGame();
                    }
                    break;
                case "help":
                    TryHelp(line);
                    break;
                default:
                    // actually a cardinal verb?
                    if (Room.CompassDirections.Contains(verb)
                        && CurrentRoom.Spots.ContainsKey(verb))
                    {
                        TryEnter(CurrentRoom.Spots[verb], line);
                    }
                    else if (Room.ShortCompassDirections.Contains(verb))
                    {
                        string card = Room.CompassDirections[Room.ShortCompassDirections.ToList().IndexOf(verb)];
                        if (CurrentRoom.Spots.ContainsKey(card))
                            TryEnter(CurrentRoom.Spots[card], line);
                    }
                    else
                    {
                        // okay, a normal verb

                        if (target == null)
                        {
                            PrintLine("You cannot see " + FabricateProperNoun(line[1]) + ".");
                            return;
                        }

                        try
                        {
                            // call normal verb
                            if (Verbs.ContainsKey(verb))
                                Verbs[verb](target, line, fullLine);
                            else if (VerbShortcuts.ContainsKey(verb))
                                Verbs[VerbShortcuts[verb]](target, line, fullLine);
                            else
                                throw new Exception();
                        }
                        catch
                        {
                            // failed finding verb

                            string name;
                            try
                            {
                                name = FindTarget(line[1]).ToString();
                            }
                            catch
                            {
                                if (line.Length > 1)
                                    name = "the " + line[1];
                                else
                                    name = null;
                            }
                            PrintLine("You don't know how to " + line[0]
                                + (line.Length > 1 ? " " + name : "") + ".");
                        }
                    }
                    break;
            }

            // tick all the verbables
            Tick();
        }

        /// <summary>
        /// Fabricates a nicer form of the noun with an article, etc
        /// </summary>
        /// <param name="fragment">The arbitrary chunk we are dealing with</param>
        /// <returns></returns>
        public static string FabricateProperNoun(string fragment)
        {
            if (fragment.Contains(' '))
                return string.Format("\"{0}\"", fragment);

            if (fragment.EndsWith("s"))
                return "some " + fragment;

            char[] vowels = { 'a', 'e', 'i', 'o', 'u' };

            if (vowels.Contains(char.ToLower(fragment.First())))
                return "an " + fragment;
            else
                return "a " + fragment;
        }

        public static void Quit()
        {
            Environment.Exit(0);
        }

        public static void Wait(int ms)
        {
            GameConsole.Wait(ms);
        }

        static List<object> mRegisteredInstances
            = new List<object>();

        public static void RegisterInstance(object o)
        {
            mRegisteredInstances.Add(o);
        }

        public void NewGame()
        {
            InternalNewGame();

            RAEGame game = (RAEGame)GetType().GetProperty("Instance").GetGetMethod()
                .Invoke(null, null);
            game.Player.AutoLookOnMovement = true;
            game.TryLook(game.CurrentRoom, new string[0]);
        }

        private static void InternalNewGame()
        {
            foreach (object o in mRegisteredInstances)
            {
                if (o is Verbable)
                    ((Verbable)o).ResetInstance();
                else if (o is RAEGame)
                    ((RAEGame)o).ResetInstance();
            }
            mRegisteredInstances.Clear();
            FullClear();
            CursorX = 0;
            CursorY = 0;
        }

        public abstract void ResetInstance();

        public bool LoadGame()
        {
            string[] files = Directory.GetFiles(".", "*." + GetType().Name + "-sav");

            string[] menu = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo fi = new FileInfo(files[i]);
                menu[i] = fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
            }

            PrintLine("Choose a file to load:");
            int choice = Menu(menu, "Back");

            if (choice == 0)
            {
                return false;
            }
            else
            {
                LoadGame(files[choice - 1]);
                return true;
            }
        }

        private void InternalLoadGame(string filename)
        {
            var r = new BinaryReader(File.OpenRead(filename));
            try
            {
                if (new string(r.ReadChars(4)) != "RASV")
                    throw new InvalidDataException("Invalid header for RASV file.");

                if (r.ReadString() != GetType().FullName)
                {
                    throw new InvalidDataException("Save file for different game.");
                }
                Name = r.ReadString();

                // props
                int numProps = r.ReadInt32();
                for (int i = 0; i < numProps; i++)
                {
                    string propName = r.ReadString();
                    string propVal = r.ReadString();

                    FieldInfo fi = GetType().GetField(propName, BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Instance);
                    if (fi == null)
                    {
                        throw new InvalidDataException(propName + " is not a recognized game variable.");
                    }
                    Type pt = fi.FieldType;
                    if (pt.IsPrimitive)
                    {
                        fi.SetValue(this,
                            pt.GetMethod("Parse", new Type[] { typeof(string) }).Invoke(null, new object[] { propVal })
                        );
                    }
                    else if (pt == typeof(string))
                    {
                        fi.SetValue(this,
                            propVal);
                    }
                    else if (pt.IsEnum)
                    {
                        fi.SetValue(this,
                            Enum.Parse(pt, propVal)
                        );
                    }
                    else if (pt == typeof(Verbable) || pt.IsSubclassOf(typeof(Verbable)))
                    {
                        fi.SetValue(this,
                            Type.GetType(propVal).GetProperty("Instance").GetGetMethod().Invoke(null, null)
                        );
                    }
                    else
                    {
                        throw new ArgumentException(fi.ToString() + " was saved as wrong type.");
                    }
                }

                // then... list
                int numThens = r.ReadInt32();
                for (int i = 0; i < numThens; i++)
                {
                    mThenHashes.Add(r.ReadInt32());
                }

                // items
                int numObjs = r.ReadInt32();
                for (int i = 0; i < numObjs; i++)
                {
                    string typeName = r.ReadString();
                    Type t = Type.GetType(typeName);
                    if (t == null)
                    {
                        t = Assembly.GetAssembly(GetType()).GetType(typeName);
                    }

                    // if it still is null, it is a removed element of the game.
                    // Just fake parsing and move on
                    if (t == null)
                    {
                        numProps = r.ReadInt32();
                        for (int j = 0; j < numProps; j++)
                        {
                            r.ReadString();
                            r.ReadString();
                        }

                        continue;
                    }

                    Verbable o = (Verbable)t.GetProperty("Instance").GetGetMethod().Invoke(null, null);
                    o.Unserialize(r);
                }
            }
            finally
            {
                r.Close();
            }

            Player.AutoLookOnMovement = true;
            TryLook(CurrentRoom, new string[0]);
        }

        public void LoadGame(string filename)
        {
            InternalNewGame();

            // kinda have to mess with this a bit to not make first room not start
            RAEGame game = (RAEGame)GetType().GetProperty("Instance").GetGetMethod()
                .Invoke(null, null);

            game.InternalLoadGame(filename);
        }

        internal void SetMember(Verbable o, Type t, string propName, string propVal)
        {
            MemberInfo mi = null;
            try
            {
                var mis = t.GetMember(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.Instance);
                foreach (var m in mis)
                {
                    if (m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                    {
                        mi = m;
                        break;
                    }
                }
                if (mi == null)
                    throw new Exception(); ;
            }
            catch
            {
                throw new InvalidDataException(propName + " not found in " + t.Name);
            }
            if (mi is PropertyInfo)
            {
                PropertyInfo pi = (PropertyInfo)mi;
                Type pt = pi.PropertyType;
                if (pt.IsPrimitive)
                {
                    pi.GetSetMethod().Invoke(o, new object[] {
                                pt.GetMethod("Parse", new Type[] { typeof(string) }).Invoke(null, new object[] { propVal })
                            });
                }
                else if (pt == typeof(string))
                {
                    pi.GetSetMethod().Invoke(o, new object[] {
                                propVal
                            });
                }
                else if (pt.IsEnum)
                {
                    pi.GetSetMethod().Invoke(o, new object[] {
                                Enum.Parse(pt, propVal)
                            });
                }
                else if (pt == typeof(Verbable) || pt.IsSubclassOf(typeof(Verbable)))
                {
                    Type rt = Type.GetType(propVal);
                    if (rt == null)
                    {
                        rt = Assembly.GetAssembly(GetType()).GetType(propVal);
                    }
                    if (rt == null)
                    {
                        throw new ArgumentException(propVal + " does not seem to exist");
                    }

                    pi.GetSetMethod().Invoke(o, new object[] {
                                rt.GetProperty("Instance").GetGetMethod().Invoke(null, null)
                            });
                }
                else if (pt == typeof(State))
                {
                    if (!o.States.ContainsKey(propVal))
                    {
                        throw new ArgumentException("Invalid state for " + o + ": " + propVal);
                    }

                    pi.GetSetMethod().Invoke(o, new object[] {
                                o.States[propVal]
                            });
                }
                else
                {
                    throw new ArgumentException(pi.ToString() + " was saved as wrong type.");
                }
            }
            else if (mi is FieldInfo)
            {
                FieldInfo fi = (FieldInfo)mi;
                Type pt = fi.FieldType;
                if (pt.IsPrimitive)
                {
                    fi.SetValue(o,
                        pt.GetMethod("Parse", new Type[] { typeof(string) }).Invoke(null, new object[] { propVal })
                    );
                }
                else if (pt == typeof(string))
                {
                    fi.SetValue(o,
                        propVal);
                }
                else if (pt.IsEnum)
                {
                    fi.SetValue(o,
                        Enum.Parse(pt, propVal)
                    );
                }
                else if (pt == typeof(Verbable) || pt.IsSubclassOf(typeof(Verbable)))
                {
                    fi.SetValue(o,
                        Type.GetType(propVal).GetProperty("Instance").GetGetMethod().Invoke(null, null)
                    );
                }
                else if (pt == typeof(State))
                {
                    fi.SetValue(o,
                        o.States[propVal]
                    );
                }
                else
                {
                    throw new ArgumentException(fi.ToString() + " was saved as wrong type.");
                }
            }
            else
            {
                throw new InvalidDataException(propName + " not found in " + t.Name);
            }
        }

        public bool SaveGame()
        {
            string[] files = Directory.GetFiles(".", "*." + GetType().Name + "-sav");

            int newSave = files.Length;
            string[] menu = new string[files.Length + 1]; // add one for New Save
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo fi = new FileInfo(files[i]);
                menu[i] = fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
            }
            menu[newSave] = "New Save";

            RAEGame.PrintLine("Choose a file to save:");
            int choice = Menu(menu, "Back");

            if (choice == 0)
            {
                return false;
            }
            else if (choice <= files.Length)
            {
                SaveGame(files[choice - 1]);
                return true;
            }
            else
            {
                string name = InputLine("Name: ") + "." + GetType().Name + "-sav";
                SaveGame(name);
                return true;
            }
        }

        public void SaveGame(string filename)
        {
            var w = new BinaryWriter(new FileStream(filename, FileMode.Create));
            try
            {
                w.Write(new byte[] { 0x52, 0x41, 0x53, 0x56 }); // RASV

                List<Verbable> items = new List<Verbable>();
                items.Add(Player);
                items.AddRange(Items.Values);
                items.AddRange(NPCs.Values);
                items.AddRange(Rooms.Values);
                foreach (var r in Rooms.Values)
                {
                    items.AddRange(r.Spots.Values);
                }

                // basic game props
                w.Write(GetType().FullName);
                w.Write(Name);

                // write props
                var props = new Dictionary<string, string>();
                string propVal;
                foreach (var fi in GetType().GetFields(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    Type pt = fi.FieldType;
                    if (pt.IsPrimitive || pt == typeof(string))
                    {
                        propVal = fi.GetValue(this).ToString();
                    }
                    else if (pt.IsEnum)
                    {
                        propVal = fi.GetValue(this).ToString();
                    }
                    else if (pt == typeof(Verbable) || pt.IsSubclassOf(typeof(Verbable)))
                    {
                        Verbable v = (Verbable)fi.GetValue(this);
                        if (v == null)
                            continue;
                        propVal = v.GetType().FullName;
                    }
                    else
                    {
                        continue;
                    }
                    props.Add(fi.Name, propVal);
                }
                w.Write(props.Count);
                foreach (var p in props)
                {
                    w.Write(p.Key);
                    w.Write(p.Value);
                }

                // write then... list
                w.Write(mThenHashes.Count);
                foreach (var h in mThenHashes)
                {
                    w.Write(h);
                }

                // write items
                int countPos = (int)w.BaseStream.Position;
                w.Write(0);
                int count = 0;
                foreach (var o in items)
                {
                    if (o.Serialize(w))
                        count++;
                }
                w.Seek(countPos, SeekOrigin.Begin);
                w.Write(count);
            }
            finally
            {
                w.Close();
            }
        }

        public static int Roll(int max)
        {
            return mRandomizer.Next(max);
        }

        public static int Roll(int min, int max)
        {
            return mRandomizer.Next(min, max + 1);
        }

        public static double RollDouble(double max)
        {
            return mRandomizer.NextDouble() * max;
        }

        public static double RollDouble(double min, double max)
        {
            return min + mRandomizer.NextDouble() * (max - min);
        }

        public static object Pick(System.Collections.IList list)
        {
            return list[Roll(list.Count)];
        }

        internal void UpdateTitle()
        {
            Console.Title =
                (Name != null ? Name : GetType().Name)
                + (CurrentRoom != null ? " - "
                    + Capitalize((CurrentRoom.Article != Article.None ? Enum.GetName(typeof(Article), CurrentRoom.Article).ToLower() + " " : "")
                    + CurrentRoom.Name)
                    : "");
        }

        public bool CheckThen(int hash)
        {
            bool contains = mThenHashes.Contains(hash);
            if (!contains)
                mThenHashes.Add(hash);
            return contains;
        }

        internal int InternalGetVWaitTicks(Verbable v)
        {
            if (VTickers.ContainsKey(v))
                return VTickers[v];
            else
                return Verbable.WAIT_NEVER;
        }

        internal void InternalSetVWaitTicks(Verbable v, int ticks)
        {
            if (ticks == Verbable.WAIT_NEVER)
            {
                if (VTickers.ContainsKey(v))
                    VTickers.Remove(v);
            }
            else
            {
                VTickers[v] = ticks;
            }
        }

        public void Tick()
        {
            Queue<Verbable> triggered = new Queue<Verbable>();

            for (int i = 0; i < VTickers.Count; i++)
            {
                var kv = VTickers.Skip(i).First();
                VTickers[kv.Key] = kv.Value - 1;

                if (kv.Value <= 1)
                    triggered.Enqueue(kv.Key);
            }

            while (triggered.Count > 0)
            {
                Verbable v = triggered.Dequeue();
                VTickers.Remove(v);

                if (v.Verbs.ContainsKey("$tick"))
                {
                    v.Verbs["$tick"](null, null);
                }
            }
        }
    }

    public enum Article
    {
        A,
        The,
        Some,
        An,
        Your,
        My,
        None
    }

    public class State : ICloneable
    {
        public string Name { get; set; }
        public Article Article { get; set; }
        public List<string> AKA { get; private set; }
        public Dictionary<string, Verbable.Verb> Verbs { get; private set; }

        public State(string name, Article a)
        {
            Name = name;
            Article = a;
            AKA = new List<string>();
            Verbs = new Dictionary<string, Verbable.Verb>();
        }

        public object Clone()
        {
            State s = new State(Name, Article);
            s.AKA = AKA.ToList();
            s.Verbs = Verbs.ToDictionary(kv => kv.Key, kv => kv.Value);
            return s;
        }
    }

    public abstract class Verbable
    {
        public delegate void Verb(string[] line, Verbable tool);

        public Dictionary<string, State> States;
        public State CurrentState { get; set; }
        public State Default;
        public RAEGame Game { get; private set; }
        [DefaultValue(25)]
        public int SayTypingRate { get; set; }

        public Dictionary<string, Verb> Verbs
        {
            get
            {
                return CurrentState.Verbs;
            }
        }

        public readonly string DefaultName;
        public readonly Article DefaultArticle;

        public string Name
        {
            get { return CurrentState.Name; }
            set { CurrentState.Name = value; }
        }
        public Article Article
        {
            get { return CurrentState.Article; }
            set { CurrentState.Article = value; }
        }
        public List<string> AKA { get { return CurrentState.AKA; } }

        public override string ToString()
        {
            return RAEGame.Bold((Article != RAE.Game.Article.None ? Article.ToString().ToLower() + " " : "") + Name);
        }

        public virtual string ToTheString()
        {
            return RAEGame.Bold((Article != RAE.Game.Article.None ? "the " : "") + Name);
        }

        public virtual void Describe()
        {
            RAEGame.PrintLine("You see " + ToString() + ".");
        }

        public State AddState(string name)
        {
            State s = (State)States["default"].Clone();
            States[name] = s;
            CurrentState = s;
            return s;
        }

        public string StateName
        {
            get
            {
                return (from kv in States
                        where kv.Value == CurrentState
                        select kv.Key).First();
            }
        }

        Verbable mLocation;
        public virtual Verbable Location
        {
            get { return mLocation; }
            set
            {
                //RAEGame.PrintLine("MOVE " + ToString() + ": "
                //    + mLocation + " TO " + value);

                if (mLocation != null)
                    mLocation.mContents.Remove(this);
                mLocation = value;
                if (mLocation != null)
                    mLocation.mContents.Add(this);
            }
        }

        List<Verbable> mContents;
        public List<Verbable> Contents { get { return mContents; } }

        public void Give(IEnumerable<Verbable> items)
        {
            foreach (Verbable item in items)
                item.Location = this;
        }

        public void Take(IEnumerable<Verbable> items)
        {
            foreach (Verbable item in items)
                if (item.Location == this)
                    item.Location = null;
        }

        public Verbable(RAEGame game, string defaultName, Article defaultArticle)
        {
            Game = game;
            this.DefaultName = defaultName;
            this.DefaultArticle = defaultArticle;
            States = Verbable.GetNestedTypeList<State>(GetType(), this);
            Default = new State(GetType().Name, RAE.Game.Article.A);
            foreach (string verb in Game.Verbs.Keys)
                Default.Verbs.Add(verb, null);
            States.Add("default", Default);
            CurrentState = Default;
            CurrentState.Name = defaultName;
            CurrentState.Article = defaultArticle;
            mContents = new List<Verbable>();
            SayTypingRate = 25;
        }

        public abstract void Init();

        internal static Dictionary<string, T> GetNestedTypeList<T>(Type masterType, params object[] newParams)
        {
            Type[] newTypes = (from object o in newParams
                               select o.GetType()).ToArray();

            Dictionary<string, T> os;
            if (typeof(T).IsSubclassOf(typeof(Verbable)))
            {
                os = (from Type t in masterType.GetNestedTypes(
                          BindingFlags.Public | BindingFlags.FlattenHierarchy)
                      where t.IsSubclassOf(typeof(T))
                      select (T)t.GetProperty("Instance")
                          .GetGetMethod().Invoke(null, new object[0]))
                    .ToDictionary(t => t.GetType().Name);
            }
            else
            {
                os = (from Type t in masterType.GetNestedTypes(
                             BindingFlags.Public | BindingFlags.FlattenHierarchy)
                      where t.IsSubclassOf(typeof(T))
                      select (T)t.GetConstructor(newTypes)
                         .Invoke(newParams))
                    .ToDictionary(t => t.GetType().Name);
            }
            return os;
        }

        public void Say(string content)
        {
            RAEGame.Print(RAEGame.Capitalize(ToTheString()) + ": ");
            RAEGame.TypeOutLine(content, SayTypingRate);
            RAEGame.Wait(SayTypingRate * 2);
        }

        internal bool Serialize(BinaryWriter w)
        {
            Dictionary<string, string> props = new Dictionary<string, string>();
            string propVal;

            Type t = GetType();

            foreach (var fi in t.GetFields(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                // ignore readonly fields
                if (fi.IsInitOnly)
                    continue;

                // this is a prop backing field
                if (fi.Name.StartsWith("<"))
                    continue;

                Type pt = fi.FieldType;
                if (pt.IsPrimitive || pt == typeof(string))
                {
                    propVal = fi.GetValue(this).ToString();
                }
                else if (pt.IsEnum)
                {
                    propVal = fi.GetValue(this).ToString();
                }
                else if (pt == typeof(Verbable) || pt.IsSubclassOf(typeof(Verbable)))
                {
                    Verbable v = (Verbable)fi.GetValue(this);
                    if (v == null)
                        continue;
                    propVal = v.GetType().FullName;
                }
                else if (pt == typeof(State))
                {
                    State s = (State)fi.GetValue(this);
                    propVal = (from kv in States
                               where kv.Value == s
                               select kv.Key).First();
                    if (propVal.ToLower() == fi.Name.ToLower())
                        continue;   // actually field defining it
                    if (propVal == "default")
                        continue;   // default state is default value :p
                }
                else
                {
                    continue;
                }
                props.Add(fi.Name, propVal);
            }
            foreach (var pi in t.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance))
            {
                // check for default
                object[] defaults = pi.GetCustomAttributes(typeof(DefaultValueAttribute), true);
                if (defaults.Length >= 1
                    && ((DefaultValueAttribute)defaults[0]).Value.Equals(pi.GetGetMethod().Invoke(this, null)))
                {
                    // this property is at default
                    continue;
                }

                // check if name or article is instance-defined default
                if (pi.Name == "Name" && Name == DefaultName)
                    continue;
                if (pi.Name == "Article" && Article == DefaultArticle)
                    continue;

                // special case for CurrentState's default
                if (pi.Name == "CurrentState"
                    && CurrentState == Default)
                {
                    continue;
                }

                Type pt = pi.PropertyType;
                if (!pi.CanWrite || !pi.CanRead)
                    continue;
                if (pt.IsPrimitive || pt == typeof(string))
                {
                    propVal = pi.GetGetMethod().Invoke(this, null).ToString();
                }
                else if (pt.IsEnum)
                {
                    propVal = pi.GetGetMethod().Invoke(this, null).ToString();
                }
                else if (pt == typeof(Verbable) || pt.IsSubclassOf(typeof(Verbable)))
                {
                    Verbable v = (Verbable)pi.GetGetMethod().Invoke(this, null);
                    if (v == null)
                        continue;
                    propVal = v.GetType().FullName;
                }
                else if (pt == typeof(State))
                {
                    State s = (State)pi.GetGetMethod().Invoke(this, null);
                    if (s == null)
                        continue;
                    propVal = (from kv in States
                               where kv.Value == s
                               select kv.Key).First();
                    if (propVal.ToLower() == pi.Name.ToLower())
                        continue;   // actually field defining it
                    if (propVal == "default")
                        continue;   // default state is default value :p
                }
                else
                {
                    continue;
                }
                props.Add(pi.Name, propVal);
            }

            w.Write(t.FullName);
            w.Write(props.Count);
            foreach (var p in props)
            {
                w.Write(p.Key);
                w.Write(p.Value);
            }
            return true;
        }

        internal void Unserialize(BinaryReader r)
        {
            Type t = GetType();
            int numProps = r.ReadInt32();

            Dictionary<string, string> props = new Dictionary<string, string>();

            // read props
            for (int j = 0; j < numProps; j++)
            {
                props.Add(r.ReadString(), r.ReadString());
            }
            // set the values defined in save file
            foreach (KeyValuePair<string, string> kv in props)
            {
                Game.SetMember(this, t, kv.Key, kv.Value);
            }
            // then explicitly override properties which have a default and weren't saved
            // as they implicitly should have default value
            foreach (PropertyInfo pi in t.GetProperties())
            {
                if (pi.MemberType == MemberTypes.Property && !props.Keys.Contains(pi.Name))
                {
                    object[] attrs = pi.GetCustomAttributes(typeof(DefaultValueAttribute), true);
                    if (attrs.Length > 0)
                    {
                        // has a default
                        Game.SetMember(this, t, pi.Name, ((DefaultValueAttribute)attrs[0]).Value.ToString());
                    }
                }
            }
            // same with default state
            if (!props.Keys.Contains("CurrentState"))
                CurrentState = Default;
        }

        public abstract void ResetInstance();

        public const int WAIT_NEVER = 0;
        [DefaultValue(WAIT_NEVER)]
        public int WaitTicks
        {
            get { return Game.InternalGetVWaitTicks(this); }
            set
            {
                if (value < WAIT_NEVER)
                    throw new ArgumentOutOfRangeException();

                Game.InternalSetVWaitTicks(this, value);
            }
        }
    }

    public class Player : Verbable
    {
        public Player(RAEGame game)
            : base(game, "player", Article.The)
        {
            if (mInstance == null)
                mInstance = this;

            AKA.AddRange(new string[] {
                "me", "myself", "I"});

            AutoLookOnMovement = false;
        }

        static Player mInstance;
        public static Player Instance
        {
            get
            {
                return mInstance;
            }
        }

        [DefaultValue(true)]
        public bool AutoLookOnMovement { get; set; }

        public override Verbable Location
        {
            get
            {
                return base.Location;
            }
            set
            {
                if (!(value is Room))
                    throw new ArgumentException("Player location must be a room.");

                base.Location = value;
                Game.UpdateTitle();

                if (value != null)
                    Game.TryVerb(value, "enter", new string[0]);
                if (AutoLookOnMovement)
                    Game.TryLook(value, new string[0]);
            }
        }

        public override string ToString()
        {
            return RAEGame.CC("yellow")
                + ((Article != Article.None ? Article.ToString().ToLower() + " " : "") + Name)
                + RAEGame.CC("gray");
        }

        public override string ToTheString()
        {
            return RAEGame.CC("yellow")
                + ((Article != Article.None ? "the " : "") + Name)
                + RAEGame.CC("gray");
        }

        public override void ResetInstance()
        {
            mInstance = null;
        }

        public override void Init()
        {
        }
    }

    public abstract class Room : Verbable
    {
        public Dictionary<string, Spot> Spots { get; private set; }
        public static readonly string[] CompassDirections
            = new string[] { "north", "south", "east", "west", "northeast", "southeast", "southwest", "northwest" };
        public static readonly string[] ShortCompassDirections
            = new string[] { "n", "s", "e", "w", "ne", "se", "sw", "nw" };

        public Room(RAEGame game, string defaultName, Article defaultArticle)
            : base(game, defaultName, defaultArticle)
        {
            DescribedBefore = false;
        }

        public override void Init()
        {
            Spots = GetNestedTypeList<Spot>(GetType(), Game, this);
        }

        [DefaultValue(false)]
        public bool DescribedBefore { get; set; }

        public override void Describe()
        {
            if (!DescribedBefore)
            {
                RAEGame.PrintLine("You find yourself in {0}.", ToString());
                DescribedBefore = true;
            }
            else
            {
                RAEGame.PrintLine("You find yourself back in {0}.", ToTheString());
            }

            foreach (string card in CompassDirections)
            {
                if (Spots.ContainsKey(card) && !Spots[card].Removed)
                    RAEGame.PrintLine("To the " + card + ", you see " + Spots[card] + ".");
            }

            var spotList = (from KeyValuePair<string, Spot> kv in Spots
                            where !CompassDirections.Contains(kv.Key) && !kv.Value.Hidden && !kv.Value.Removed
                            select kv.Value).ToArray();
            if (spotList.Length > 0)
            {
                RAEGame.Print("You see ");
                for (int i = 0; i < spotList.Length; i++)
                {
                    Spot s = spotList[i];
                    RAEGame.Print((i == 0 ? "" : (i == spotList.Length - 1 ? ", and " : ", ")) + s);
                }
                RAEGame.PrintLine(".");
            }

            var items = (from Verbable v in Contents
                         where v is Item
                         select v).ToArray();
            if (items.Length > 0)
            {
                RAEGame.Print("Lying around, there is ");
                for (int i = 0; i < items.Length; i++)
                {
                    RAEGame.Print((i == 0 ? "" : (i == items.Length - 1 ? ", and " : ", ")) + items[i]);
                }
                RAEGame.PrintLine(".");
            }

            var npcs = (from Verbable v in Contents
                        where v is NPC
                        select v).ToArray();
            if (npcs.Length > 0)
            {
                RAEGame.Print("Standing around, you see ");
                for (int i = 0; i < npcs.Length; i++)
                {
                    RAEGame.Print((i == 0 ? "" : (i == npcs.Length - 1 ? ", and " : ", ")) + npcs[i]);
                }
                RAEGame.PrintLine(".");
            }
        }
    }

    public abstract class Spot : Verbable
    {
        /// <summary>
        /// If hidden from room description
        /// </summary>
        [DefaultValue(false)]
        public bool Hidden { get; set; }
        /// <summary>
        /// If completely unreferencable in the parser, also acts hidden.
        /// </summary>
        [DefaultValue(false)]
        public bool Removed { get; set; }
        public Room Room { get; private set; }

        public Spot(RAEGame game, Room room, string defaultName, Article defaultArticle)
            : base(game, defaultName, defaultArticle)
        {
            Room = room;
            Hidden = false;
            Removed = false;
        }
    }

    public abstract class Item : Verbable
    {
        public Item(RAEGame game, string defaultName, Article defaultArticle)
            : base(game, defaultName, defaultArticle)
        {
            InventoryVisible = false;
        }

        [DefaultValue(false)]
        public bool InventoryVisible { get; set; }

        public override void Describe()
        {
            if (Game.Player.Contents.Contains(this))
                RAEGame.PrintLine("You have " + this + ".");
            else
                base.Describe();
        }
    }

    public abstract class NPC : Verbable
    {
        public NPC(RAEGame game, string defaultName, Article defaultArticle)
            : base(game, defaultName, defaultArticle)
        {
            MovementVerb = "walks";
        }

        [DefaultValue("walks")]
        public string MovementVerb
        {
            get;
            set;
        }

        public override Verbable Location
        {
            get
            {
                return base.Location;
            }
            set
            {
                if (!(value is Room))
                    throw new ArgumentException("NPC location must be a room.");

                base.Location = value;
            }
        }

        /// <summary>
        /// Change the NPC's location, but in a more flowery way
        /// </summary>
        /// <param name="destination"></param>
        public void MoveTo(Room destination)
        {
            if (Location == Game.Player.Location)
            {
                RAEGame.PrintLine(ToString() + " " + MovementVerb + " over to " + destination.ToString() + ".");
            }
            else if (destination == Game.Player.Location)
            {
                RAEGame.PrintLine(ToString() + " " + MovementVerb + " in from " + Location.ToString() + ".");
            }

            Location = destination;
        }
    }
}
