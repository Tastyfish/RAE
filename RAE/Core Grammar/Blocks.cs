using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony.Ast;
using Irony.Parsing;
using System.Reflection;
using System.Reflection.Emit;

using RAE.Game;

namespace RAE
{
    partial class Compiler
    {
        void PreloadProgram(ParseTreeNode node, ref FunctionInfo verbInitFn)
        {
            foreach (ParseTreeNode line in node.ChildNodes)
            {
                //CurrentIdea.IL.EmitWriteLine("LIVE: " + line.Term.Name);

                if (line.Term.Name == "GAME")
                {
                    PreloadGame(line, ref verbInitFn);
                }
                else if (line.Term.Name == "ITEM")
                {
                    PreloadVerbable(line, typeof(Item));
                    ClassStack.Pop();
                }
                else if (line.Term.Name == "NPC")
                {
                    PreloadVerbable(line, typeof(NPC));
                    ClassStack.Pop();
                }
                else if (line.Term.Name == "ROOM")
                {
                    PreloadVerbable(line, typeof(Room));
                    ClassStack.Pop();
                }
                else if (line.Term.Name == "USING IDS")
                    DoUsing(line);
                else if (line.Term.Name == "VERB")
                {
                    string[] vs = PreloadVerb(line);
                    for (int i = 1; i < vs.Length; i++)
                        PrecacheVerbShortcuts.Add(vs[i], vs[0]);
                }
                else if (line.Term.Name == "NONE")
                { }
                else
                    throw new Exception("Program cannot contain at root " + line.Term.Name);
            }

            if (ClassStack.Count != 1)
                throw new CompileException("Class stack misalignment in Program>Precache");
        }

        void DoProgram(ParseTreeNode node)
        {
            foreach (ParseTreeNode line in node.ChildNodes)
            {
                //CurrentIdea.IL.EmitWriteLine("LIVE: " + line.Term.Name);

                if (line.Term.Name == "GAME")
                    DoGame(line);
                else if (line.Term.Name == "ITEM")
                    DoVerbable(line, typeof(Item));
                else if (line.Term.Name == "NPC")
                    DoVerbable(line, typeof(NPC));
                else if (line.Term.Name == "ROOM")
                    DoVerbable(line, typeof(Room));
                else if (line.Term.Name == "USING IDS")
                { }
                else if (line.Term.Name == "VERB")
                    DoVerb(line);
                else if (line.Term.Name == "NONE")
                { }
                else
                    throw new Exception("Program cannot contain at root " + line.Term.Name);
            }
        }

        void FinishProgram(FunctionInfo verbInitFn)
        {
            CurrentClass.FunctionStack.Push(verbInitFn);

            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);

            // verbs
            CurrentFunction.IL.Emit(OpCodes.Newobj,
                typeof(Dictionary<string, RAEGame.GlobalVerb>).GetConstructor(Type.EmptyTypes));
            foreach (var verb in PrecacheVerbs)
            {
                CurrentFunction.IL.Emit(OpCodes.Dup);
                LoadConstant(verb.Key);

                CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                CurrentFunction.IL.Emit(OpCodes.Ldftn, verb.Value.Method);
                CurrentFunction.IL.Emit(OpCodes.Newobj,
                    typeof(RAEGame.GlobalVerb).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));

                CurrentFunction.IL.Emit(OpCodes.Call,
                    typeof(Dictionary<string, RAEGame.GlobalVerb>).GetMethod("Add"));
            }

            // vsc
            CurrentFunction.IL.Emit(OpCodes.Newobj,
                typeof(Dictionary<string, string>).GetConstructor(Type.EmptyTypes));
            foreach (KeyValuePair<string, string> kv in PrecacheVerbShortcuts)
            {
                CurrentFunction.IL.Emit(OpCodes.Dup);
                LoadConstant(kv.Key);
                LoadConstant(kv.Value);
                CurrentFunction.IL.Emit(OpCodes.Call,
                    typeof(Dictionary<string, string>).GetMethod("Add"));
            }

            // finally call parent init
            CurrentFunction.IL.Emit(OpCodes.Call,
                typeof(RAEGame).GetMethod("Init"));

            CloseFunction(verbInitFn, true);

            // close game object
            if (CurrentClass.FunctionStack.Count == 0 || CurrentFunction.Method.Name != "Init")
                throw new CompileException("Game stack misalignment: Init");
            if (!CurrentClass.Class.IsSubclassOf(typeof(RAEGame)))
                throw new CompileException("Game stack misalignment: Class");
            CloseFunction(CurrentFunction, true);
            CloseClass(CurrentClass, false);
            CloseSubClasses(TopLevelClasses);
        }

        void CloseSubClasses(Dictionary<string, ClassInfo> cis)
        {
            foreach (ClassInfo ci in cis.Values)
            {
                if(Verbose)
                    Console.WriteLine("Closing " + ci.Class.Name);
                ci.Class.CreateType();

                CloseSubClasses(ci.SubClasses);
            }
        }

        void DoUsing(ParseTreeNode node)
        {
            string name = node.ChildNodes.Last().Token.Text;
            string qual = "";

            bool first = true;
            foreach (ParseTreeNode child in node.ChildNodes)
            {
                qual += (!first ? "." : "") + child.Token.Text;
                first = false;
            }

            Type t = Type.GetType(qual, false, true);
            if (t == null)
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(qual, false, true);
                    if (t != null)
                        break;
                }
                if (t == null)
                {
                    DoError(node.ChildNodes[3], "Type " + qual + " not found.");
                }
            }

            QualifiedTypes.Add(name.ToLower(), t);
        }

        void MarkLinePosition(ParseTreeNode node)
        {
            CurrentFunction.IL.MarkSequencePoint(
                SourceDocument, node.Span.Location.Line + 1, node.Span.Location.Column,
                node.Span.Location.Line + 1, node.Span.Location.Column);
        }

        struct StatementInfo
        {
            /// <summary>Every path within statement ends in a return</summary>
            /// <remarks>
            /// ie. can optimize out rest of path safely,
            /// and return on fn (!void) will be clean if ends this way
            /// </remarks>
            public bool Returns;
            /// <summary>
            /// Similar to Returns, but only ends in break;
            /// </summary>
            /// <remarks>
            /// ie. can optimize out rest of path safely
            /// </remarks>
            public bool Breaks;

            /// <summary>
            /// Indicates it is a statement that does not exist
            /// </summary>
            private bool NonExistant;

            public static StatementInfo operator &(StatementInfo a, StatementInfo b)
            {
                if (a.NonExistant)
                    return b;
                else if (b.NonExistant)
                    return a;
                else
                    return new StatementInfo()
                    {
                        Returns = a.Returns && b.Returns,
                        Breaks = a.Breaks && b.Breaks
                    };
            }

            public readonly static StatementInfo Continuous
                = new StatementInfo();
            public readonly static StatementInfo NonExistent
                = new StatementInfo() { NonExistant = true };
        }

        StatementInfo DoStatement(ParseTreeNode node)
        {
            MarkLinePosition(node);

            switch (node.Term.Name)
            {
                case "RETURN":
                    return DoReturn(node);
                case "BREAK":
                    return DoBreak(node);
                case "GLOBAL DEF":
                    return DoGlobalDef(node);
                case "LOCAL DEF":
                    return DoLocalDef(node);
                case "WHILE":
                    return DoWhile(node);
                case "IF":
                    return DoIf(node);
                case "FOR":
                    return DoFor(node);
                case "SWITCH":
                    return DoSwitch(node);
                case "FN DEF":
                    return DoFnDef(node);
                case "ON VERB":
                    return DoOnVerb(node);
                case "TICK":
                    return DoTick(node);
                case "SPOT":
                    return DoSpot(node, false);
                case "SPOTST":
                    return DoSpot(node, true);
                case "STATE":
                    return DoState(node);
                case "TRY":
                    return DoTryCatch(node);
                case "THROW":
                    return DoThrow(node);
                case "DIALOG STATEMENT":
                    return DoDialogStatement(node);
                case "DIALOG MENU":
                    return DoDialogMenu(node);
                case "NONE":
                    return StatementInfo.Continuous;
                default:
                    // pass down to expr handling since they can happen at same level
                    Type t = DoExpr(node, true);
                    if (t == typeof(string))
                    {
                        // shortcut to output!
                        CurrentFunction.IL.Emit(OpCodes.Call,
                            typeof(RAEGame).GetMethod("PrintLine",
                            new Type[] { typeof(string) }));
                    }
                    else if (t == typeof(void))
                    {
                        // do nothing
                    }
                    else
                    {
                        // illegal
                        DoError(node, "Invalid expression to be used as statement.");
                    }
                    return StatementInfo.Continuous;
            }
        }

        FunctionInfo CreateFunction(string name, Dictionary<string, LocalInfo> pars, Type retType, MethodAttributes attribs = MethodAttributes.Public)
        {
            if (CurrentClass.Functions.ContainsKey(name))
                throw new CompileException("Function of name " + name + " already exists in " + CurrentClass.Class.Name + ".");

            // build method
            bool virt = false;
            Type[] parTypes = (from arg in pars select arg.Value.Type).ToArray();
            MethodInfo baseMethod = GetMethod(CurrentClass.Class.BaseType, name, BindingFlags.Public | ((attribs & MethodAttributes.Static) != 0 ? BindingFlags.Static : BindingFlags.Instance), parTypes);
            if (baseMethod != null && baseMethod.IsVirtual)
                virt = true;

            MethodBuilder mb = CurrentClass.Class.DefineMethod(name,
                attribs | (virt ? MethodAttributes.Virtual : 0),
                retType, parTypes); // get argument type array
            for (int i = 0; i < pars.Count; i++)
                mb.DefineParameter(i + 1, ParameterAttributes.None, pars.Keys.Skip(i).First());
            ILGenerator il = mb.GetILGenerator();
            FunctionInfo fi = new FunctionInfo()
            {
                Method = mb,
                IL = il,
                Locals = new Dictionary<string, LocalInfo>(),
                Arguments = pars,
                //ReturnLabel = il.DefineLabel(),
                AllocedTemps = new List<LocalInfo>()
            };
            CurrentClass.FunctionStack.Push(fi);
            CurrentClass.Functions.Add(name, fi);

            return fi;
        }

        void CloseFunction(FunctionInfo fi, bool addReturn)
        {
            if (!CurrentFunction.Equals(fi))
                throw new Exception("Call stack misalignment at end of " + fi.Method.Name);
            if (fi.AllocedTemps.Count != 0)
                throw new Exception("Temp var not freed of type " + fi.AllocedTemps.First().Type.Name);
            if (BreakScopes.Count != 0)
                throw new Exception(BreakScopes.Count.ToString() + " break scopes not exited in " + fi.Method.Name);

            if (addReturn)
            {
                Type retType = fi.Method.ReturnType;
                if (retType != typeof(void))
                {
                    // nothing was ever returned, should return 0/null
                    if (retType == typeof(int) || retType == typeof(bool))
                        LoadConstant(0);
                    else if (retType == typeof(double))
                        LoadConstant(0.0);
                    else
                        LoadConstant(null);
                }
                // return pt
                //CurrentFunction.IL.MarkLabel(CurrentFunction.ReturnLabel);
                fi.IL.Emit(OpCodes.Ret);
            }

            CurrentClass.FunctionStack.Pop();
        }

        MethodBuilder AddInstanceProp(TypeBuilder tb, out ILGenerator ibgbIl, out FieldInfo ibVal)
        {
            // props, fields
            var ib = tb.DefineProperty("Instance", PropertyAttributes.None,
                tb, Type.EmptyTypes);
            ibVal = tb.DefineField("__Instance", tb, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.SpecialName);

            // the ResetInstance() virtual method
            var resetM = tb.DefineMethod("ResetInstance", MethodAttributes.Public | MethodAttributes.Virtual);
            ibgbIl = resetM.GetILGenerator();
            // Class.__Instance = null;
            ibgbIl.Emit(OpCodes.Ldnull);
            ibgbIl.Emit(OpCodes.Stsfld, ibVal);
            ibgbIl.Emit(OpCodes.Ret);

            // the Instance getter
            var ibgb = tb.DefineMethod("get_Instance", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, tb, Type.EmptyTypes);
            ib.SetGetMethod(ibgb);
            ibgbIl = ibgb.GetILGenerator();

            ibgbIl.Emit(OpCodes.Ldsfld, ibVal);
            ibgbIl.Emit(OpCodes.Dup);
            ibgbIl.Emit(OpCodes.Ldnull);
            var L1 = ibgbIl.DefineLabel();
            ibgbIl.Emit(OpCodes.Beq_S, L1);
            // != null
            ibgbIl.Emit(OpCodes.Ret);
            // == null
            ibgbIl.MarkLabel(L1);
            ibgbIl.Emit(OpCodes.Pop);
            // calling function will add code to create new instance
            return ibgb;
        }

        void FinishInstanceProp(FieldInfo instField, ILGenerator conGen)
        {
            conGen.Emit(OpCodes.Dup);
            conGen.Emit(OpCodes.Stsfld, instField);
            conGen.Emit(OpCodes.Dup);
            conGen.Emit(OpCodes.Callvirt, CurrentClass.Functions["Init"].Method);
            conGen.Emit(OpCodes.Ret);
        }

        void PreloadChangeClassParent(ClassInfo ci, Type b)
        {
            // this should never happen
            if (ci.Class.BaseType != typeof(object) || b == typeof(object))
                throw new CompileException("Class preload type corruption.");

            ci.Class.SetParent(b);
            
            // inst var

            if (IsSubclassOf(b, typeof(Verbable)) || b == typeof(RAEGame))
            {
                ILGenerator ibgbIl;
                ci.GetInstance = AddInstanceProp(ci.Class, out ibgbIl, out ci.InstanceField);
                for (int i = 0; i < ci.InitConParams.Length; i++)
                {
                    if (ci.InitConParams[i] is MethodInfo)
                        ibgbIl.Emit(OpCodes.Call, (MethodInfo)ci.InitConParams[i]);
                    else if (ci.InitConParams[i] is string)
                        ibgbIl.Emit(OpCodes.Ldstr, (string)ci.InitConParams[i]);
                    else if (ci.InitConParams[i] is int)
                        ibgbIl.Emit(OpCodes.Ldc_I4, (int)ci.InitConParams[i]);
                    else if (ci.InitConParams[i].GetType().IsEnum)
                        ibgbIl.Emit(OpCodes.Ldc_I4, (int)ci.InitConParams[i]);
                    else
                        throw new CompileException("Dunno how to parse this internal ctor param: " + ci.Class.Name + "..ctor " + i);
                }

                // will finish later when con and Init exists
            }
        }

        void DoChangeClassParent(ClassInfo ci, Type b)
        {
            // do con in form of this(RAEGame game, ...) : base(game, ...);
            ConstructorBuilder con = ci.Class.DefineConstructor(
                MethodAttributes.Public, CallingConventions.HasThis, ci.ExtraConParams);
            ILGenerator il = con.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 1; i <= ci.ExtraConParams.Length; i++)
            {
                switch (i)
                {
                    case 1:
                        il.Emit(OpCodes.Ldarg_1);
                        break;
                    case 2:
                        il.Emit(OpCodes.Ldarg_2);
                        break;
                    case 3:
                        il.Emit(OpCodes.Ldarg_3);
                        break;
                    default:
                        il.Emit(OpCodes.Ldarg, i);
                        break;
                }
            }

            // load user cons if applicable
            Type[] baseCallParams = ci.ExtraConParams;

            if (ci.UserConParams != null)
            {
                baseCallParams = baseCallParams.Concat(DoIsolatedParamList(ci.UserConParams, il)).ToArray();
            }

            ConstructorInfo pctor;

            if (b.IsGenericType)
                pctor = TypeBuilder.GetConstructor(b, b.GetGenericTypeDefinition()
                    .GetConstructor(baseCallParams));
            else
                pctor = b.GetConstructor(baseCallParams);

            il.Emit(OpCodes.Call, pctor);
            il.Emit(OpCodes.Ret);

            // continue GetInstance
            var ibgbIl = ci.GetInstance.GetILGenerator();

            ibgbIl.Emit(OpCodes.Newobj, con);
            // also register object
            // Game..RegisterInstance(o)

            ibgbIl.Emit(OpCodes.Dup);
            ibgbIl.Emit(OpCodes.Call, typeof(RAEGame).GetMethod("RegisterInstance"));

        }

        ClassInfo CreateClass(string name, Type b = null, TypeAttributes extra = 0,
            Type[] extraConParams = null, object[] initConParams = null, ParseTreeNode userConParams = null)
        {
            TypeBuilder tb;
            if (ClassStack.Count == 0)
                tb = NewModule.DefineType(name,
                    TypeAttributes.Public | extra, typeof(object));
            else
                tb = CurrentClass.Class.DefineNestedType(name,
                TypeAttributes.NestedPublic | extra, typeof(object));

            if (extraConParams == null)
                extraConParams = Type.EmptyTypes;
            if (initConParams == null)
                initConParams = new MethodInfo[0];
            
            ClassInfo ci = new ClassInfo()
            {
                Class = tb,
                Functions = new Dictionary<string, FunctionInfo>(),
                Globals = new Dictionary<string, GlobalInfo>(),
                FunctionStack = new Stack<FunctionInfo>(),
                GetInstance = null,
                InstanceField = null,
                SubClasses = new Dictionary<string, ClassInfo>(),
                InitConParams = initConParams,
                ExtraConParams = extraConParams,
                UserConParams = userConParams
            };

            if (b != typeof(object))
            {
                PreloadChangeClassParent(ci, b);
            }

            if (ClassStack.Count > 0)
                CurrentClass.SubClasses.Add(name.ToLower(), ci);
            else
                TopLevelClasses.Add(name, ci);
            ClassStack.Push(ci);

            CheckResolveType(ci.Class);

            return ci;
        }

        void CloseClass(ClassInfo ci, bool fullyClose = true)
        {
            Type bt = ci.Class.BaseType;

            if (bt != typeof(object)
                && (IsSubclassOf(bt, typeof(Verbable)) || bt == typeof(RAEGame)))
            {
                DoChangeClassParent(ci, bt);

                FinishInstanceProp(ci.InstanceField, ci.GetInstance.GetILGenerator());
            }

            if (!CurrentClass.Equals(ci))
                throw new Exception("Class stack misalignment at end of " + ci.Class.Name);
            if (ci.FunctionStack.Count > 0)
                throw new Exception("Function stack misalignment at end of " + ci.Class.Name);

            ClassStack.Pop();
            if (fullyClose)
                ci.Class.CreateType();
        }

        StatementInfo DoFnDef(ParseTreeNode node)
        {
            // name & ret type
            string name = node.ChildNodes[2].Token.Text.ToLower();
            var typeNode = node.ChildNodes[1];
            Type retType;
            if (typeNode.Term.Name == "VOID")
            {
                retType = typeof(void);
            }
            else
            {
                retType = DoType(typeNode);
            }
            var args = DoDefParamList(node.ChildNodes[3]);

            try
            {
                FunctionInfo fi;
                if (CurrentClass.Functions.ContainsKey(name))
                {
                    // probably preloaded
                    fi = CurrentClass.Functions[name];
                    if (fi.Arguments.Count == args.Count && fi.Method.ReturnType == retType)
                    {
                        CurrentClass.FunctionStack.Push(fi);
                    }
                    else
                    {
                        DoError(node, "Function already defined?");
                    }
                }
                else
                {
                    fi = CreateFunction(name, args, retType);
                }

                StatementInfo blkInfo = DoBlockBody(node.ChildNodes[4]);
                if (retType != typeof(void) && !blkInfo.Returns)
                    DoError(node, "Function must return a " + retType.Name + " on every code path.");

                CloseFunction(fi, !blkInfo.Returns);
            }
            catch (CompileException exc)
            {
                DoError(node, exc.Message);
            }
            return StatementInfo.Continuous;
        }

        void PreloadFnDef(ParseTreeNode node)
        {
            // name & ret type
            string name = node.ChildNodes[2].Token.Text.ToLower();
            var typeNode = node.ChildNodes[1];
            Type retType;
            if (typeNode.Term.Name == "VOID")
            {
                retType = typeof(void);
            }
            else
            {
                retType = DoType(typeNode);
            }
            var args = DoDefParamList(node.ChildNodes[3]);

            try
            {
                FunctionInfo fi = CreateFunction(name, args, retType);

                CurrentClass.FunctionStack.Pop();
            }
            catch (CompileException exc)
            {
                DoError(node, exc.Message);
            }
        }

        StatementInfo DoBlockBody(ParseTreeNode node)
        {
            if (node.ChildNodes.Count == 0)
                return StatementInfo.Continuous;

            for (int i = 0; i < node.ChildNodes.Count; i++)
            {
                //CurrentIdea.IL.EmitWriteLine(CurrentIdea.Method.Name + ": " + i);

                StatementInfo stmt = DoStatement(node.ChildNodes[i]);
                if (stmt.Breaks || stmt.Returns)
                    return stmt;
            }

            return StatementInfo.Continuous;
        }

        StatementInfo DoWhile(ParseTreeNode node)
        {
            Label mainLoopLabel = CurrentFunction.IL.DefineLabel();
            Label loopEntryLabel = CurrentFunction.IL.DefineLabel();
            Label breakLabel = CurrentFunction.IL.DefineLabel();

            // jump to condition part
            CurrentFunction.IL.Emit(OpCodes.Br, loopEntryLabel);

            // block body
            CurrentFunction.IL.MarkLabel(mainLoopLabel);
            BreakScopes.Push(breakLabel);
            StatementInfo blkInfo = DoBlockBody(node.ChildNodes[2]);
            BreakScopes.Pop();

            // condition code
            CurrentFunction.IL.MarkLabel(loopEntryLabel);
            Type t = DoExpr(node.ChildNodes[1]);
            if (t != typeof(bool))
                DoError(node.ChildNodes[1], "While loop requires boolean expression.");
            CurrentFunction.IL.Emit(OpCodes.Brtrue, mainLoopLabel);
            CurrentFunction.IL.MarkLabel(breakLabel);

            // even if the inner block always returns, the condition could be false
            return StatementInfo.Continuous;
        }

        StatementInfo DoFor(ParseTreeNode node)
        {
            Label mainLoopLabel = CurrentFunction.IL.DefineLabel();
            Label loopEntryLabel = CurrentFunction.IL.DefineLabel();
            Label breakLabel = CurrentFunction.IL.DefineLabel();

            DoStatement(node.ChildNodes[1]);

            // jump to condition part
            CurrentFunction.IL.Emit(OpCodes.Br, loopEntryLabel);

            // block body
            CurrentFunction.IL.MarkLabel(mainLoopLabel);
            BreakScopes.Push(breakLabel);
            StatementInfo blkInfo = DoBlockBody(node.ChildNodes[4]);
            BreakScopes.Pop();

            // inc
            DoStatement(node.ChildNodes[3]);

            // condition code
            CurrentFunction.IL.MarkLabel(loopEntryLabel);
            Type t = DoExpr(node.ChildNodes[2]);
            if (t != typeof(bool))
                DoError(node.ChildNodes[2], "For loop requires boolean expression.");
            CurrentFunction.IL.Emit(OpCodes.Brtrue, mainLoopLabel);
            CurrentFunction.IL.MarkLabel(breakLabel);

            // condition could be false first time, nullifying anything gleaned from statement
            return StatementInfo.Continuous;
        }

        StatementInfo DoSwitch(ParseTreeNode node)
        {
            Type checkType = DoExpr(node.ChildNodes[1]);
            List<Label> labels = new List<Label>();
            Label endLabel = CurrentFunction.IL.DefineLabel();
            StatementInfo blksInfo;

            for (int i = 0; i < node.ChildNodes[2].ChildNodes.Count; i++)
            {
                CurrentFunction.IL.Emit(OpCodes.Dup);
                Type caseType = DoExpr(node.ChildNodes[2].ChildNodes[i].ChildNodes[1]);
                ConvertType(caseType, checkType);
                labels.Add(CurrentFunction.IL.DefineLabel());
                CurrentFunction.IL.Emit(OpCodes.Beq, labels[i]);
            }
            CurrentFunction.IL.Emit(OpCodes.Pop);
            if (node.ChildNodes[3].Term.Name == "DEFAULT") // is there a default?
            {
                BreakScopes.Push(endLabel);
                blksInfo = DoBlockBody(node.ChildNodes[3].ChildNodes[1]);
                BreakScopes.Pop();
            }
            else
            {
                blksInfo = new StatementInfo();
            }
            CurrentFunction.IL.Emit(OpCodes.Br, endLabel);

            for (int i = 0; i < node.ChildNodes[2].ChildNodes.Count; i++)
            {
                CurrentFunction.IL.MarkLabel(labels[i]);
                CurrentFunction.IL.Emit(OpCodes.Pop);
                BreakScopes.Push(endLabel);
                StatementInfo blkInfo = DoBlockBody(node.ChildNodes[2].ChildNodes[i].ChildNodes[2]);
                blksInfo &= blksInfo;
                BreakScopes.Pop();
                if (!blkInfo.Returns && !blkInfo.Breaks && i < node.ChildNodes[2].ChildNodes.Count - 1)
                    CurrentFunction.IL.Emit(OpCodes.Br, endLabel);
            }
            CurrentFunction.IL.MarkLabel(endLabel);
            return blksInfo;
        }

        StatementInfo DoIf(ParseTreeNode node)
        {
            Type t = DoExpr(node.ChildNodes[1]);
            if (t != typeof(bool))
                DoError(node.ChildNodes[1], "Branch statement requires boolean expression.");

            Label l = CurrentFunction.IL.DefineLabel();
            CurrentFunction.IL.Emit(OpCodes.Brfalse, l);

            StatementInfo blkInfo = DoBlockBody(node.ChildNodes[2]);
            StatementInfo elseInfo;

            // else? then make else label and skip main if to end
            Label elseLabel = CurrentFunction.IL.DefineLabel();
            bool hasElse = node.ChildNodes[3].ChildNodes.Count > 0;
            if (hasElse && !blkInfo.Returns)
            {
                CurrentFunction.IL.Emit(OpCodes.Br, elseLabel);
            }

            // mark here regardless
            CurrentFunction.IL.MarkLabel(l);

            if (hasElse)
            {
                elseInfo = DoBlockBody(node.ChildNodes[3].ChildNodes[1]);
                CurrentFunction.IL.MarkLabel(elseLabel);
            }
            else
            {
                elseInfo = new StatementInfo();
            }

            return blkInfo & elseInfo;
        }

        StatementInfo DoTryCatch(ParseTreeNode node)
        {
            bool hasVar = node.ChildNodes[3].Term.Name == "DEF VARIABLE";

            // the exception variable
            DefVariable exception;
            if (hasVar)
            {
                exception = DoDefVariable(node.ChildNodes[3]);
                if (exception.Type != typeof(Exception)
                    && !exception.Type.IsSubclassOf(typeof(Exception)))
                    DoError(node, "Exception variable's type must be a subclass of Exception.");
                CreateVariable(exception.Name, exception.Type);
            }
            else
            {
                exception = new DefVariable()
                {
                    Name = "",
                    Type = DoType(node.ChildNodes[3])
                };
                if (exception.Type != typeof(Exception)
                    && !exception.Type.IsSubclassOf(typeof(Exception)))
                    DoError(node, "Exception variable's type must be a subclass of Exception.");
            }

            // label to leave to for inner "returns" -- will only be used if there /is/ a return
            // see DoReturn()
            TryBlockInfo blockInfo = new TryBlockInfo { ReturnLabel = CurrentFunction.IL.DefineLabel() };

            TryScopes.Push(blockInfo);
            CurrentFunction.IL.BeginExceptionBlock();
            
            StatementInfo tryInfo = DoBlockBody(node.ChildNodes[1]);

            MarkLinePosition(node.ChildNodes[3]);
            CurrentFunction.IL.BeginCatchBlock(exception.Type);
            if (hasVar)
            {
                StoreVariable(exception.Name); // store exception data
            }
            else
            {
                CurrentFunction.IL.Emit(OpCodes.Pop);
            }

            StatementInfo catchInfo = DoBlockBody(node.ChildNodes[4]);
            CurrentFunction.IL.EndExceptionBlock();
            TryScopes.Pop();

            // must make a blob to hastilly return that will otherwise be skipped
            if (blockInfo.LabelUsed)
            {
                Label skipLabel = CurrentFunction.IL.DefineLabel();
                CurrentFunction.IL.Emit(OpCodes.Br_S, skipLabel);
                CurrentFunction.IL.MarkLabel(blockInfo.ReturnLabel);
                CurrentFunction.IL.Emit(OpCodes.Ret);
                CurrentFunction.IL.MarkLabel(skipLabel);
            }

            return tryInfo & catchInfo;
        }

        StatementInfo DoThrow(ParseTreeNode node)
        {
            Type exprType = DoExpr(node.ChildNodes[1]);
            CurrentFunction.IL.Emit(OpCodes.Throw);
            return new StatementInfo() { Breaks = true };
        }
    }
}
