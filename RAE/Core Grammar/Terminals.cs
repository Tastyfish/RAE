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
        Type[] DoTypeList(ParseTreeNode node)
        {
            if (node.Term.Name != "TYPE MULTI")
                return new Type[] { DoType(node) };

            Type[] types = new Type[node.ChildNodes.Count];

            for (int i = 0; i < node.ChildNodes.Count; i++)
            {
                types[i] = DoType(node.ChildNodes[i]);
            }
            return types;
        }

        string PreviousUnresolvedTypeToken;

        Type DoType(ParseTreeNode node)
        {
            string name;
            if (node.Token != null)
                name = node.Token.Text.ToLower();
            else
                name = null;

            if (node.Term is IdentifierTerminal)
            {
                if (QualifiedTypes.ContainsKey(name))
                {
                    return QualifiedTypes[name];
                }

                try
                {
                    ClassInfo ci = GetNestedClass(name);
                    return ci.Class;
                }
                catch
                {
                    Type t = Assembly.GetExecutingAssembly().GetType("RAE.Game." + name, false, true);
                    if (t != null)
                        return t;

                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = asm.GetType(name, false, true);
                        if (t != null)
                            return t;
                    }
                    PreviousUnresolvedTypeToken = name;
                    throw new CompileException(node, "Type " + name + " not found.");
                }
            }
            else if (node.Term.Name == "GAME TYPE")
            {
                if (node.ChildNodes[0].Term.Name == "GAME")
                    return ClassStack.Skip(ClassStack.Count - 1).First().Class;
                else
                    return Type.GetType("RAE.Game." + node.ChildNodes[0].Term.Name, true, true);
            }
            else if (node.Term.Name == "PRIM TYPE")
            {
                switch (node.ChildNodes[0].Term.Name)
                {
                    case "INT":
                        return typeof(int);
                    case "STRING":
                        return typeof(string);
                    case "DOUBLE":
                        return typeof(double);
                    case "BOOL":
                        return typeof(bool);
                    default:
                        throw new InvalidOperationException("Invalid prim type: " + node.ChildNodes[0].Term.Name);
                }
            }
            else if (node.Term.Name == "ARRAY TYPE")
            {
                Type elemType = DoType(node.ChildNodes[0]);
                return Type.GetType(elemType.FullName + "[]");
            }
            else if (node.Term.Name == "GENERIC TYPE")
            {
                name = node.ChildNodes[0].Token.Text.ToLower();
                Type[] etypes = DoTypeList(node.ChildNodes[1]);
                name += "`" + etypes.Length;
                Type t;

                if (QualifiedTypes.ContainsKey(name))
                {
                    t = QualifiedTypes[name];
                }
                else
                {
                    t = Assembly.GetExecutingAssembly().GetType("RAE.Game." + name, false, true);
                    if (t == null)
                    {

                        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            t = asm.GetType(name, false, true);
                            if (t != null)
                                break;
                        }

                        if (t == null) {
                            PreviousUnresolvedTypeToken = node.ChildNodes[0].Token.Text;
                            throw new CompileException(node, "Generic type " + node.ChildNodes[0].Token.Text + "#... not found.");
                        }
                    }
                }

                if (!t.IsGenericTypeDefinition)
                    DoError(node, "This is not a generic type!");

                try
                {
                    return t.MakeGenericType(etypes);
                }
                catch
                {
                    // shouldn't be possible, actually - make this fail hard
                    PreviousUnresolvedTypeToken = null;
                    throw new CompileException(node, "Invalid set of type arguments for " + t.Name);
                }
            }

            throw new InvalidOperationException("Invalid type type: " + node.Term.Name);
        }

        void DeclareUnresolvedType(TypeReference r)
        {
            r.UnresolvedName = r.UnresolvedName.ToLower();
            UnresolvedTypeReferences.Add(r);
        }

        void CheckResolveType(Type newType)
        {
            var list = from tr in UnresolvedTypeReferences
                       where tr.UnresolvedName == newType.Name.ToLower()
                       select tr;

            foreach (var tr in list)
            {
                switch (tr.ReferenceType)
                {
                    case ReferenceType.TypeBase:
                        PreloadChangeClassParent(tr.Class, DoType(tr.FullNode));
                        break;
                    case ReferenceType.Global:
                        ClassStack.Push(tr.Class);
                        PreloadGlobalDef(tr.FullNode);
                        ClassStack.Pop();
                        break;
                }
                if (tr.Continuation != null)
                    tr.Continuation(tr);
            }

            UnresolvedTypeReferences.RemoveAll(list.Contains);
        }

        struct DefVariable
        {
            public string Name;
            public Type Type;
        }

        DefVariable DoDefVariable(ParseTreeNode node)
        {
            DefVariable dv = new DefVariable();
            dv.Type = DoType(node.ChildNodes[0]);
            dv.Name = (string)node.ChildNodes[1].Token.Value;
            return dv;
        }
 
        void CreateVariable(string name, Type type, bool global = false, bool priv = false)
        {
            if (IsVariableDefined(name))
            {
                throw new ArgumentException("Variable " + name + " is already defined."
                    + "\nRedundant definition in " + CurrentFunction.Method.Name, name);
            }

            name = name.ToLower();

            // global if defined in Live
            if (global)
            {
                GlobalInfo gi = new GlobalInfo()
                {
                    Type = type,
                    Field = CurrentClass.Class.DefineField(name, type, priv ? FieldAttributes.Private : FieldAttributes.Public)
                };
                CurrentClass.Globals.Add(name, gi);
            }
            else
            {
                LocalBuilder lb = CurrentFunction.IL.DeclareLocal(type);
                lb.SetLocalSymInfo(name);
                CurrentFunction.Locals.Add(name, new LocalInfo() { Type = type, Index = CurrentFunction.Locals.Count });
            }
        }

        Type LoadVariable(string name)
        {
            name = name.ToLower();

            if (CurrentFunction.Locals.ContainsKey(name))
            {
                int idx = CurrentFunction.Locals[name].Index;
                switch (idx)
                {
                    case 0:
                        CurrentFunction.IL.Emit(OpCodes.Ldloc_0);
                        break;
                    case 1:
                        CurrentFunction.IL.Emit(OpCodes.Ldloc_1);
                        break;
                    case 2:
                        CurrentFunction.IL.Emit(OpCodes.Ldloc_2);
                        break;
                    case 3:
                        CurrentFunction.IL.Emit(OpCodes.Ldloc_3);
                        break;
                    default:
                        CurrentFunction.IL.Emit(OpCodes.Ldloc_S, (byte)idx);
                        break;
                }
                return CurrentFunction.Locals[name].Type;
            }
            else if (CurrentFunction.Arguments.ContainsKey(name))
            {
                int idx = CurrentFunction.Arguments[name].Index;
                switch (idx)
                {
                    case 0:
                        CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                        break;
                    case 1:
                        CurrentFunction.IL.Emit(OpCodes.Ldarg_1);
                        break;
                    case 2:
                        CurrentFunction.IL.Emit(OpCodes.Ldarg_2);
                        break;
                    case 3:
                        CurrentFunction.IL.Emit(OpCodes.Ldarg_3);
                        break;
                    default:
                        CurrentFunction.IL.Emit(OpCodes.Ldarg_S, (byte)idx);
                        break;
                }
                return CurrentFunction.Arguments[name].Type;
            }
            else if (CurrentClass.Globals.ContainsKey(name))
            {
                CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                CurrentFunction.IL.Emit(OpCodes.Ldfld, CurrentClass.Globals[name].Field);
                return CurrentClass.Globals[name].Type;
            }
            else
            {
                try
                {
                    // get base field
                    bool isStatic;
                     // call this so we throw before loading this
                    Type t = GetFieldType(CurrentClass.Class.BaseType, name, out isStatic);
                    if (!isStatic)
                        CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                    LoadField(CurrentClass.Class.BaseType, name);
                    return t;
                }
                catch
                {
                    try
                    {
                        // get type instance
                        ClassInfo nc = GetNestedClass(name);

                        CurrentFunction.IL.Emit(OpCodes.Call,
                            nc.GetInstance);
                        return nc.Class;
                    }
                    catch
                    {
                        try
                        {
                            // try game statics
                            MethodInfo mi = typeof(RAEGame).GetProperty(name,
                                BindingFlags.IgnoreCase | BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                                .GetGetMethod();

                            CurrentFunction.IL.Emit(OpCodes.Call, mi);
                            return mi.ReturnType;
                        }
                        catch
                        {
                            throw new CompileException("Variable " + name + " is not defined.");
                        }
                    }
                }
            }
        }

        ClassInfo GetNestedClass(string name)
        {
            for (int i = 0; i < ClassStack.Count; i++)
            {
                ClassInfo c = ClassStack.Skip(i).First();
                if (c.SubClasses.ContainsKey(name))
                    return c.SubClasses[name];
            }
            throw new CompileException("Class not found: " + name);
        }

        Type GetVariableType(string name)
        {
            name = name.ToLower();

            if (CurrentClass.Globals.ContainsKey(name))
            {
                return CurrentClass.Globals[name].Type;
            }
            else if (CurrentFunction.Locals.ContainsKey(name))
            {
                return CurrentFunction.Locals[name].Type;
            }
            else if (CurrentFunction.Arguments.ContainsKey(name))
            {
                return CurrentFunction.Arguments[name].Type;
            }
            else
            {
                try
                {
                    bool isStatic;
                    return GetFieldType(CurrentClass.Class.BaseType, name, out isStatic);
                }
                catch
                {
                    try
                    {
                        // get type instance
                        ClassInfo nc = GetNestedClass(name);

                        return nc.Class;
                    }
                    catch
                    {
                        try
                        {
                            // get game static
                            MethodInfo mi = typeof(RAEGame).GetProperty(name,
                                BindingFlags.IgnoreCase | BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                                .GetGetMethod();
                            return mi.ReturnType;
                        }
                        catch
                        {
                            throw new CompileException("Variable " + name + " is not defined.");
                        }
                    }
                }
            }
        }

        FunctionInfo CreateHiddenFunction(Dictionary<string, LocalInfo> pars, Type retType, bool isStatic = false)
        {
            string name;
            int i = 0;
            while (true)
            {
                name = ".p$" + (i++).ToString();

                if (!CurrentClass.Functions.ContainsKey(name))
                    break; // found one completely unchosen yet
            }

            return CreateFunction(name, pars, retType,
                MethodAttributes.PrivateScope | MethodAttributes.SpecialName | (isStatic ? MethodAttributes.Static : 0));
        }

        string AllocTempVariable(Type t, bool wontFree = false)
        {
            string lname;
            int i = 0;
            while (true)
            {
                lname = "__" + t.Name.ToLower() + "$" + (i++).ToString() + (wontFree ? "$" : "");

                if (IsVariableDefined(lname))
                {
                    var l = CurrentFunction.Locals[lname];
                    if (CurrentFunction.AllocedTemps.Contains(l) || wontFree)
                        continue;       // being used
                    else
                    {
                        CurrentFunction.AllocedTemps.Add(l);
                        return lname;   // we've already created, and it's not being used
                    }
                }
                else
                {
                    break; // found one completely unchosen yet
                }
            }

            var lb = CurrentFunction.IL.DeclareLocal(t, wontFree);
            //lb.SetLocalSymInfo(lname);
            var li = new LocalInfo() { Type = t, Index = lb.LocalIndex };
            CurrentFunction.Locals.Add(lname, li);
            CurrentFunction.AllocedTemps.Add(li);
            return lname;
        }

        void FreeTempVariable(string name)
        {
            if (!CurrentFunction.Locals.ContainsKey(name))
                throw new Exception("LVar " + name + " does not exist.");
            var li = CurrentFunction.Locals[name];

            if (!CurrentFunction.AllocedTemps.Contains(li))
                throw new Exception("TVar " + name + " was not allocated.");

            CurrentFunction.AllocedTemps.Remove(li);
        }

        void StoreVariable(string name)
        {
            name = name.ToLower();

            if (!IsVariableDefined(name))
            {
                throw new CompileException("Variable " + name + " is not defined.");
            }

            if (CurrentClass.Globals.ContainsKey(name))
            {
                var lname = AllocTempVariable(CurrentClass.Globals[name].Type);

                StoreVariable(lname);
                CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                LoadVariable(lname);
                FreeTempVariable(lname);
                CurrentFunction.IL.Emit(OpCodes.Stfld, CurrentClass.Globals[name].Field);
            }
            else if (CurrentFunction.Locals.ContainsKey(name))
            {
                int idx = CurrentFunction.Locals[name].Index;
                switch (idx)
                {
                    case 0:
                        CurrentFunction.IL.Emit(OpCodes.Stloc_0);
                        break;
                    case 1:
                        CurrentFunction.IL.Emit(OpCodes.Stloc_1);
                        break;
                    case 2:
                        CurrentFunction.IL.Emit(OpCodes.Stloc_2);
                        break;
                    case 3:
                        CurrentFunction.IL.Emit(OpCodes.Stloc_3);
                        break;
                    default:
                        CurrentFunction.IL.Emit(OpCodes.Stloc_S, (byte)idx);
                        break;
                }
            }
            else if (CurrentFunction.Arguments.ContainsKey(name))
            {
                int idx = CurrentFunction.Arguments[name].Index;
                CurrentFunction.IL.Emit(OpCodes.Starg_S, (byte)idx);
            }
            else
            {
                try
                {
                    bool isStatic;
                    Type t = GetFieldType(CurrentClass.Class.BaseType, name, out isStatic);
                    if (!isStatic)
                    {
                        string tname = AllocTempVariable(t);
                        StoreVariable(tname);
                        CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                        LoadVariable(tname);
                        FreeTempVariable(tname);
                    }
                    StoreFieldFinish(CurrentClass.Class.BaseType, name);
                }
                catch
                {
                    try
                    {
                        // get type instance
                        ClassInfo nc = GetNestedClass(name);
                        throw new CompileException("A singleton instance cannot be assigned to.");
                    }
                    catch
                    {
                        try
                        {
                            // try game statics?
                            MethodInfo mi = typeof(RAEGame).GetProperty(name,
                                BindingFlags.IgnoreCase | BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                                .GetSetMethod();
                            CurrentFunction.IL.Emit(OpCodes.Call, mi);
                        }
                        catch
                        {
                            throw new CompileException("Variable " + name + " is not defined.");
                        }
                    }
                }
            }
        }

        bool IsVariableDefined(string name)
        {
            name = name.ToLower();

            try
            {
                GetVariableType(name);
                return true;
            }
            catch
            {
                return false;
            }
        }

        StatementInfo DoGlobalDef(ParseTreeNode node)
        {
            bool priv = node.ChildNodes[0].Token.Text == "private";

            Type t = DoType(node.ChildNodes[1]);
            string name = node.ChildNodes[2].Token.Text.ToLower();
            bool hasAssignment = node.ChildNodes[3].ChildNodes.Count > 0;

            if (CurrentClass.Globals.ContainsKey(name))
            {
                GlobalInfo gi = CurrentClass.Globals[name];
                if (gi.Type != t)
                {
                    DoError(node, "Global already defined?");
                }
            }
            else
            {
                try
                {
                    CreateVariable(name, t, true, priv);
                }
                catch (Exception e)
                {
                    DoError(node.ChildNodes[2], e.Message);
                }
            }

            if (hasAssignment)
            {
                Type et = DoExpr(node.ChildNodes[3].ChildNodes[1]);
                ConvertType(et, t);
                StoreVariable(name);
            }
            return StatementInfo.Continuous;
        }

        void PreloadGlobalDef(ParseTreeNode node)
        {
            bool priv = node.ChildNodes[0].Term.Name == "PRIVATE";

            Type t;
            try
            {
                t = DoType(node.ChildNodes[1]);
            }
            catch
            {
                DeclareUnresolvedType(new TypeReference()
                {
                    Class = CurrentClass,
                    FullNode = node,
                    ReferenceType = ReferenceType.Global,
                    UnresolvedName = PreviousUnresolvedTypeToken
                });
                return;
            }
            string name = node.ChildNodes[2].Token.Text.ToLower();
            bool hasAssignment = node.ChildNodes[3].ChildNodes.Count > 0;

            try
            {
                CreateVariable(name, t, true, priv);
            }
            catch (Exception e)
            {
                DoError(node.ChildNodes[2], e.Message);
            }
        }

        StatementInfo DoLocalDef(ParseTreeNode node)
        {
            Type t = DoType(node.ChildNodes[0]);
            string name = (string)node.ChildNodes[1].Token.Value;
            bool hasAssignment = node.ChildNodes[2].ChildNodes.Count > 0;

            try
            {
                CreateVariable(name, t);
            }
            catch (Exception e)
            {
                DoError(node, e.Message);
            }

            if (hasAssignment)
            {
                Type et = DoExpr(node.ChildNodes[2].ChildNodes[1]);
                ConvertType(et, t);
                StoreVariable(name);
            }
            return StatementInfo.Continuous;
        }

        Dictionary<string, LocalInfo> DoDefParamList(ParseTreeNode node)
        {
            Dictionary<string, LocalInfo> pars = new Dictionary<string, LocalInfo>();

            foreach (var def in node.ChildNodes)
            {
                DefVariable dv = DoDefVariable(def);

                pars.Add(dv.Name,
                    new LocalInfo()
                    {
                        Type = dv.Type,
                        Index = pars.Count + 1 // 0 being this
                    });
            }

            return pars;
        }

        public List<Type> DoParamList(ParseTreeNode node)
        {
            List<Type> types = new List<Type>();

            foreach (var def in node.ChildNodes)
            {
                types.Add(DoExpr(def));
            }

            return types;
        }

        // processes a param list, but forces isolation of params into their own lambda functions
        // More expensive but absolutely required for constructor stuff
        // it also forcibly downgrades to builtin types for first pass-friendliness
        public List<Type> DoIsolatedParamList(ParseTreeNode node, ILGenerator gen)
        {
            List<Type> types = new List<Type>();

            foreach (var def in node.ChildNodes)
            {
                // 2 pass this to get type correctly
                var f = CreateHiddenFunction(new Dictionary<string, LocalInfo>(), typeof(object), true);
                var type = DoExpr(def);

                while (type is TypeBuilder)
                    type = type.BaseType;

                f.IL.Emit(OpCodes.Ret);
                CloseFunction(f, false);
                gen.Emit(OpCodes.Call, f.Method);
                gen.Emit(OpCodes.Castclass, type);

                types.Add(type);
            }

            return types;
        }
    }
}
