using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;

using Irony.Parsing;
using RAE.Game;

namespace RAE
{
    partial class Compiler
    {
        Stack<ClassInfo> ClassStack;
        ClassInfo CurrentClass => ClassStack.Peek();
        FunctionInfo CurrentFunction => CurrentClass.CurrentFunction;
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
            public FunctionInfo CurrentFunction => FunctionStack.Peek();
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
            public bool IsStatic => Method.IsStatic;
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
            Environment.Exit(1);
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

        /// <summary>
        /// Add a minimum implementation of a verb that the engine requires to exist.
        /// </summary>
        /// <param name="name">The official verb name eg. look</param>
        /// <param name="tryfn">The builtin verb implementation eg. TryLook</param>
        void AddStockVerb(string name, string tryfn)
        {
            var pars = new Dictionary<string, LocalInfo>
            {
                { "target", new LocalInfo() { Index = 1, Type = typeof(Verbable) } },
                { "line", new LocalInfo() { Index = 2, Type = typeof(string[]) } },
                { "fullline", new LocalInfo() { Index = 3, Type = typeof(string[]) } }
            };

            FunctionInfo fi = CreateHiddenFunction(pars, typeof(void));
            // call TryLook, TryEnter, etc which doesn't take fullline but is an instance method
            fi.IL.Emit(OpCodes.Ldarg_0);
            fi.IL.Emit(OpCodes.Ldarg_1);
            fi.IL.Emit(OpCodes.Ldarg_2);
            fi.IL.Emit(OpCodes.Call, typeof(RAEGame).GetMethod(tryfn, BindingFlags.Public | BindingFlags.Instance));
            CloseFunction(fi, true);

            PrecacheVerbs.Add(name, fi);
        }

        /// <summary>
        /// Add the minimum implementations of the verbs that the engine requires to exist.
        /// </summary>
        /// <seealso cref="AddStockVerb(string, string)"/>
        void AddRequiredVerbs()
        {
            // The game engine requires some sort of implementation of these 2 verbs so let's go with the minimal implementation.

            if (!PrecacheVerbs.ContainsKey("look"))
                AddStockVerb("look", "TryLook");

            if (!PrecacheVerbs.ContainsKey("enter"))
                AddStockVerb("enter", "TryEnter");
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
            AddRequiredVerbs();
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
