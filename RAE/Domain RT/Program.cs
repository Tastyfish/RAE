using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Text.RegularExpressions;

using Irony.Parsing;
using RAE.Game;

namespace RAE
{
    class Program
    {
        private static void PrintHelp()
        {
            Console.WriteLine("RAE Compiler / Runtime v"
                    + Assembly.GetExecutingAssembly().GetName().Version);
            Console.WriteLine("Usage: rae [-sv] [--name=...] [filename ...]");
            Console.WriteLine("-s\tSaves the assembly.");
            Console.WriteLine("-v\tVerbose compilation.");
            Console.WriteLine("--name\tSet name of output.");
        }

        static void Main(string[] args)
        {
            bool optionSave = false;
            bool optionVerbose = false;
            string asmName = null;
            List<string> files = new List<string>();

            var flagR = new Regex(@"^\-(\w+)$");
            var nameR = new Regex(@"^\-\-name=(\w+)$");
            var starR = new Regex(@"^([^\*]*?)([^\\/*]*\*[^\\/*]*)$");

            foreach (string arg in args)
            {
                Match m = flagR.Match(arg);
                if (m.Success)
                {
                    foreach (char flag in m.Groups[1].Value)
                    {
                        switch (flag)
                        {
                            case 's':
                                optionSave = true;
                                break;
                            case 'v':
                                optionVerbose = true;
                                break;
                        }
                    }
                }
                else
                {
                    m = nameR.Match(arg);
                    if (m.Success)
                    {
                        asmName = m.Groups[1].Value;
                    }
                    else
                    {
                        m = starR.Match(arg);
                        if (m.Success)
                        {
                            foreach (var sf in Directory.GetFiles(m.Groups[1].Value, m.Groups[2].Value, SearchOption.AllDirectories))
                            {
                                if (!files.Contains(sf))
                                    files.Add(sf);
                            }
                        }
                        else if (!files.Contains(arg))
                        {
                            files.Add(arg);
                        }
                    }
                }
            }

            if (files.Count == 0)
            {
                PrintHelp();
                return;
            }

            // default to first file name
            if (asmName == null)
            {
                FileInfo f = new FileInfo(files[0]);
                asmName = f.Name.Substring(0, f.Name.Length - f.Extension.Length);
            }

            Parser p = new Parser(new RAEGrammer());
            ParseTree[] trees = new ParseTree[files.Count];
            bool failed = false;

            if (optionVerbose)
                Console.WriteLine("Will build " + asmName + "...");

            for (int i = 0; i < trees.Length; i++)
            {
                if (optionVerbose)
                    Console.WriteLine("Parsing " + files[i] + "...");

                FileInfo f = new FileInfo(files[i]);
                string fileCnt;
                try
                {
                    fileCnt = f.OpenText().ReadToEnd();
                }
                catch (FileNotFoundException e)
                {
                    Console.WriteLine(e.Message);
                    return;
                }
                trees[i] = p.Parse(fileCnt, f.Name);
                if (trees[i].Status == ParseTreeStatus.Error)
                {
                    foreach (var msg in trees[i].ParserMessages)
                    {
                        Console.WriteLine(f.Name + msg.Location.ToString()
                            + ": " + msg.Message);
                    }
                    Console.WriteLine("" + trees[i].ParserMessages.Count + " errors in " + f.Name);
                    failed = true;
                }
            }

            if (optionVerbose)
                Console.WriteLine("Compiling...");

            if (failed)
                Environment.Exit(2);

            Compiler cpl = new Compiler(asmName)
            {
                Verbose = optionVerbose
            };
            cpl.Compile(trees, files.ToArray(), optionSave);
        }
    }

    partial class Compiler
    {
        Stack<ClassInfo> ClassStack;
        ClassInfo CurrentClass { get { return ClassStack.Peek(); } }
        FunctionInfo CurrentFunction { get { return CurrentClass.CurrentFunction; } }
        Dictionary<string, Type> QualifiedTypes;
        Dictionary<string, ClassInfo> TopLevelClasses;
        Dictionary<string, FunctionInfo> PrecacheVerbs = new Dictionary<string, FunctionInfo>();
        Dictionary<string, string> PrecacheVerbShortcuts = new Dictionary<string, string>();
        Stack<Label> BreakScopes = new Stack<Label>();  // the label is where the break statement should jump to
        Stack<TryBlockInfo> TryScopes = new Stack<TryBlockInfo>();    // see TryBlockInfo
        public bool Verbose = false;
        List<TypeReference> UnresolvedTypeReferences = new List<TypeReference>();

        class TypeReference
        {
            public string UnresolvedName;
            public ParseTreeNode FullNode;
            public ClassInfo Class;
            public ReferenceType ReferenceType;
            public ResolveContinuation Continuation;
            public object Param;
        }
        delegate void ResolveContinuation(TypeReference tr);

        public enum ReferenceType
        {
            TypeBase,
            Global
        }

        public class ClassInfo
        {
            public TypeBuilder Class;
            public FunctionInfo CurrentFunction { get { return FunctionStack.Peek(); } }
            public Stack<FunctionInfo> FunctionStack;
            public Dictionary<string, FunctionInfo> Functions;
            public Dictionary<string, GlobalInfo> Globals;
            public MethodBuilder GetInstance;
            public FieldInfo InstanceField;
            public Dictionary<string, ClassInfo> SubClasses;
            public FunctionInfo Contructor;

            public Type[] ExtraConParams;
            public object[] InitConParams;
            public ParseTreeNode UserConParams;
        }

        public class FunctionInfo
        {
            public MethodBuilder Method;
            public ILGenerator IL;
            public Dictionary<string, LocalInfo> Arguments;
            public Dictionary<string, LocalInfo> Locals;
            public List<LocalInfo> AllocedTemps;
            //public Label ReturnLabel;
            public bool IsStatic { get { return Method.IsStatic; } }
        }

        public struct GlobalInfo
        {
            public Type Type;
            public FieldBuilder Field;
        }

        public struct LocalInfo
        {
            public Type Type;
            public int Index;
        }

        public class TryBlockInfo
        {
            /// <summary>
            /// the label is where a return should leave to in a try/catch
            /// </summary>
            public Label ReturnLabel;
            public string ReturnVar;
            public bool LabelUsed = false;
        }

        AssemblyBuilder NewAssembly;
        ModuleBuilder NewModule;
        string AsmName;
        string SourceName = "Unknown";
        ISymbolDocumentWriter SourceDocument;

        public Compiler(string asmName)
        {
            AsmName = asmName;

            // setup assembly
            AssemblyName an = new AssemblyName(asmName.ToUpperInvariant());

            NewAssembly =
                AppDomain.CurrentDomain.DefineDynamicAssembly(an,
                AssemblyBuilderAccess.RunAndSave);
            ConstructorInfo debc = typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });
            CustomAttributeBuilder debb = new CustomAttributeBuilder(debc, new object[] {
                DebuggableAttribute.DebuggingModes.DisableOptimizations
            });
            NewAssembly.SetCustomAttribute(debb);
            NewModule = NewAssembly.DefineDynamicModule(an.Name, asmName + ".exe", true);

            ClassStack = new Stack<ClassInfo>();
            QualifiedTypes = new Dictionary<string, Type>();
            TopLevelClasses = new Dictionary<string, ClassInfo>();
            AddQualifiedExtras();
        }

        void AddQualifiedExtras()
        {
            QualifiedTypes.Add("exception", typeof(Exception));

            Assembly mscorlib = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.FullName.Split(new char[] { ',' })[0] == "mscorlib").First();
            // some generic type defs
            QualifiedTypes.Add("list`1", mscorlib.GetType("System.Collections.Generic.List`1"));
            QualifiedTypes.Add("dictionary`2", mscorlib.GetType("System.Collections.Generic.Dictionary`2"));
        }

        void DoError(ParseTreeNode node, string msg)
        {
            Console.WriteLine(SourceName
                + node.Span.Location.ToString()
                + ": " + msg);
            System.Environment.Exit(1);
        }

        [Serializable]
        class CompileException : Exception
        {
            public readonly ParseTreeNode Location;

            public CompileException(string msg)
                : base(msg)
            {
                Location = null;
            }

            public CompileException(ParseTreeNode location, string msg)
                : base(msg)
            {
                Location = location;
            }
        }

        public void Compile(ParseTree[] tree, string[] fileNames, bool save)
        {
            FunctionInfo verbInitFn = new FunctionInfo(); // HACK: all this dumb byref stuff for verb compiling

            if (Verbose)
                Console.WriteLine("First pass...");
            for (int i = 0; i < tree.Length; i++)
            {
                SourceName = fileNames[i];
                if (Verbose)
                    Console.WriteLine(" " + SourceName);
                SourceDocument = NewModule.DefineDocument(SourceName, Guid.Empty, Guid.Empty, Guid.Empty);
                try
                {
                    PreloadProgram(tree[i].Root, ref verbInitFn);
                }
                catch (CompileException e)
                {
                    if (e.Location != null)
                        DoError(e.Location, e.Message);
                    else
                        DoError(tree[i].Root, e.Message);
                }
            }

            // check for unresolved types
            if (UnresolvedTypeReferences.Count > 0)
            {
                Console.WriteLine("Unresolved types:");
                foreach (var t in UnresolvedTypeReferences)
                {
                    Console.WriteLine(t.UnresolvedName + " at " + t.FullNode.Span.ToString());
                }
                Environment.Exit(1);
            }

            if (Verbose)
                Console.WriteLine("Second pass...");
            for (int i = 0; i < tree.Length; i++)
            {
                SourceName = fileNames[i];
                if (Verbose)
                    Console.WriteLine(" " + SourceName);
                SourceDocument = NewModule.DefineDocument(SourceName, Guid.Empty, Guid.Empty, Guid.Empty);
                try
                {
                    DoProgram(tree[i].Root);
                }
                catch (CompileException e)
                {
                    if (e.Location != null)
                        DoError(e.Location, e.Message);
                    else
                        DoError(tree[i].Root, e.Message);
                }
            }
            FinishProgram(verbInitFn);

            // add start function, that's just { Game.Instance.ParseLine(); }
            MethodBuilder main = NewModule.DefineGlobalMethod(".main",
                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(void), Type.EmptyTypes);
            ILGenerator mainIL = main.GetILGenerator();

            // just pick first game found for execution
            Type game = null;
            try
            {
                game = (from Type t in NewModule.GetTypes()
                        where t.IsSubclassOf(typeof(RAEGame))
                        select t).First();

                // RAEGame.Run<game>()
                MethodInfo runMI = typeof(RAEGame).GetMethod("Run", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(game);
                mainIL.Emit(OpCodes.Call, runMI);
            }
            catch
            {
                // don't actually start a game because there isn't one
                mainIL.EmitWriteLine("No Game object.");
            }
            mainIL.Emit(OpCodes.Ret);

            NewModule.CreateGlobalFunctions();
            NewAssembly.SetEntryPoint(main, PEFileKinds.ConsoleApplication);

            // Save
            if (save)
            {
                if (game != null)
                    NewAssembly.Save(AsmName + ".exe");
                else
                    NewAssembly.Save(AsmName + ".dll");
            }

            NewModule.GetMethod(".main").Invoke(null, null);
        }
    }
}
