using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using Irony.Parsing;

using RAE.Game;

namespace RAE
{
    partial class Compiler
    {
        void PreloadGame(ParseTreeNode node, ref FunctionInfo verbInitFn)
        {
            string className = "";
            if (node.ChildNodes[1].Term.Name == "NONE")
                if (ClassStack.Count != 0)
                    className = CurrentClass.Class.Name;
                else
                    DoError(node.ChildNodes[1], "Game block must have a name if it's the first instance.");
            else
                className = node.ChildNodes[1].Token.Text;

            // create a new one
            if (ClassStack.Count == 0 || CurrentClass.Class.Name != className)
            {
                if (ClassStack.Count > 0)
                {
                    DoError(node, "Cannot define more than one game object.");
                    /*
                    ClassStack.Pop();

                    if (TopLevelClasses.ContainsKey(className))
                    {
                        ClassStack.Push(TopLevelClasses[className]);
                        return;
                    }*/
                }

                ClassInfo ci = CreateClass(node.ChildNodes[1].Token.Text, typeof(RAEGame));
                Dictionary<string, LocalInfo> pars = new Dictionary<string, LocalInfo>();
                FunctionInfo fi = CreateFunction("Init", pars, typeof(void));

                verbInitFn = CreateHiddenFunction(new Dictionary<string, LocalInfo>(), typeof(void));
                CurrentClass.FunctionStack.Pop();

                fi.IL.Emit(OpCodes.Ldarg_0);
                fi.IL.Emit(OpCodes.Call, verbInitFn.Method);
            }
        }

        void DoGame(ParseTreeNode node)
        {
            string className;
            if (node.ChildNodes[1].Term.Name == "NONE")
                className = CurrentClass.Class.Name;
            else
                className = node.ChildNodes[1].Token.Text;

            // replace game class with another
            if (CurrentClass.Class.Name != className)
            {
                // quick sanity checks
                if (CurrentClass.FunctionStack.Count == 0 || CurrentFunction.Method.Name != "Init")
                    throw new CompileException("Game stack misalignment: Init");
                if (!CurrentClass.Class.IsSubclassOf(typeof(RAEGame)))
                    throw new CompileException("Game stack misalignment: Class");

                CloseFunction(CurrentFunction, true);
                CloseClass(CurrentClass, false);

                DoError(node, "Cannot define more than one game object.");

                // create a new one
                //PreloadGame(node, ref );
            }

            DoBlockBody(node.ChildNodes[2]);
        }

        void PreloadVerbable(ParseTreeNode node, Type t)
        {
            ParseTreeNode subClass = node.ChildNodes[1].Term.Name == "NONE" ? null : node.ChildNodes[1];
            ParseTreeNode newParams = node.ChildNodes[2].Term.Name == "NONE" ? null : node.ChildNodes[2];
            string name = node.ChildNodes[3].Token.Text;
            Article a = DoArticle(node.ChildNodes[4]);
            string friendlyName = (string)node.ChildNodes[5].Token.Value;
            string unresolved = null;

            if (subClass != null)
            {
                try
                {
                    Type st = DoType(subClass);
                    if (st.IsGenericType ? !st.GetGenericTypeDefinition().IsSubclassOf(t) : !st.IsSubclassOf(t))
                        DoError(node, st.Name + " is not a subtype of " + t.Name);
                    t = st;
                }
                catch
                {
                    unresolved = PreviousUnresolvedTypeToken;
                }
            }

            ClassInfo ci;
            try
            {
                ci = CreateClass(name, t, 0,
                    new Type[] { typeof(RAEGame), typeof(string), typeof(Article) },
                    new object[] {
                        ClassStack.First().GetInstance,
                        friendlyName,
                        a
                    });
                if (unresolved != null)
                {
                    DeclareUnresolvedType(new TypeReference()
                    {
                        Class = ci,
                        FullNode = subClass,
                        UnresolvedName = unresolved,
                        ReferenceType = ReferenceType.TypeBase
                    });
                }
            }
            catch (Exception e)
            {
                DoError(node, e.Message);
                return;
            }
            Dictionary<string, LocalInfo> pars = new Dictionary<string, LocalInfo>();
            FunctionInfo fi = CreateFunction("Init", pars, typeof(void));

            foreach (ParseTreeNode child in node.ChildNodes[6].ChildNodes)
            {
                switch (child.Term.Name)
                {
                    case "STATE":
                        PreloadState(child);
                        break;
                    case "SPOT":
                        PreloadSpot(child, false);
                        ClassStack.Pop();
                        break;
                    case "SPOTST":
                        PreloadSpot(child, true);
                        ClassStack.Pop();
                        break;
                    case "FN DEF":
                        PreloadFnDef(child);
                        break;
                    case "GLOBAL DEF":
                        PreloadGlobalDef(child);
                        break;
                }
            }
        }

        void PreloadSpot(ParseTreeNode node, bool hasSubType)
        {
            ParseTreeNode subClass = hasSubType ? node.ChildNodes[1] : null;
            ParseTreeNode newParams = hasSubType ? node.ChildNodes[2] : null;
            string name = node.ChildNodes[hasSubType ? 3 : 1].Token.Text;
            Article a = DoArticle(node.ChildNodes[hasSubType ? 4 : 2]);
            string friendlyName = (string)node.ChildNodes[hasSubType ? 5 : 3].Token.Value;

            Type t = typeof(Spot);
            string unresolved = null;

            if (subClass != null)
            {
                try
                {
                    Type st = DoType(subClass);
                    if (st.IsGenericType ? !st.GetGenericTypeDefinition().IsSubclassOf(t) : !st.IsSubclassOf(t))
                        DoError(node, st.Name + " is not a subtype of " + t.Name);
                    t = st;
                }
                catch
                {
                    unresolved = PreviousUnresolvedTypeToken;
                    t = typeof(object);
                }
            }

            ClassInfo ci;
            try
            {
                ci = CreateClass(name, t, 0,
                    new Type[] { typeof(RAEGame), typeof(Room), typeof(string), typeof(Article) },
                    new object[] {
                        ClassStack.Last().GetInstance,
                        ClassStack.First().GetInstance,
                        friendlyName,
                        a
                    },
                    newParams);
            }
            catch (Exception e)
            {
                DoError(node, e.Message);
                return;
            }

            if (unresolved != null)
            {
                DeclareUnresolvedType(new TypeReference()
                {
                    Class = ci,
                    FullNode = subClass,
                    UnresolvedName = unresolved,
                    ReferenceType = ReferenceType.TypeBase,
                    Continuation = (tr) =>
                    {
                        object[] trps = (object[])tr.Param;
                        ci = (ClassInfo)trps[0];
                        node = (ParseTreeNode)trps[1];
                        hasSubType = (bool)trps[2];
                        PreloadSpotP2(ci, node, hasSubType);
                    },
                    Param = new object[] { ci, node, hasSubType }
                });
            }
            else
            {
                PreloadSpotP2(ci, node, hasSubType);
            }
        }

        void PreloadSpotP2(ClassInfo ci, ParseTreeNode node, bool hasSubType)
        {
            ClassStack.Push(ci);

            Dictionary<string, LocalInfo> pars = new Dictionary<string, LocalInfo>();
            FunctionInfo fi = CreateFunction("Init", pars, typeof(void));

            foreach (ParseTreeNode child in node.ChildNodes[hasSubType ? 6 : 4].ChildNodes)
            {
                switch (child.Term.Name)
                {
                    case "STATE":
                        PreloadState(child);
                        break;
                    case "FN DEF":
                        PreloadFnDef(child);
                        break;
                    case "GLOBAL DEF":
                        PreloadGlobalDef(child);
                        break;
                }
            }

            ClassStack.Pop();
        }

        void DoVerbable(ParseTreeNode node, Type t)
        {
            string name = node.ChildNodes[3].Token.Text;
            Article a = DoArticle(node.ChildNodes[4]);
            string friendlyName = (string)node.ChildNodes[5].Token.Value;

            if (!CurrentClass.SubClasses.ContainsKey(name.ToLower()))
                PreloadVerbable(node, t);
            else
                ClassStack.Push(CurrentClass.SubClasses[name.ToLower()]);

            var init = GetMethod(t, "Init");
            if (!init.IsAbstract)
            {
                CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                CurrentFunction.IL.Emit(OpCodes.Call, init);
            }

            StatementInfo blkInfo = DoBlockBody(node.ChildNodes[6]);

            if (CurrentFunction.Method.Name != "Init")
                throw new CompileException("Call stack misalignment in item " + name);
            if (!CurrentClass.Class.IsSubclassOf(t))
                throw new CompileException("Class stack misalignment in item " + name);

            CloseFunction(CurrentFunction, !blkInfo.Returns);
            CloseClass(CurrentClass, false);
        }

        StatementInfo DoSpot(ParseTreeNode node, bool hasSubType)
        {
            if (!hasSubType)
            {
                node.ChildNodes.Insert(1, new ParseTreeNode(RAEGrammer.NONE_TOKEN, node.Span));
                node.ChildNodes.Insert(1, new ParseTreeNode(RAEGrammer.NONE_TOKEN, node.Span));
            }

            DoVerbable(node, typeof(Spot));
            return StatementInfo.Continuous;
        }

        void PreloadState(ParseTreeNode node)
        {
            string name = node.ChildNodes[1].Token.Text;
            CreateVariable(name, typeof(State), true);
        }

        StatementInfo DoState(ParseTreeNode node)
        {
            string name = node.ChildNodes[1].Token.Text;

            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            LoadConstant(name);

            Type bt = CurrentClass.Class.BaseType;
            MethodInfo mi = GetMethod(bt, "AddState");

            CurrentFunction.IL.Emit(OpCodes.Call, mi);
            StoreVariable(name);

            DoBlockBody(node.ChildNodes[2]);

            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            CurrentFunction.IL.Emit(OpCodes.Dup);
            CurrentFunction.IL.Emit(OpCodes.Ldfld,
                typeof(Verbable).GetField("States"));
            LoadConstant("default");
            CurrentFunction.IL.Emit(OpCodes.Call,
                typeof(Dictionary<string, State>)
                .GetProperty("Item").GetGetMethod());

            var gt = bt.IsGenericType ?
                bt.GetGenericTypeDefinition() : bt;

            CurrentFunction.IL.Emit(OpCodes.Call,
                GetSetMethod(bt, gt.GetProperty("CurrentState")));
            return StatementInfo.Continuous;
        }

        string[] PreloadVerb(ParseTreeNode node)
        {
            return (from ParseTreeNode child in node.ChildNodes[1].ChildNodes
                    select child.Token.Text).ToArray();
        }

        void DoVerb(ParseTreeNode node)
        {
            string name = node.ChildNodes[1].ChildNodes[0].Token.Text;
            var pars = new Dictionary<string, LocalInfo>
            {
                { "target", new LocalInfo() { Index = 1, Type = typeof(Verbable) } },
                { "line", new LocalInfo() { Index = 2, Type = typeof(string[]) } },
                { "fullline", new LocalInfo() { Index = 3, Type = typeof(string[]) } }
            };

            FunctionInfo fi = CreateHiddenFunction(pars, typeof(void));
            StatementInfo blkInfo = DoBlockBody(node.ChildNodes[2]);
            CloseFunction(fi, !blkInfo.Returns);

            PrecacheVerbs.Add(name, fi);
        }

        StatementInfo DoOnVerb(ParseTreeNode node)
        {
            string name = node.ChildNodes[1].Token.Text;

            Dictionary<string, LocalInfo> pars = new Dictionary<string, LocalInfo>
            {
                { "line", new LocalInfo() { Index = 1, Type = typeof(string[]) } },
                { "tool", new LocalInfo() { Index = 2, Type = typeof(Item) } }
            };
            FunctionInfo fi = CreateHiddenFunction(pars, typeof(void));

            StatementInfo blkInfo = DoBlockBody(node.ChildNodes[2]);
            CloseFunction(fi, !blkInfo.Returns);

            // get verb dictionary
            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            CurrentFunction.IL.Emit(OpCodes.Call,
                typeof(Verbable).GetProperty("CurrentState").GetGetMethod());
            CurrentFunction.IL.Emit(OpCodes.Call,
                typeof(State).GetProperty("Verbs").GetGetMethod());
            // push key
            LoadConstant(name);

            // push function delegate
            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            CurrentFunction.IL.Emit(OpCodes.Ldftn, fi.Method);
            CurrentFunction.IL.Emit(OpCodes.Newobj,
                typeof(Verbable.Verb).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));

            // set
            try
            {
                CurrentFunction.IL.Emit(OpCodes.Call,
                    typeof(Dictionary<string, Verbable.Verb>).GetProperty("Item").GetSetMethod());
            }
            catch
            {
                DoError(node.ChildNodes[1], "Unknown verb: " + name);
            }
            return StatementInfo.Continuous;
        }

        StatementInfo DoTick(ParseTreeNode node)
        {
            Dictionary<string, LocalInfo> pars = new Dictionary<string, LocalInfo>
            {
                { "$p1", new LocalInfo() { Index = 1, Type = typeof(string[]) } },
                { "$p2", new LocalInfo() { Index = 2, Type = typeof(Item) } }
            };
            FunctionInfo fi = CreateHiddenFunction(pars, typeof(void));

            StatementInfo blkInfo = DoBlockBody(node.ChildNodes[1]);
            CloseFunction(fi, !blkInfo.Returns);

            // get verb dictionary
            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            CurrentFunction.IL.Emit(OpCodes.Call,
                typeof(Verbable).GetProperty("CurrentState").GetGetMethod());
            CurrentFunction.IL.Emit(OpCodes.Call,
                typeof(State).GetProperty("Verbs").GetGetMethod());
            // push key
            LoadConstant("$tick");

            // push function delegate
            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            CurrentFunction.IL.Emit(OpCodes.Ldftn, fi.Method);
            CurrentFunction.IL.Emit(OpCodes.Newobj,
                typeof(Verbable.Verb).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));

            // set
            CurrentFunction.IL.Emit(OpCodes.Call,
                typeof(Dictionary<string, Verbable.Verb>).GetProperty("Item").GetSetMethod());
            return StatementInfo.Continuous;
        }

        StatementInfo DoDialogMenu(ParseTreeNode node)
        {
            // basically a wrapper for
            // while(loop) switch(menu()) {};

            List<Label> labels = new List<Label>();
            Label beginLabel = CurrentFunction.IL.DefineLabel();
            Label endLabel = CurrentFunction.IL.DefineLabel();

            // setup loop
            CurrentFunction.IL.MarkLabel(beginLabel);

            // do briefing code
            BreakScopes.Push(endLabel);
            StatementInfo blksInfo = DoBlockBody(node.ChildNodes[1]);
            BreakScopes.Pop();

            if (blksInfo.Breaks || blksInfo.Returns)
                return blksInfo;

            // do menu(), mainly this will be creating string array
            LoadConstant(node.ChildNodes[2].ChildNodes.Count);
            CurrentFunction.IL.Emit(OpCodes.Newarr, typeof(string));

            for (int i = 0; i < node.ChildNodes[2].ChildNodes.Count; i++)
            {
                CurrentFunction.IL.Emit(OpCodes.Dup);
                LoadConstant(i);
                bool hasif = node.ChildNodes[2].ChildNodes[i].Term.Name == "CASE IF";
                if (hasif)
                {
                    Type t = DoExpr(node.ChildNodes[2].ChildNodes[i].ChildNodes[3]);
                    if (t != typeof(bool))
                        DoError(node.ChildNodes[2].ChildNodes[i].ChildNodes[3],
                            "This conditional expression must have a boolean result.");

                    Label keepLabel = CurrentFunction.IL.DefineLabel();
                    Label continueLabel = CurrentFunction.IL.DefineLabel();

                    CurrentFunction.IL.Emit(OpCodes.Brtrue, keepLabel);
                    CurrentFunction.IL.Emit(OpCodes.Ldnull);
                    CurrentFunction.IL.Emit(OpCodes.Br_S, continueLabel);

                    CurrentFunction.IL.MarkLabel(keepLabel);
                    t = DoExpr(node.ChildNodes[2].ChildNodes[i].ChildNodes[1]);
                    ConvertType(t, typeof(string));

                    CurrentFunction.IL.MarkLabel(continueLabel);
                }
                else
                {
                    Type t = DoExpr(node.ChildNodes[2].ChildNodes[i].ChildNodes[1]);
                    ConvertType(t, typeof(string));
                }

                CurrentFunction.IL.Emit(OpCodes.Stelem, typeof(string));
            }

            if (node.ChildNodes[3].Term.Name == "ESCAPE")
            {
                Type t = DoExpr(node.ChildNodes[3].ChildNodes[1]);
                ConvertType(t, typeof(string));
            }
            else
            {
                CurrentFunction.IL.Emit(OpCodes.Ldnull);
            }

            CurrentFunction.IL.EmitCall(OpCodes.Call,
                typeof(RAEGame).GetMethod("Menu",
                new Type[] { typeof(string[]), typeof(string) }), null);

            // head of switch
            for (int i = 0; i < node.ChildNodes[2].ChildNodes.Count; i++)
            {
                CurrentFunction.IL.Emit(OpCodes.Dup);
                LoadConstant(i + 1);
                labels.Add(CurrentFunction.IL.DefineLabel());
                CurrentFunction.IL.Emit(OpCodes.Beq, labels[i]);
            }
            // escape
            if (node.ChildNodes[3].Term.Name == "ESCAPE") // is there an escape?
            {
                CurrentFunction.IL.Emit(OpCodes.Pop);
                BreakScopes.Push(endLabel);
                blksInfo = DoBlockBody(node.ChildNodes[3].ChildNodes[2]);
                BreakScopes.Pop();
                if (!blksInfo.Returns && !blksInfo.Breaks)
                    CurrentFunction.IL.Emit(OpCodes.Br, endLabel); // finish
            }
            else
            {
                blksInfo = StatementInfo.NonExistent;
            }

            // body of choices
            for (int i = 0; i < node.ChildNodes[2].ChildNodes.Count; i++)
            {
                bool hasif = node.ChildNodes[2].ChildNodes[i].Term.Name == "CASE IF";

                CurrentFunction.IL.MarkLabel(labels[i]);
                CurrentFunction.IL.Emit(OpCodes.Pop);
                BreakScopes.Push(endLabel);
                StatementInfo blkInfo = DoBlockBody(node.ChildNodes[2].ChildNodes[i].ChildNodes[hasif ? 4 : 2]);
                blksInfo &= blkInfo;
                BreakScopes.Pop();
                if (!blkInfo.Returns && !blkInfo.Breaks)
                    CurrentFunction.IL.Emit(OpCodes.Br, beginLabel); // loop to beginning
            }

            CurrentFunction.IL.MarkLabel(endLabel);
            // only remove break, as that was handled here
            blksInfo.Breaks = false;
            return blksInfo;
        }
    }
}
