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
    public partial class Compiler
    {
        MethodInfo DoClassCall(ParseTreeNode node)
        {
            // get class
            Type classType = DoExpr(node.ChildNodes[0].ChildNodes[0]);

            // box if int,double,etc
            string tname = null;
            if (classType.IsValueType)
            {
                tname = AllocTempVariable(classType);
                StoreVariable(tname);
                CurrentFunction.IL.Emit(OpCodes.Ldloca, CurrentFunction.Locals[tname].Index);
            }

            // method name
            var methodName = (string)node.ChildNodes[0].ChildNodes[2].Token.Value;

            // push params in
            var pars = DoParamList(node.ChildNodes[1]);

            // get method
            MethodInfo mi = null;
            try
            {
                mi = classType.GetMethod(methodName,
                    BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                    null, pars.ToArray(), null);
                if (mi == null)
                    throw new Exception();
            }
            catch
            {
                try
                {
                    mi = classType.BaseType.GetMethod(methodName,
                        BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                        null, pars.ToArray(), null);
                    if (mi == null)
                        throw new Exception();
                }
                catch
                {
                    try
                    {
                        for (int i = 0; i < ClassStack.Count; i++)
                        {
                            ClassInfo c = ClassStack.Skip(i).First();
                            if (c.Class == classType)
                            {
                                FunctionInfo fi = c.Functions[methodName.ToLower()];
                                if (fi.Arguments.Count != pars.Count)
                                    DoError(node, "Invalid number of arguments for " + methodName);

                                for (int j = 0; j < fi.Arguments.Count; j++)
                                {
                                    if (pars[j] != fi.Arguments.Values.Skip(j).First().Type)
                                        DoError(node, methodName + "()'s parameter " + fi.Arguments.Keys.Skip(j).First() + " is wrong type.");
                                }
                                mi = fi.Method;
                                break;
                            }
                            else
                            {
                                foreach (ClassInfo sc in c.SubClasses.Values)
                                {
                                    if (sc.Class == classType)
                                    {
                                        FunctionInfo fi = sc.Functions[methodName.ToLower()];
                                        if (fi.Arguments.Count != pars.Count)
                                            DoError(node, "Invalid number of arguments for " + methodName);

                                        for (int j = 0; j < fi.Arguments.Count; j++)
                                        {
                                            if (pars[j] != fi.Arguments.Values.Skip(j).First().Type)
                                                DoError(node, methodName + "()'s parameter " + fi.Arguments.Keys.Skip(j).First() + " is wrong type.");
                                        }
                                        mi = fi.Method;
                                        break;
                                    }
                                }
                                if (mi != null)
                                    break;
                            }
                        }
                        if (mi == null)
                            throw new Exception();
                    }
                    catch
                    {
                        DoError(node.ChildNodes[0].ChildNodes[2],
                            "Could not find function " + methodName + " in " + classType.Name);
                        mi = null;
                    }
                }
            }

            // call
            if (mi.IsVirtual && !classType.IsValueType)
                CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
            else
                CurrentFunction.IL.Emit(OpCodes.Call, mi);

            if (tname != null)
                FreeTempVariable(tname);

            return mi;
        }

        MethodInfo DoClassStaticCall(ParseTreeNode node)
        {
            // get class
            Type classType = DoType(node.ChildNodes[0].ChildNodes[0]);

            // method name
            var methodName = (string)node.ChildNodes[0].ChildNodes[2].Token.Value;

            // push params in
            var pars = DoParamList(node.ChildNodes[1]);

            // get method
            MethodInfo mi;
            if (classType == CurrentClass.Class)
            {
                throw new Exception("RAE classes cannot have static methods.");
            }
            else
            {
                mi = classType.GetMethod(methodName,
                    BindingFlags.IgnoreCase | BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                    null, pars.ToArray(), null);
            }

            if (mi == null)
            {
                DoError(node.ChildNodes[0].ChildNodes[2], "Static method " + methodName + " not found.");
            }

            // call
            if (mi.IsVirtual)
                CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
            else
                CurrentFunction.IL.Emit(OpCodes.Call, mi);

            return mi;
        }

        MethodInfo DoLocalCall(ParseTreeNode node)
        {
            // get class
            Type classType = CurrentClass.Class;
            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            bool gameStatic = false;

            // method name
            var methodName = (string)node.ChildNodes[0].Token.Value;

            // push params in
            var pars = DoParamList(node.ChildNodes[1]);

            // purify params if some unfinished singletons
            for (int i = 0; i < pars.Count; i++)
            {
                if (pars[i] is TypeBuilder)
                {
                    pars[i] = pars[i].BaseType;
                    i--;
                }
            }

            // get method
            MethodInfo mi;
            if (CurrentClass.Functions.ContainsKey(methodName.ToLower()))
            {
                mi = CurrentClass.Functions[methodName.ToLower()].Method;
            }
            else
            {
                classType = CurrentClass.Class.BaseType;
                mi = classType.GetMethod(methodName,
                    BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy,
                    null, pars.ToArray(), null);
            }
            if (mi == null)
            {
                // try game statics
                classType = typeof(RAEGame);
                mi = classType.GetMethod(methodName,
                    BindingFlags.IgnoreCase | BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                    null, pars.ToArray(), null);
                gameStatic = true;
            }

            if (mi == null)
                DoError(node, "Cannot find method " + methodName);

            // call
            if (mi.IsVirtual)
                CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
            else
                CurrentFunction.IL.Emit(OpCodes.Call, mi);

            if (gameStatic) // pop out the "this" that was never used
            {
                if (mi.ReturnType != typeof(void))
                {
                    string tname = AllocTempVariable(mi.ReturnType);
                    StoreVariable(tname);
                    CurrentFunction.IL.Emit(OpCodes.Pop);
                    LoadVariable(tname);
                    FreeTempVariable(tname);
                }
                else
                {
                    CurrentFunction.IL.Emit(OpCodes.Pop);
                }
            }

            return mi;
        }

        Type LoadField(Type t, string name)
        {
            // search for field
            FieldInfo fi = null;
            try
            {
                fi = GetField(t, name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (fi == null)
                    throw new Exception();
            }
            catch
            {
                try
                {
                    for (int i = 0; i < ClassStack.Count; i++)
                    {
                        ClassInfo c = ClassStack.Skip(i).First();
                        if (c.Class == t)
                        {
                            fi = c.Globals[name.ToLower()].Field;
                            break;
                        }
                        else
                        {
                            foreach (ClassInfo sc in c.SubClasses.Values)
                            {
                                if (sc.Class == t)
                                {
                                    fi = sc.Globals[name.ToLower()].Field;
                                    break;
                                }
                            }
                            if (fi != null)
                                break;
                        }
                    }
                }
                catch
                {
                    fi = null;
                }
            }

            // the catch is that if it's private, we must actually be in that class
            if (fi != null && fi.IsPrivate && t != ClassStack.First().Class)
            {
                throw new CompileException(t.Name + "." + name + " cannot be accessed here, as it is private.");
            }

            if (fi != null)
            {
                //Console.Write("FIELD(" + t.Name + "." + name + ") " + fi + ": ");
                // a field
                CurrentFunction.IL.Emit(fi.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, fi);
                //Console.WriteLine(" LOADED");
                return fi.FieldType;
            }
            else
            {
                // maybe a property?
                PropertyInfo pi;
                if (t is TypeBuilder)
                    t = t.BaseType;
                var gt = t.IsGenericType ? t.GetGenericTypeDefinition() : t;

                pi = gt.GetProperty(name,
                    BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Static);
                if (pi == null || !pi.CanRead)
                    throw new CompileException(t.Name + "." + name + " cannot be read.");

                MethodInfo mi = GetGetMethod(t, pi);
                CurrentFunction.IL.Emit(OpCodes.Call, mi);
                return mi.ReturnType;
            }
        }

        Type GetFieldType(Type t, string name, out bool isStatic)
        {
            // search for field
            FieldInfo fi = null;
            try
            {
                fi = GetField(t, name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (fi == null)
                    throw new Exception();
            }
            catch
            {
                try
                {
                    fi = GetField(t.BaseType, name,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    if (fi == null)
                        throw new Exception();
                }
                catch
                {
                    try
                    {
                        for (int i = 0; i < ClassStack.Count; i++)
                        {
                            ClassInfo c = ClassStack.Skip(i).First();
                            if (c.Class == t)
                            {
                                fi = c.Globals[name.ToLower()].Field;
                                break;
                            }
                            else
                            {
                                foreach (ClassInfo sc in c.SubClasses.Values)
                                {
                                    if (sc.Class == t)
                                    {
                                        fi = sc.Globals[name.ToLower()].Field;
                                        break;
                                    }
                                }
                                if (fi != null)
                                    break;
                            }
                        }
                    }
                    catch
                    {
                        fi = null;
                    }
                }
            }

            if (fi != null)
            {
                // a field
                isStatic = fi.IsStatic;
                return fi.FieldType;
            }
            else
            {
                // maybe a property?
                if (t is TypeBuilder)
                    t = t.BaseType;
                var gt = t.IsGenericType ?
                    t.GetGenericTypeDefinition() : t;

                var pi = gt.GetProperty(name,
                    BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Static);
                if (pi == null || !pi.CanRead)
                    throw new CompileException("" + name + " cannot be read.");

                MethodInfo mi = GetGetMethod(t, pi);
                isStatic = mi.IsStatic;
                return mi.ReturnType;
            }
        }

        Type DoClassFieldRead(ParseTreeNode node)
        {
            // get class
            Type classType = DoExpr(node.ChildNodes[0]);

            // box if int,double,etc
            string tname = null;
            if (classType.IsValueType)
            {
                tname = AllocTempVariable(classType);
                StoreVariable(tname);
                CurrentFunction.IL.Emit(OpCodes.Ldloca, CurrentFunction.Locals[tname].Index);
            }

            // field name
            var fieldName = (string)node.ChildNodes[2].Token.Value;

            Type rt;
            try
            {
                rt = LoadField(classType, fieldName);
            }
            catch (CompileException e)
            {
                DoError(node, e.Message);
                rt = typeof(void);
            }
            if (tname != null)
                FreeTempVariable(tname);
            return rt;
        }

        Type DoClassStaticFieldRead(ParseTreeNode node)
        {
            // get class
            Type classType = DoType(node.ChildNodes[0]);

            // field name
            var fieldName = (string)node.ChildNodes[2].Token.Value;

            // search for field
            FieldInfo fi = classType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Static);

            if (fi != null)
            {
                // a field
                CurrentFunction.IL.Emit(OpCodes.Ldsfld, fi);
                return fi.FieldType;
            }
            else
            {
                // maybe a property?
                MethodInfo mi;
                if (classType is TypeBuilder)
                {
                    if (fieldName == "Instance")
                        mi = GetNestedClass(classType.Name).GetInstance;
                    else
                        mi = GetNestedClass(classType.Name).Functions[fieldName].Method;
                }
                else
                {
                    PropertyInfo pi = classType.GetProperty(fieldName,
                        BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Static);

                    if (pi == null || !pi.CanRead)
                        throw new CompileException("" + pi + " cannot be read.");

                    mi = pi.GetGetMethod();
                }
                CurrentFunction.IL.Emit(OpCodes.Call, mi);
                return mi.ReturnType;
            }
        }

        Type DoClassFieldWriteObject(ParseTreeNode node, out string tname)
        {
            // get class
            Type classType = DoExpr(node.ChildNodes[0]);

            // box if int,double,etc
            if (classType.IsValueType)
            {
                tname = AllocTempVariable(classType);
                StoreVariable(tname);
                CurrentFunction.IL.Emit(OpCodes.Ldloca, CurrentFunction.Locals[tname].Index);
            }
            else
                tname = null;

            return classType;
        }

        void StoreFieldFinish(Type classType, string fieldName)
        {
            // search for field
            FieldInfo fi = null;
            try
            {
                fi = GetField(classType, fieldName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (fi == null)
                    throw new Exception();
            }
            catch
            {
                try
                {
                    fi = GetField(classType.BaseType, fieldName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    if (fi == null)
                        throw new Exception();
                }
                catch
                {
                    try
                    {
                        for (int i = 0; i < ClassStack.Count; i++)
                        {
                            ClassInfo c = ClassStack.Skip(i).First();
                            if (c.Class == classType)
                            {
                                fi = c.Globals[fieldName.ToLower()].Field;
                                break;
                            }
                            else
                            {
                                foreach (ClassInfo sc in c.SubClasses.Values)
                                {
                                    if (sc.Class == classType)
                                    {
                                        fi = sc.Globals[fieldName.ToLower()].Field;
                                        break;
                                    }
                                }
                                if (fi != null)
                                    break;
                            }
                        }
                    }
                    catch
                    {
                        fi = null;
                    }
                }
            }
            if (fi != null)
            {
                // a field
                CurrentFunction.IL.Emit(fi.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, fi);
            }
            else
            {
                // maybe a property?
                if (classType is TypeBuilder)
                    classType = classType.BaseType;

                var gt = classType.IsGenericType ?
                    classType.GetGenericTypeDefinition() : classType;

                var pi = gt.GetProperty(fieldName,
                    BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Static);
                if (pi == null || !pi.CanWrite)
                    throw new CompileException("" + pi + " cannot be written.");
                MethodInfo mi = GetSetMethod(classType, pi);
                CurrentFunction.IL.Emit(OpCodes.Call, mi);
            }
        }

        void DoClassFieldWriteFinish(ParseTreeNode node, Type classType, string tname)
        {
            // field name
            var fieldName = (string)node.ChildNodes[2].Token.Value;

            try
            {
                StoreFieldFinish(classType, fieldName);
            }
            catch (Exception e)
            {
                DoError(node, e.Message);
            }

            if (tname != null)
                FreeTempVariable(tname);
        }

        Type DoObjectCommand(ParseTreeNode node, bool topStatement)
        {
            string op = node.ChildNodes[1].ChildNodes[0].Term.Name;
            Type t = DoExpr(node.ChildNodes[0]);
            var pars = DoParamList(node.ChildNodes[1].ChildNodes[1]);

            if (t.IsSubclassOf(typeof(Verbable)) || t == typeof(Verbable))
            {
                try
                {
                    return DoVerbableOp(t, op, pars.ToArray(), topStatement);
                }
                catch (CompileException e)
                {
                    DoError(node, e.Message);
                    return null;
                }
            }
            else if (t.IsArray || t.GetInterface("IList") != null)
            {
                try
                {
                    return DoCollectionOp(t, op, pars.ToArray(), topStatement);
                }
                catch (CompileException e)
                {
                    DoError(node, e.Message);
                    return null;
                }
            }
            else
            {
                DoError(node, "Object command " + op
                    + " must operate on a subtype of Verbable, IList, or be an array, not " + t.Name);
                return null;
            }
        }

        Type DoThisObjectCommand(ParseTreeNode node, bool topStatement)
        {
            string op = node.ChildNodes[0].Term.Name;
            Type t;

            switch (op)
            {
                case "GIVE":    // these ops default to player
                case "TAKE":
                case "GOTO":
                case "HAS":
                case "!HAS":
                    CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                    if (!CurrentClass.Class.IsSubclassOf(typeof(RAEGame)))
                    {
                        CurrentFunction.IL.Emit(OpCodes.Call, typeof(Verbable)
                            .GetProperty("Game").GetGetMethod());
                    }
                    CurrentFunction.IL.Emit(OpCodes.Call, typeof(RAEGame)
                        .GetProperty("Player").GetGetMethod());
                    t = typeof(Player);
                    break;
                default:
                    if (!CurrentClass.Class.IsSubclassOf(typeof(Verbable)))
                        DoError(node, "The class this is contained in is not a verbable.");
                    CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                    t = typeof(Verbable);
                    break;
            }

            var pars = DoParamList(node.ChildNodes[1]);

            try
            {
                return DoVerbableOp(t, op, pars.ToArray(), topStatement);
            }
            catch (CompileException e)
            {
                DoError(node, e.Message);
                return null;
            }
        }

        Type DoCollectionOp(Type atype, string op, Type[] pars, bool topStatement)
        {
            switch (op)
            {
                case "GIVE":
                    // add item to list
                    if (atype.GetInterface("ICollection`1") != null)
                    {
                        if (!topStatement)
                            break;
                        Type etype = atype.GetInterface("ICollection`1").GetGenericArguments()[0];
                        MethodInfo mi = atype.GetMethod("Add", new Type[] { etype });

                        if (pars.Length > 1)
                        {
                            // effectively translates {a give b} -> { for(int i = 0; i < b.length; i++) a.Add(b); }
                            Label loopl = CurrentFunction.IL.DefineLabel();
                            Label loope = CurrentFunction.IL.DefineLabel();
                            // b = {b}
                            Type t = CreateArray(etype, pars);
                            string bvar = AllocTempVariable(t);
                            StoreVariable(bvar);
                            // int i = 0
                            string ivar = AllocTempVariable(typeof(int));
                            LoadConstant(0);
                            StoreVariable(ivar);
                            // {
                            CurrentFunction.IL.MarkLabel(loopl);
                            // i < b.length
                            LoadVariable(ivar);
                            LoadConstant(pars.Length);
                            CurrentFunction.IL.Emit(OpCodes.Clt);
                            CurrentFunction.IL.Emit(OpCodes.Brfalse, loope);
                            // a.Add(b[i]);
                            CurrentFunction.IL.Emit(OpCodes.Dup);
                            LoadVariable(bvar);
                            LoadVariable(ivar);
                            CurrentFunction.IL.Emit(OpCodes.Ldelem, etype);
                            CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
                            // i++
                            LoadVariable(ivar);
                            CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1);
                            CurrentFunction.IL.Emit(OpCodes.Add);
                            StoreVariable(ivar);
                            // }
                            CurrentFunction.IL.Emit(OpCodes.Br, loopl);
                            CurrentFunction.IL.MarkLabel(loope);
                            // remove a on stack
                            CurrentFunction.IL.Emit(OpCodes.Pop);

                            FreeTempVariable(bvar);
                            FreeTempVariable(ivar);
                        }
                        else
                        {
                            // if there's only 1 item, much simpler. just
                            // {a give b} -> {a.Add(b)}

                            CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
                        }
                        return typeof(void);
                    }
                    else
                    {
                        throw new CompileException("Only lists can have items added using give");
                    }
                case "TAKE":
                    // remove item from list
                    if (atype.GetInterface("ICollection`1") != null)
                    {
                        if (!topStatement)
                            break;
                        Type etype = atype.GetInterface("ICollection`1").GetGenericArguments()[0];
                        MethodInfo mi = atype.GetMethod("Remove", new Type[] { etype });

                        if (pars.Length > 1)
                        {
                            // effectively translates {a take b} -> { for(int i = 0; i < b.length; i++) a.Remove(b); }
                            Label loopl = CurrentFunction.IL.DefineLabel();
                            Label loope = CurrentFunction.IL.DefineLabel();
                            // b = {b}
                            Type t = CreateArray(etype, pars);
                            string bvar = AllocTempVariable(t);
                            StoreVariable(bvar);
                            // int i = 0
                            string ivar = AllocTempVariable(typeof(int));
                            LoadConstant(0);
                            StoreVariable(ivar);
                            // {
                            CurrentFunction.IL.MarkLabel(loopl);
                            // i < b.length
                            LoadVariable(ivar);
                            LoadConstant(pars.Length);
                            CurrentFunction.IL.Emit(OpCodes.Clt);
                            CurrentFunction.IL.Emit(OpCodes.Brfalse, loope);
                            // a.Add(b[i]);
                            CurrentFunction.IL.Emit(OpCodes.Dup);
                            LoadVariable(bvar);
                            LoadVariable(ivar);
                            CurrentFunction.IL.Emit(OpCodes.Ldelem, etype);
                            CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
                            CurrentFunction.IL.Emit(OpCodes.Pop);
                            // i++
                            LoadVariable(ivar);
                            CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1);
                            CurrentFunction.IL.Emit(OpCodes.Add);
                            StoreVariable(ivar);
                            // }
                            CurrentFunction.IL.Emit(OpCodes.Br, loopl);
                            CurrentFunction.IL.MarkLabel(loope);
                            // remove a on stack
                            CurrentFunction.IL.Emit(OpCodes.Pop);

                            FreeTempVariable(bvar);
                            FreeTempVariable(ivar);
                        }
                        else
                        {
                            // if there's only 1 item, much simpler. just
                            // {a take b} -> {a.Remove(b)}

                            CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
                        }
                        return typeof(void);
                    }
                    else
                    {
                        throw new CompileException("Only lists can have items removed using take");
                    }
                case "HAS":
                    if (topStatement)
                        throw new CompileException(op + " expects to be used in an expression.");

                    if (pars.Length != 1)
                        throw new Exception("Has can only receive one argument of the contained type.");

                    if (atype.IsArray)
                    {
                        MethodInfo mi = typeof(Array).GetMethod("IndexOf", new Type[] { atype, atype.GetElementType() });
                        CurrentFunction.IL.Emit(OpCodes.Call, mi);
                        CurrentFunction.IL.Emit(OpCodes.Ldc_I4_0);
                        CurrentFunction.IL.Emit(OpCodes.Clt);
                        DoNotOp(typeof(int));
                        return typeof(bool);
                    }
                    else if (atype.GetInterface("ICollection`1") != null)
                    {
                        Type etype = atype.GetInterface("ICollection`1").GetGenericArguments()[0];
                        MethodInfo mi = atype.GetMethod("Contains", new Type[] { etype });
                        CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
                        return typeof(bool);
                    }
                    else
                    {
                        throw new CompileException("Only arrays and ILists can use has this way.");
                    }
                case "!HAS":
                    if (topStatement)
                        throw new CompileException(op + " expects to be used in an expression.");

                    if (pars.Length != 1)
                        throw new Exception("!Has can only receive one argument of the contained type.");

                    if (atype.IsArray)
                    {
                        MethodInfo mi = typeof(Array).GetMethod("IndexOf", new Type[] { atype, atype.GetElementType() });
                        CurrentFunction.IL.Emit(OpCodes.Call, mi);
                        CurrentFunction.IL.Emit(OpCodes.Ldc_I4_0);
                        CurrentFunction.IL.Emit(OpCodes.Clt);
                        return typeof(bool);
                    }
                    else if (atype.GetInterface("ICollection`1") != null)
                    {
                        Type etype = atype.GetInterface("ICollection`1").GetGenericArguments()[0];
                        MethodInfo mi = atype.GetMethod("Contains", new Type[] { etype });
                        CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);
                        DoNotOp(typeof(bool));
                        return typeof(bool);
                    }
                    else
                    {
                        throw new CompileException("Only arrays and ILists can use !has this way.");
                    }
                default:
                    throw new CompileException("Unknown collection command: " + op);
            }

            throw new CompileException(op + " returns void and thus expects to be used outside of an expression.");
        }

        Type DoVerbableOp(Type atype, string op, Type[] pars, bool topStatement)
        {
            switch (op)
            {
                case "AKA":
                    if (!topStatement)
                        break;
                    Type t = CreateArray<string>(pars);
                    string aname = AllocTempVariable(t);
                    StoreVariable(aname);
                    var mi = atype.GetProperty("AKA").GetGetMethod();
                    CurrentFunction.IL.Emit(OpCodes.Call, mi);
                    LoadVariable(aname);
                    FreeTempVariable(aname);
                    mi = mi.ReturnType.GetMethod("AddRange");
                    CurrentFunction.IL.Emit(OpCodes.Call, mi);
                    return typeof(void);
                case "GOTO":
                    if (pars.Length != 1 || !pars[0].IsSubclassOf(typeof(Room)))
                        throw new CompileException("Goto can only receive one Room argument.");

                    if (!topStatement)
                        CurrentFunction.IL.Emit(OpCodes.Dup); // return the room

                    mi = typeof(Verbable).GetProperty("Location").GetSetMethod();
                    CurrentFunction.IL.Emit(OpCodes.Callvirt, mi);

                    if (topStatement)
                        return typeof(void);
                    else
                        return pars[0];
                case "GIVE":
                    if (!topStatement)
                        break;
                    t = CreateArray<Verbable>(pars);
                    mi = typeof(Verbable).GetMethod("Give");
                    CurrentFunction.IL.Emit(OpCodes.Call, mi);
                    return typeof(void);
                case "TAKE":
                    if (!topStatement)
                        break;
                    t = CreateArray<Verbable>(pars);
                    mi = typeof(Verbable).GetMethod("Take");
                    CurrentFunction.IL.Emit(OpCodes.Call, mi);
                    return typeof(void);
                case "HAS":
                    if (topStatement)
                        throw new CompileException(op + " expects to be used in an expression.");

                    if (pars.Length != 1 || !pars[0].IsSubclassOf(typeof(Verbable)))
                        throw new Exception("Has can only receive one Verbable argument.");

                    string tname = AllocTempVariable(pars[0]);
                    StoreVariable(tname);
                    mi = typeof(Verbable).GetProperty("Contents").GetGetMethod();
                    MethodInfo cmi = mi.ReturnType.GetMethod("Contains");
                    CurrentFunction.IL.Emit(OpCodes.Call, mi);
                    LoadVariable(tname);
                    FreeTempVariable(tname);
                    CurrentFunction.IL.Emit(OpCodes.Call, cmi);

                    return typeof(bool);
                case "!HAS":
                    if (topStatement)
                        throw new CompileException(op + " expects to be used in an expression.");

                    if (pars.Length != 1 || !pars[0].IsSubclassOf(typeof(Verbable)))
                        throw new Exception("!Has can only receive one Verbable argument.");

                    tname = AllocTempVariable(pars[0]);
                    StoreVariable(tname);
                    mi = typeof(Verbable).GetProperty("Contents").GetGetMethod();
                    cmi = mi.ReturnType.GetMethod("Contains");
                    CurrentFunction.IL.Emit(OpCodes.Call, mi);
                    LoadVariable(tname);
                    FreeTempVariable(tname);
                    CurrentFunction.IL.Emit(OpCodes.Call, cmi);
                    DoNotOp(typeof(bool));

                    return typeof(bool);
                default:
                    throw new CompileException("Unknown verbable command: " + op);
            }

            throw new CompileException(op + " returns void and thus expects to be used outside of an expression.");
        }

        Type DoThisObjectCommandParID(ParseTreeNode node, bool topStatement)
        {
            string op = node.ChildNodes[0].Term.Name;
            string par = node.ChildNodes[1].Token.Text;

            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            Type t = CurrentClass.Class;

            try
            {
                return DoVerbableOpParID(node, t, op, par, topStatement);
            }
            catch (CompileException e)
            {
                DoError(node, e.Message);
                return null;
            }
        }

        Type DoObjectCommandParID(ParseTreeNode node, bool topStatement)
        {
            string op = node.ChildNodes[1].ChildNodes[0].Term.Name;
            Type t = DoExpr(node.ChildNodes[0]);
            if (t != typeof(Verbable) && !t.IsSubclassOf(typeof(Verbable)))
                DoError(node, "Object command " + op
                    + " must operate on a subtype of Verbable, not " + t.Name);
            var pars = node.ChildNodes[1].ChildNodes[1].Token.Text;

            try
            {
                return DoVerbableOpParID(node.ChildNodes[1], t, op, pars, topStatement);
            }
            catch (CompileException e)
            {
                DoError(node, e.Message);
                return null;
            }
        }

        Type DoVerbableOpParID(ParseTreeNode node, Type atype, string op, string par, bool topStatement)
        {
            Type parType;
            switch (op)
            {
                case "DO":
                    if (!topStatement)
                        break;
                    string tname = AllocTempVariable(atype);
                    StoreVariable(tname);

                    CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                    if (!CurrentClass.Class.IsSubclassOf(typeof(RAEGame)))
                        CurrentFunction.IL.Emit(OpCodes.Call,
                            typeof(Verbable).GetProperty("Game").GetGetMethod());
                    LoadVariable(tname);
                    FreeTempVariable(tname);
                    LoadConstant(par);
                    CurrentFunction.IL.Emit(OpCodes.Ldnull);
                    CurrentFunction.IL.Emit(OpCodes.Call,
                        typeof(RAEGame).GetMethod("TryVerb",
                        new Type[] { typeof(Verbable), typeof(string), typeof(string[]) }));
                    CurrentFunction.IL.Emit(OpCodes.Pop);
                    return typeof(void);
                case "TO":
                    if (!topStatement)
                        break;
                    CurrentFunction.IL.Emit(OpCodes.Dup);
                    //CurrentFunction.IL.EmitWriteLine(atype.Name + " TO " + par);
                    parType = LoadField(atype, par);
                    if (parType != typeof(State))
                        DoError(node.ChildNodes[1], par + " is not a recognized state for " + atype.Name);
                    CurrentFunction.IL.Emit(OpCodes.Call,
                        typeof(Verbable).GetProperty("CurrentState").GetSetMethod());
                    return typeof(void);
                case "IS":
                    if (topStatement)
                        throw new CompileException(op + " expects to be used in an expression.");

                    CurrentFunction.IL.Emit(OpCodes.Dup);
                    parType = LoadField(atype, par);
                    if (parType != typeof(State))
                        DoError(node.ChildNodes[1], par + " is not a recognized state for " + atype.Name);
                    string tmp = AllocTempVariable(parType);
                    StoreVariable(tmp);
                    CurrentFunction.IL.Emit(OpCodes.Call,
                        typeof(Verbable).GetProperty("CurrentState").GetGetMethod());
                    LoadVariable(tmp);
                    FreeTempVariable(tmp);
                    CurrentFunction.IL.Emit(OpCodes.Ceq);

                    return typeof(bool);
                case "ISNT":
                    if (topStatement)
                        throw new CompileException(op + " expects to be used in an expression.");

                    CurrentFunction.IL.Emit(OpCodes.Dup);
                    parType = LoadField(atype, par);
                    if (parType != typeof(State))
                        DoError(node.ChildNodes[1], par + " is not a recognized state for " + atype.Name);
                    tmp = AllocTempVariable(parType);
                    StoreVariable(tmp);
                    CurrentFunction.IL.Emit(OpCodes.Call,
                        typeof(Verbable).GetProperty("CurrentState").GetGetMethod());
                    LoadVariable(tmp);
                    FreeTempVariable(tmp);
                    CurrentFunction.IL.Emit(OpCodes.Ceq);
                    DoNotOp(typeof(bool));

                    return typeof(bool);
            }

            throw new CompileException(op + " returns void and thus expects to be used outside of an expression.");
        }

        Type DoThenCommand(ParseTreeNode node, bool topStatement)
        {
            Type gt = DoGameConstant();
            LoadConstant(node.GetHashCode());
            CurrentFunction.IL.Emit(OpCodes.Call, typeof(RAEGame).GetMethod("CheckThen"));

            Label tl = CurrentFunction.IL.DefineLabel();
            Label el = CurrentFunction.IL.DefineLabel();

            CurrentFunction.IL.Emit(OpCodes.Brtrue, tl);
            Type t1 = DoExpr(node.ChildNodes[0]);
            CurrentFunction.IL.Emit(OpCodes.Br_S, el);

            CurrentFunction.IL.MarkLabel(tl);
            Type t2 = DoExpr(node.ChildNodes[2]);

            CurrentFunction.IL.MarkLabel(el);

            return UpgradeValueTypes(t1, t2);
        }

        // non-uniform type params
        Type CreateArray<elemType>(Type[] types)
        {
            return CreateArray(typeof(elemType), types);
        }

        Type CreateArray(Type elemType, Type[] types)
        {
            Type atype = Type.GetType(elemType.FullName + "[]");

            LoadConstant(types.Length);
            CurrentFunction.IL.Emit(OpCodes.Newarr, elemType);
            string aname = AllocTempVariable(atype);
            StoreVariable(aname);
            string ename = AllocTempVariable(elemType);

            for (int i = types.Length - 1; i >= 0; i--)
            {
                ConvertType(types[i], elemType);
                StoreVariable(ename);
                LoadVariable(aname);
                LoadConstant(i);
                LoadVariable(ename);
                CurrentFunction.IL.Emit(OpCodes.Stelem, elemType);
            }

            LoadVariable(aname);
            FreeTempVariable(ename);
            FreeTempVariable(aname);

            return atype;
        }

        // quicker one for uniformly typed inputs
        Type CreateArray<elemType>(int count)
        {
            LoadConstant(count);
            CurrentFunction.IL.Emit(OpCodes.Newarr, typeof(elemType));
            string aname = AllocTempVariable(typeof(elemType[]));
            StoreVariable(aname);
            string ename = AllocTempVariable(typeof(elemType));

            for (int i = count - 1; i >= 0; i--)
            {
                StoreVariable(ename);
                LoadVariable(aname);
                LoadConstant(i);
                LoadVariable(ename);
                CurrentFunction.IL.Emit(OpCodes.Stelem, typeof(elemType));
            }
            LoadVariable(aname);
            FreeTempVariable(ename);
            FreeTempVariable(aname);

            return typeof(elemType[]);
        }
    }
}
