using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony;
using Irony.Parsing;

using System.Reflection;
using System.Reflection.Emit;

using RAE.Game;

namespace RAE
{
    public partial class Compiler
    {
        StatementInfo DoReturn(ParseTreeNode node)
        {
            if (node.ChildNodes[1].ChildNodes.Count == 0)
            {
                // code returning void
                if (CurrentFunction.Method.ReturnType != typeof(void))
                    DoError(node.ChildNodes[1], "Function " + CurrentFunction.Method.Name
                        + " expects " + CurrentFunction.Method.ReturnType.Name + " on return.");
            }
            else
            {
                if (CurrentFunction.Method.ReturnType == typeof(void))
                    DoError(node.ChildNodes[1], "Void function cannot return value.");

                // code returning result of expression
                Type t = DoExpr(node.ChildNodes[1].ChildNodes[0]);

                ConvertType(t, CurrentFunction.Method.ReturnType);
            }

            // a ret in a protected block, ie try/catch is illegal, so must leave to a messy ret outside block
            if (TryScopes.Count == 0)
            {
                CurrentFunction.IL.Emit(OpCodes.Ret);
            }
            else
            {
                var info = TryScopes.Peek();
                CurrentFunction.IL.Emit(OpCodes.Leave, info.ReturnLabel);
                info.LabelUsed = true;
            }
            return new StatementInfo() { Returns = true };
        }

        StatementInfo DoBreak(ParseTreeNode node)
        {
            if (BreakScopes.Count == 0)
                DoError(node, "Cannot logically break here.");

            CurrentFunction.IL.Emit(OpCodes.Br, BreakScopes.Peek());
            return new StatementInfo() { Breaks = true };
        }

        /// <summary>
        /// Reliably get a field
        /// </summary>
        /// <param name="t">Class type</param>
        /// <param name="name">Name of field</param>
        /// <returns>The corresponding MethodInfo</returns>
        FieldInfo GetField(Type t, string name)
        {
            if (t is TypeBuilder)
                t = t.BaseType;

            if (!t.IsGenericType)
            {
                return t.GetField(name, BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            }
            else
            {
                var fi = t.GetGenericTypeDefinition().GetField(
                        name, BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

                if (fi == null)
                    return null;

                try
                {

                    // a generic type builder method
                    return TypeBuilder.GetField(t, fi);
                }
                catch (ArgumentException e)
                {
                    // not actually of a generic type
                    if (e.ParamName == "field")
                        return fi;
                    else
                        throw;
                }
            }
        }

        /// <summary>
        /// Reliably get a field
        /// </summary>
        /// <param name="t">Class type</param>
        /// <param name="name">Name of field</param>
        /// <param name="flags">Extra binding flags</param>
        /// <returns>The corresponding MethodInfo</returns>
        FieldInfo GetField(Type t, string name, BindingFlags flags)
        {
            if (t is TypeBuilder)
                t = t.BaseType;

            if (!t.IsGenericType)
            {
                return t.GetField(name, BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | flags);
            }
            else
            {
                var fi = t.GetGenericTypeDefinition().GetField(
                        name, BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | flags);

                if (fi == null)
                    return null;

                try
                {

                    // a generic type builder method
                    return TypeBuilder.GetField(t, fi);
                }
                catch (ArgumentException e)
                {
                    // not actually of a generic type
                    if (e.ParamName == "field")
                        return fi;
                    else
                        throw;
                }
            }
        }

        /// <summary>
        /// Reliably get a method
        /// </summary>
        /// <param name="t">Class type</param>
        /// <param name="name">Name of method</param>
        /// <returns>The corresponding MethodInfo</returns>
        MethodInfo GetMethod(Type t, string name)
        {
            if (t is TypeBuilder)
                t = t.BaseType;

            if (!t.IsGenericType)
            {
                return t.GetMethod(name, BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            }
            else
            {
                try
                {
                    // a generic type builder method
                    var mi = t.GetGenericTypeDefinition().GetMethod(
                        name, BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

                    return mi.ContainsGenericParameters ?
                        TypeBuilder.GetMethod(t, mi) : mi;
                }
                catch (NullReferenceException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Reliably get a method
        /// </summary>
        /// <param name="t">Class type</param>
        /// <param name="name">Name of method</param>
        /// <param name="flags">Extra binding flags</param>
        /// <param name="returnType">Return type of method</param>
        /// <param name="types">Parameter types</param>
        /// <returns>The corresponding MethodInfo</returns>
        MethodInfo GetMethod(Type t, string name, BindingFlags flags, Type[] types)
        {
            if (t is TypeBuilder)
                t = t.BaseType;

            if (!t.IsGenericType)
            {
                return t.GetMethod(name, BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase | flags, null, types, null);
            }
            else
            {
                try
                {
                    // a generic type builder method
                    var mi = t.GetGenericTypeDefinition().GetMethod(
                        name, BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase | flags, null, types, null);

                    return mi.ContainsGenericParameters ?
                        TypeBuilder.GetMethod(t, mi) : mi;
                }
                catch (NullReferenceException)
                {
                    return null;
                }
            }
        }

        MethodInfo GetGetMethod(Type t, PropertyInfo pi)
        {
            if (!t.IsGenericType)
                return pi.GetGetMethod();
            else
                try
                {
                    var mi = t.GetGenericTypeDefinition()
                        .GetProperty(pi.Name).GetGetMethod();

                    return !mi.ContainsGenericParameters ? mi
                        : t is TypeBuilder ? TypeBuilder.GetMethod(t, mi) // TypeBuilders always have to be snowflakes
                        : mi.IsGenericMethodDefinition ? mi.MakeGenericMethod(t.GetGenericArguments()) // method has generics
                        : t.GetGenericArguments().Any(a=>a is TypeBuilder) ? TypeBuilder.GetMethod(t, pi.GetGetMethod()) // type has generic type who is TypeBuilder
                        : t.GetProperty(pi.Name).GetGetMethod(); // this is actually a non-generic method in a generic type. Blugh!
                }
                catch (NullReferenceException)
                {
                    return null;
                }
        }

        MethodInfo GetSetMethod(Type t, PropertyInfo pi)
        {
            if (!t.IsGenericType || !(t is TypeBuilder))
                return pi.GetSetMethod();
            else
                try
                {
                    var mi = t.GetGenericTypeDefinition()
                        .GetProperty(pi.Name).GetSetMethod();

                    return mi.ContainsGenericParameters ?
                        TypeBuilder.GetMethod(t, mi) : mi;
                }
                catch (NullReferenceException)
                {
                    return null;
                }
        }

        bool IsSubclassOf(Type t, Type parent)
        {
            if (t.IsGenericType)
                t = t.GetGenericTypeDefinition();

            if (t is TypeBuilder)
                t = t.BaseType;

            return t.IsSubclassOf(parent) || t == parent;
        }

        void ConvertType(Type src, Type dest)
        {
            if (src == dest)
                return;

            if (src.IsPrimitive && dest.IsPrimitive)
            {
                switch (dest.Name)
                {
                    case "Int32":
                    case "Boolean":
                        if (src != typeof(int) && src != typeof(bool))
                            CurrentFunction.IL.Emit(OpCodes.Conv_I4);
                        break;
                    case "Double":
                        CurrentFunction.IL.Emit(OpCodes.Conv_R8);
                        break;
                }
            }
            else if (src == typeof(string) && dest.IsEnum)
            {
                string tname = AllocTempVariable(src);
                StoreVariable(tname);
                CurrentFunction.IL.Emit(OpCodes.Ldtoken, dest);
                CurrentFunction.IL.Emit(OpCodes.Call,
                    typeof(Type).GetMethod("GetTypeFromHandle"));
                LoadVariable(tname);
                FreeTempVariable(tname);
                CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1);
                CurrentFunction.IL.Emit(OpCodes.Call,
                    typeof(Enum).GetMethod("Parse",
                    new Type[] { typeof(Type), src, typeof(bool) }));
                CurrentFunction.IL.Emit(OpCodes.Unbox_Any, dest);
            }
            else
            {
                string tname = null;
                if (src.IsValueType)
                {
                    tname = AllocTempVariable(src, true);
                    StoreVariable(tname);
                    CurrentFunction.IL.Emit(OpCodes.Ldloca, CurrentFunction.Locals[tname].Index);
                }
                if (src != dest)
                {
                    if (dest == typeof(string))
                    {
                        MethodInfo mi;

                        mi = GetMethod(src, "ToString", BindingFlags.Public | BindingFlags.Instance, new Type[0]);
                        if (mi == null || mi.GetParameters().Length != 0)
                            throw new CompileException(src.Name + " cannot be converted to a string.");
                        CurrentFunction.IL.Emit(mi.IsVirtual && !src.IsValueType ? OpCodes.Callvirt : OpCodes.Call,
                            mi);
                    }
                    else
                        CurrentFunction.IL.Emit(OpCodes.Castclass, dest);
                }
                if (src.IsValueType)
                {
                    FreeTempVariable(tname);
                }
            }
        }

        // if top statement true, it should only return if dostatement expects to handle it
        // currently that means it's a string
        Type DoExpr(ParseTreeNode node, bool topStatement = false)
        {
            Type t;
            switch (node.Term.Name)
            {
                case "INT NUMBER":
                    if (topStatement)
                        break;
                    LoadConstant((int)node.Token.Value);
                    return typeof(int);
                case "FLOAT NUMBER":
                    if (topStatement)
                        break;
                    LoadConstant((double)node.Token.Value);
                    return typeof(double);
                case "ID":
                    try
                    {
                        t = LoadVariable((string)node.Token.Value);
                        if (topStatement && t != typeof(string))
                            break;
                        return t;
                    }
                    catch (CompileException e)
                    {
                        DoError(node, e.Message);
                        throw;
                    }
                case "CONSTANTS":
                    if (topStatement)
                        break;
                    switch (node.ChildNodes[0].Term.Name)
                    {
                        case "NULL":
                            CurrentFunction.IL.Emit(OpCodes.Ldnull);
                            return typeof(object);
                        case "TRUE":
                            CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1);
                            return typeof(bool);
                        case "FALSE":
                            CurrentFunction.IL.Emit(OpCodes.Ldc_I4_0);
                            return typeof(bool);
                        case "THIS":
                            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                            return CurrentClass.Class;
                        case "GAME":
                            return DoGameConstant();
                        case "PLAYER":
                            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
                            if (!CurrentClass.Class.IsSubclassOf(typeof(RAEGame)))
                            {
                                CurrentFunction.IL.Emit(OpCodes.Call, typeof(Verbable)
                                    .GetProperty("Game").GetGetMethod());
                            }
                            CurrentFunction.IL.Emit(OpCodes.Call, typeof(RAEGame)
                                .GetProperty("Player").GetGetMethod());
                            return typeof(Player);
                        default:
                            throw new Exception("Unknown builtin constant: " + node.ChildNodes[0].Term.Name);
                    }
                case "STRING":
                    LoadConstant((string)node.Token.Value);
                    return typeof(string);
                case "CLASS CALL":
                    t = DoClassCall(node).ReturnType;
                    if (topStatement && t != typeof(void) && t != typeof(string))
                    {
                        CurrentFunction.IL.Emit(OpCodes.Pop);
                        t = typeof(void);
                    }
                    else if (!topStatement && t == typeof(void))
                    {
                        DoError(node, "Function returns void and thus cannot be used in expressions.");
                    }
                    return t;
                case "CLASS STATIC CALL":
                    t = DoClassStaticCall(node).ReturnType;
                    if (topStatement && t != typeof(void) && t != typeof(string))
                    {
                        CurrentFunction.IL.Emit(OpCodes.Pop);
                        t = typeof(void);
                    }
                    else if (!topStatement && t == typeof(void))
                    {
                        DoError(node, "Function returns void and thus cannot be used in expressions.");
                    }
                    return t;
                case "LOCAL CALL":
                    t = DoLocalCall(node).ReturnType;
                    if (topStatement && t != typeof(void) && t != typeof(string))
                    {
                        CurrentFunction.IL.Emit(OpCodes.Pop);
                        t = typeof(void);
                    }
                    else if (!topStatement && t == typeof(void))
                    {
                        DoError(node, "Function returns void and thus cannot be used in expressions.");
                    }
                    return t;
                case "CLASS FIELD":
                    if (topStatement)
                        break;
                    return DoClassFieldRead(node);
                case "CLASS STATIC FIELD":
                    if (topStatement)
                        break;
                    return DoClassStaticFieldRead(node);
                case "UNARY EXPR":
                    // could return string so do anyway
                    return DoUnaryExpr(node);
                case "BINARY EXPR":
                    // could return string so do anyway
                    return DoBinaryExpr(node);
                case "COND EXPR":
                    return DoCondExpr(node);
                case "SELF ASSIGNMENT":
                    // could return string so do anyway
                    return DoSelfAssignment(node, topStatement);
                case "NEW OBJ":
                    t = DoNewObj(node);
                    if (topStatement)
                        CurrentFunction.IL.Emit(OpCodes.Pop);
                    return t;
                case "NEW ARRAY":
                    if (topStatement)
                        break;
                    return DoNewArray(node);
                case "NEW ARRAY PREFILL":
                    if (topStatement)
                        break;
                    return DoNewArrayPrefill(node);
                case "ARRAYREF":
                    if (topStatement)
                        break;
                    return DoArrayRead(node);
                case "THIS COMMAND":
                    return DoThisObjectCommand(node, topStatement);
                case "OBJ COMMAND":
                    return DoObjectCommand(node, topStatement);
                case "THIS COMMAND PAR ID":
                    return DoThisObjectCommandParID(node, topStatement);
                case "OBJ COMMAND PAR ID":
                    return DoObjectCommandParID(node, topStatement);
                case "THEN COMMAND":
                    return DoThenCommand(node, topStatement);
                case "ASSIGNMENT":
                    return DoAssignment(node, topStatement);
                default:
                    throw new Exception("Unknown expression: " + node.Term.Name);
            }

            DoError(node, node.Term.Name + " expects to be used within an expression.");
            return null;
        }

        Type DoGameConstant()
        {
            CurrentFunction.IL.Emit(OpCodes.Ldarg_0);
            Type t = ClassStack.Skip(ClassStack.Count - 1).First().Class;
            if (CurrentClass.Class.BaseType != typeof(RAEGame))
            {
                CurrentFunction.IL.Emit(OpCodes.Call,
                    CurrentClass.Class.BaseType
                    .GetProperty("Game").GetGetMethod());
                ConvertType(typeof(RAEGame), t);
            }

            return t;
        }

        Type DoNewObj(ParseTreeNode node)
        {
            Type t = DoType(node.ChildNodes[1]);
            List<Type> pars = DoParamList(node.ChildNodes[2]);

            // get method
            ConstructorInfo ci;
            if (t == CurrentClass.Class)
            {
                throw new Exception("RAE classes cannot be instanced.");
            }
            else
            {
                ci = t.GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null, pars.ToArray(), null);
            }

            if (ci == null)
            {
                DoError(node.ChildNodes[1], "Constructor " + t.Name + "(" + outputDebugParams(pars) + ") not found.");
            }

            // call
            CurrentFunction.IL.Emit(OpCodes.Newobj, ci);

            return t;
        }

        Type DoNewArray(ParseTreeNode node)
        {
            Type elemType = DoType(node.ChildNodes[1]);
            Type lengthType = DoExpr(node.ChildNodes[2]);
            Type aType = Type.GetType(elemType.FullName + "[]");

            ConvertType(lengthType, typeof(int));
            CurrentFunction.IL.Emit(OpCodes.Newarr, elemType);
            return aType;
        }

        Type DoNewArrayPrefill(ParseTreeNode node)
        {
            Type elemType = DoType(node.ChildNodes[1].ChildNodes[0]);
            List<Type> itemTypes = DoParamList(node.ChildNodes[2]);
            Type aType = DoType(node.ChildNodes[1]);

            LoadConstant(itemTypes.Count);
            CurrentFunction.IL.Emit(OpCodes.Newarr, elemType);
            string aname = AllocTempVariable(aType);
            StoreVariable(aname);
            string ename = AllocTempVariable(elemType);

            for (int i = itemTypes.Count - 1; i >= 0; i--)
            {
                ConvertType(itemTypes[i], elemType);
                StoreVariable(ename);
                LoadVariable(aname);
                LoadConstant(i);
                LoadVariable(ename);
                CurrentFunction.IL.Emit(OpCodes.Stelem, elemType);
            }

            LoadVariable(aname);
            FreeTempVariable(ename);
            FreeTempVariable(aname);

            return aType;
        }

        string outputDebugParams(List<Type> t)
        {
            string val = "";
            for (int i = 0; i < t.Count; i++)
            {
                val += (i > 0 ? ", " : "") + t[i].Name + " p" + i.ToString();
            }
            return val;
        }

        Type DoAdditionOp(Type u, Type v)
        {
            if (u == typeof(string) && v == typeof(string))
            {
                CurrentFunction.IL.Emit(OpCodes.Call, u.GetMethod("Concat", new Type[] { u, v }));
                return u;
            }
            else if (u == typeof(string))
            {
                ConvertType(v, u);
                CurrentFunction.IL.Emit(OpCodes.Call, u.GetMethod("Concat", new Type[] { u, u }));
                return u;
            }
            else if (v == typeof(string))
            {
                var vname = AllocTempVariable(v);
                StoreVariable(vname);

                ConvertType(u, v);

                LoadVariable(vname);
                FreeTempVariable(vname);

                CurrentFunction.IL.Emit(OpCodes.Call, v.GetMethod("Concat", new Type[] { v, v }));
                return v;
            }
            else
            {
                return DoBinaryOp(u, v, OpCodes.Add, "op_Addition");
            }
        }

        Type DoSubtractionOp(Type u, Type v)
        {
            return DoBinaryOp(u, v, OpCodes.Sub, "op_Subtraction");
        }

        Type DoMultiplicationOp(Type u, Type v)
        {
            return DoBinaryOp(u, v, OpCodes.Mul, "op_Multiplication");
        }

        Type DoDivisionOp(Type u, Type v)
        {
            return DoBinaryOp(u, v, OpCodes.Div, "op_Division");
        }

        Type DoModuloOp(Type u, Type v)
        {
            return DoBinaryOp(u, v, OpCodes.Rem, "op_Remainder");
        }

        Type DoBinaryOp(Type u, Type v, OpCode primOp, string objOp, bool matchObjTypes = false)
        {
            if (u.IsPrimitive && v.IsPrimitive && primOp != null)
            {
                Type t = UpgradeValueTypes(u, v);
                if (t != u)
                {
                    string vname = AllocTempVariable(v);
                    StoreVariable(vname);
                    ConvertType(u, t);
                    LoadVariable(vname);
                    FreeTempVariable(vname);
                }

                ConvertType(v, t);

                CurrentFunction.IL.Emit(primOp);
                return t;
            }
            else if (objOp != null)
            {
                if (matchObjTypes)
                {
                    Type t = DowngradeRefTypes(u, v);
                    if (t != u)
                    {
                        string vname = AllocTempVariable(v);
                        StoreVariable(vname);
                        ConvertType(u, t);
                        LoadVariable(vname);
                        FreeTempVariable(vname);
                    }
                    ConvertType(v, t);
                    u = v = t;
                }

                // we have to find an op
                MethodInfo op = u.GetMethod(objOp,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                    null, new Type[] { u, v }, null);
                if (op == null)
                    op = v.GetMethod(objOp,
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                        null, new Type[] { u, v }, null);
                if (op == null)
                    throw new Exception("Cannot " + objOp + " " + u.Name + " and " + v.Name);

                CurrentFunction.IL.Emit(OpCodes.Call, op);
                return op.ReturnType;
            }
            else
                throw new Exception("Cannot ? " + u.Name + " and " + v.Name);
        }

        Type DoNegateOp(Type u)
        {
            if (u.IsPrimitive)
            {
                CurrentFunction.IL.Emit(OpCodes.Neg);
                return u;
            }
            else
            {
                // we have to find an op
                MethodInfo op = u.GetMethod("op_UnaryNegation",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                    null, new Type[] { u }, null);
                if (op == null)
                {
                    // see if we can do Type.Zero - x

                    op = u.GetMethod("op_Subtraction",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy,
                        null, new Type[] { u, u }, null);

                    FieldInfo fi = u.GetField("Zero", BindingFlags.Static | BindingFlags.Public);
                    if (fi == null)
                        throw new Exception("Cannot negate " + u.Name);

                    string tname = AllocTempVariable(typeof(uint));
                    StoreVariable(tname);
                    CurrentFunction.IL.Emit(OpCodes.Ldsfld, fi);
                    LoadVariable(tname);
                    FreeTempVariable(tname);
                }
                if (op == null)
                    throw new Exception("Cannot negate " + u.Name);

                CurrentFunction.IL.Emit(OpCodes.Call, op);
                return op.ReturnType;
            }
        }

        Type DoNotOp(Type t)
        {
            ConvertType(t, typeof(int));
            CurrentFunction.IL.Emit(OpCodes.Ldc_I4_0);
            CurrentFunction.IL.Emit(OpCodes.Ceq);

            return typeof(bool);
        }

        Type DoBinaryExpr(ParseTreeNode node)
        {
            Type u, v;
            switch (node.ChildNodes[1].Term.Name)
            {
                case "+":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoAdditionOp(u, v);
                case "-":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoSubtractionOp(u, v);
                case "*":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoMultiplicationOp(u, v);
                case "/":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoDivisionOp(u, v);
                case "%":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoModuloOp(u, v);
                case "|":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoBinaryOp(u, v, OpCodes.Or, "op_BinaryOr");
                case "&":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoBinaryOp(u, v, OpCodes.And, "op_BinaryAnd");
                case "^":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    return DoBinaryOp(u, v, OpCodes.Xor, "op_BinaryXor");
                case "||":
                    Label lFailure = CurrentFunction.IL.DefineLabel();
                    Label lPass = CurrentFunction.IL.DefineLabel();
                    u = DoExpr(node.ChildNodes[0]);
                    if (u != typeof(bool))
                        DoError(node, "Or requires boolean arguments.");
                    CurrentFunction.IL.Emit(OpCodes.Brtrue, lPass);
                    v = DoExpr(node.ChildNodes[2]);
                    if (v != typeof(bool))
                        DoError(node, "Or requires boolean arguments.");
                    CurrentFunction.IL.Emit(OpCodes.Brtrue, lPass);
                    CurrentFunction.IL.Emit(OpCodes.Ldc_I4_0); // push false b/c failed
                    CurrentFunction.IL.Emit(OpCodes.Br, lFailure);
                    CurrentFunction.IL.MarkLabel(lPass);
                    CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1); // push true b/c passed
                    CurrentFunction.IL.MarkLabel(lFailure);
                    return typeof(bool);
                case "&&":
                    lFailure = CurrentFunction.IL.DefineLabel();
                    u = DoExpr(node.ChildNodes[0]);
                    if (u != typeof(bool))
                        DoError(node, "And requires boolean arguments.");
                    CurrentFunction.IL.Emit(OpCodes.Brfalse, lFailure);
                    v = DoExpr(node.ChildNodes[2]);
                    if (v != typeof(bool))
                        DoError(node, "And requires boolean arguments.");
                    CurrentFunction.IL.Emit(OpCodes.Brfalse_S, lFailure);
                    CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1); // push true b/c passed
                    lPass = CurrentFunction.IL.DefineLabel();
                    CurrentFunction.IL.Emit(OpCodes.Br, lPass);
                    CurrentFunction.IL.MarkLabel(lFailure);
                    CurrentFunction.IL.Emit(OpCodes.Ldc_I4_0); // push false b/c failed
                    CurrentFunction.IL.MarkLabel(lPass);
                    return typeof(bool);
                case ">":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    DoBinaryOp(u, v, OpCodes.Cgt, null);
                    return typeof(bool);
                case "<":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);

                    DoBinaryOp(u, v, OpCodes.Clt, null);
                    return typeof(bool);
                case ">=":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);
                    Type t = DoBinaryOp(u, v, OpCodes.Clt, null);
                    t = DoNotOp(t);
                    return typeof(bool);
                case "<=":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);
                    t = DoBinaryOp(u, v, OpCodes.Cgt, null);
                    t = DoNotOp(t);
                    return typeof(bool);
                case "==":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);
                    t = DoBinaryOp(u, v, OpCodes.Ceq, "Equals", true);
                    return typeof(bool);
                case "!=":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);
                    t = DoBinaryOp(u, v, OpCodes.Ceq, "Equals", true);
                    t = DoNotOp(t);
                    return typeof(bool);
                case "<<":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);
                    return DoBinaryOp(u, v, OpCodes.Shl, "op_ShiftLeft");
                case ">>":
                    u = DoExpr(node.ChildNodes[0]);
                    v = DoExpr(node.ChildNodes[2]);
                    return DoBinaryOp(u, v, OpCodes.Shr, "op_ShiftRight");
                default:
                    DoError(node, "Unrecognized binary term: " + node.ChildNodes[1].ChildNodes[0].Term.Name);
                    return null;
            }
        }

        Type DoUnaryExpr(ParseTreeNode node)
        {
            Type t;
            if (node.ChildNodes[0].Term is KeyTerm)
            {
                // pre
                switch (node.ChildNodes[0].Term.Name)
                {
                    case "-":
                        t = DoExpr(node.ChildNodes[1]);
                        t = DoNegateOp(t);
                        break;
                    case "!":
                        t = DoExpr(node.ChildNodes[1]);
                        t = DoNotOp(t);
                        break;
                    default:
                        throw new Exception("Invalid unary preop: " + node.ChildNodes[0].ChildNodes[0].Term.Name);
                }
            }
            else
            {
                throw new Exception("Invalid unary op type");
            }

            return t;
        }

        Type DoCondExpr(ParseTreeNode node)
        {
            Label falseLabel = CurrentFunction.IL.DefineLabel();
            Label endLabel = CurrentFunction.IL.DefineLabel();

            if (DoExpr(node.ChildNodes[0]) != typeof(bool))
                DoError(node.ChildNodes[0], "Conditional operator requires boolean expression on left side.");
            CurrentFunction.IL.Emit(OpCodes.Brfalse, falseLabel);

            Type t1 = DoExpr(node.ChildNodes[1]);
            CurrentFunction.IL.Emit(OpCodes.Br, endLabel);
            CurrentFunction.IL.MarkLabel(falseLabel);
            Type t2 = DoExpr(node.ChildNodes[2]);
            CurrentFunction.IL.MarkLabel(endLabel);

            if (t1 != t2)
            {
                DoError(node, "Both true result and false result expressions must return same type.");
            }

            return t1;
        }

        void LoadConstant(int i)
        {
            OpCode o;
            switch (i)
            {
                case 0:
                    o = OpCodes.Ldc_I4_0;
                    break;
                case 1:
                    o = OpCodes.Ldc_I4_1;
                    break;
                case 2:
                    o = OpCodes.Ldc_I4_2;
                    break;
                case 3:
                    o = OpCodes.Ldc_I4_3;
                    break;
                case 4:
                    o = OpCodes.Ldc_I4_4;
                    break;
                case 5:
                    o = OpCodes.Ldc_I4_5;
                    break;
                case 6:
                    o = OpCodes.Ldc_I4_6;
                    break;
                case 7:
                    o = OpCodes.Ldc_I4_7;
                    break;
                case -1:
                    o = OpCodes.Ldc_I4_M1;
                    break;
                default:
                    if (i >= sbyte.MinValue && i <= sbyte.MaxValue)
                        CurrentFunction.IL.Emit(OpCodes.Ldc_I4_S, (sbyte)i);
                    else
                        CurrentFunction.IL.Emit(OpCodes.Ldc_I4, i);
                    return;
            }
            CurrentFunction.IL.Emit(o);
        }

        void LoadConstant(double d)
        {
            CurrentFunction.IL.Emit(OpCodes.Ldc_R8, d);
        }

        void LoadConstant(string s)
        {
            CurrentFunction.IL.Emit(OpCodes.Ldstr, s);
        }

        Type UpgradeValueTypes(Type t, Type u)
        {
            if (t == u)
            {
                return t;
            }
            if (t == typeof(double) || u == typeof(double))
            {
                return typeof(double);
            }
            else if (t == typeof(int) || u == typeof(int))
            {
                return typeof(int);
            }
            else
                return typeof(int);
        }

        Type DowngradeRefTypes(Type t, Type u)
        {
            if (t == u)
            {
                return t;
            }
            else if (t.IsSubclassOf(u))
            {
                return u;
            }
            else if (u.IsSubclassOf(t))
            {
                return t;
            }
            else
            {
                return typeof(object);
            }
        }

        Type DoAssignment(ParseTreeNode node, bool topStatement)
        {
            try
            {
                bool destID = node.ChildNodes[0].Term.Name == "ID";
                bool array = node.ChildNodes[0].Term.Name == "ARRAYREF";
                string idName;
                if (destID)
                    idName = (string)node.ChildNodes[0].Token.Value;
                else
                    idName = null;
                string op = node.ChildNodes[1].Term.Name;

                // one of the x = x ? expr shortcuts
                Type rt = null;
                if (op != "=")
                {
                    if (destID)
                    {
                        rt = LoadVariable(idName);
                    }
                    else if (array)
                    {
                        rt = DoArrayRead(node.ChildNodes[0]);
                    }
                    else
                    {
                        rt = DoClassFieldRead(node.ChildNodes[0]);
                    }
                }
                else
                {
                    if (destID)
                        rt = GetVariableType(idName);
                }

                Type t = DoExpr(node.ChildNodes[2]);

                // handle x = x ? expr shortcut operation
                if (op == "+=")
                    t = DoAdditionOp(rt, t);
                else if (op == "-=")
                    t = DoSubtractionOp(rt, t);
                else if (op == "*=")
                    t = DoMultiplicationOp(rt, t);
                else if (op == "/=")
                    t = DoDivisionOp(rt, t);
                else if (op == "%=")
                    t = DoBinaryOp(rt, t, OpCodes.Rem, "op_Remainder");
                else if (op == "|=")
                    t = DoBinaryOp(rt, t, OpCodes.Or, "op_BinaryOr");
                else if (op == "&=")
                    t = DoBinaryOp(rt, t, OpCodes.And, "op_BinaryAnd");
                else if (op == "^=")
                    t = DoBinaryOp(rt, t, OpCodes.Xor, "op_BinaryXor");
                else if (op == "<<=")
                    t = DoBinaryOp(rt, t, OpCodes.Shl, "op_ShiftLeft");
                else if (op == ">>=")
                    t = DoBinaryOp(rt, t, OpCodes.Shr, "op_ShiftRight");

                if (!topStatement)
                    CurrentFunction.IL.Emit(OpCodes.Dup); // if in expr, return it

                if (destID)
                {
                    ConvertType(t, rt);
                    StoreVariable(idName);
                }
                else if (array)
                {
                    string ename = AllocTempVariable(t);
                    StoreVariable(ename);
                    Type et; // element type
                    Type ct = DoArrayWriteObject(node.ChildNodes[0], out et);
                    LoadVariable(ename);
                    FreeTempVariable(ename);
                    ConvertType(t, et);
                    DoArrayWriteFinish(node.ChildNodes[0], ct);
                }
                else
                {
                    string ename = AllocTempVariable(t);
                    StoreVariable(ename);
                    string tname;
                    Type ct = DoClassFieldWriteObject(node.ChildNodes[0], out tname);
                    LoadVariable(ename);
                    FreeTempVariable(ename);
                    bool isStatic;
                    rt = GetFieldType(ct, node.ChildNodes[0].ChildNodes[2].Token.Text, out isStatic);
                    if (isStatic)
                        DoError(node.ChildNodes[0].ChildNodes[2], "Field in this context cannot be static.");
                    ConvertType(t, rt);
                    DoClassFieldWriteFinish(node.ChildNodes[0], ct, tname);
                }

                if (topStatement)
                    return typeof(void);
                else
                    return t;
            }
            catch (Exception e)
            {
                // most likely the var doesn't exist
                DoError(node.ChildNodes[0], e.Message);
                return null;
            }
        }

        Type DoSelfAssignment(ParseTreeNode node, bool topStatement)
        {
            bool destID = node.ChildNodes[0].Term.Name == "ID";
            bool array = node.ChildNodes[0].Term.Name == "ARRAYREF";
            string idName;
            if (destID)
                idName = (string)node.ChildNodes[0].Token.Value;
            else
                idName = null;
            string op = node.ChildNodes[1].Term.Name;

            Type rt = null;
            try
            {
                if (destID)
                {
                    rt = LoadVariable(idName);
                }
                else if (array)
                {
                    rt = DoArrayRead(node.ChildNodes[0]);
                }
                else
                {
                    rt = DoClassFieldRead(node.ChildNodes[0]);
                }
            }
            catch (Exception e)
            {
                DoError(node.ChildNodes[0], e.Message);
            }

            if (!topStatement)
                CurrentFunction.IL.Emit(OpCodes.Dup); // return old value

            switch (node.ChildNodes[1].Term.Name)
            {
                case "++":
                    CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1);
                    ConvertType(
                        DoAdditionOp(rt, typeof(int)),
                        rt);    // absolutely make sure the addition outputs rt
                    break;
                case "--":
                    CurrentFunction.IL.Emit(OpCodes.Ldc_I4_1);
                    ConvertType(
                        DoSubtractionOp(rt, typeof(int)),
                        rt);    // absolutely make sure the subtraction outputs rt
                    break;
                case "!!":
                    ConvertType(rt, typeof(bool));
                    CurrentFunction.IL.Emit(OpCodes.Ldc_I4_0);
                    CurrentFunction.IL.Emit(OpCodes.Ceq);
                    ConvertType(typeof(bool), rt);
                    break;
                default:
                    throw new Exception("Invalid self assignment op: " + node.ChildNodes[1].Term.Name);
            }

            if (destID)
            {
                StoreVariable(idName);
            }
            else if (array)
            {
                string ename = AllocTempVariable(rt);
                StoreVariable(ename);
                Type et; // element type
                Type ct = DoArrayWriteObject(node.ChildNodes[0], out et);
                LoadVariable(ename);
                FreeTempVariable(ename);
                ConvertType(rt, et);
                DoArrayWriteFinish(node.ChildNodes[0], ct);
            }
            else
            {
                string ename = AllocTempVariable(rt);
                StoreVariable(ename);
                string tname;
                Type ct = DoClassFieldWriteObject(node.ChildNodes[0], out tname);
                LoadVariable(ename);
                FreeTempVariable(ename);
                bool isStatic;
                rt = GetFieldType(ct, node.ChildNodes[0].ChildNodes[2].Token.Text, out isStatic);
                if (isStatic)
                    DoError(node.ChildNodes[0].ChildNodes[2], "Variables in this context cannot be static.");
                DoClassFieldWriteFinish(node.ChildNodes[0], ct, tname);
            }
            if (topStatement)
                return typeof(void);
            else
                return rt;
        }

        Type DoArrayRead(ParseTreeNode node)
        {
            Type vt = DoExpr(node.ChildNodes[0]);
            Type it = DoExpr(node.ChildNodes[1]);

            if (vt.IsArray)
            {
                ConvertType(it, typeof(int));

                Type et = vt.GetElementType();
                if (et == typeof(int))
                    CurrentFunction.IL.Emit(OpCodes.Ldelem_I4);
                else if (et == typeof(double))
                    CurrentFunction.IL.Emit(OpCodes.Ldelem_R8);
                else
                    CurrentFunction.IL.Emit(OpCodes.Ldelem, et);
                return et;
            }
            else if (vt.GetInterface("IList") != null
                || vt.GetInterface("IDictionary") != null)
            {
                MethodInfo itemMI = vt.GetProperty("Item").GetGetMethod();
                ConvertType(it, itemMI.GetParameters()[0].ParameterType);

                CurrentFunction.IL.Emit(OpCodes.Call, itemMI);
                return itemMI.ReturnType;
            }
            else
            {
                DoError(node.ChildNodes[0], "Cannot index " + vt.Name);
                return typeof(void);
            }
        }

        Type DoArrayWriteObject(ParseTreeNode node, out Type elementType)
        {
            Type vt = DoExpr(node.ChildNodes[0]);
            Type it = DoExpr(node.ChildNodes[1]);

            Type[] gas;
            if (vt.IsGenericType)
                gas = vt.GetGenericArguments();
            else
                gas = null;

            Type expectedIT;
            if (vt.HasElementType)
                expectedIT = typeof(int);
            else if (vt.IsGenericType && gas.Length == 1)
                expectedIT = typeof(int);
            else if (vt.IsGenericType && gas.Length == 2)
                expectedIT = gas[0];
            else
                throw new CompileException(vt.ToString() + " does not seem to be indexable.");
            ConvertType(it, expectedIT);

            if (vt.HasElementType)
                elementType = vt.GetElementType();
            else if (vt.IsGenericType && gas.Length == 1)
                elementType = gas[0];
            else if (vt.IsGenericType)
                elementType = gas[1];
            else
                throw new CompileException(vt.ToString() + " does not seem to be indexable.");
            return vt;
        }

        void DoArrayWriteFinish(ParseTreeNode node, Type classType)
        {
            if (classType.IsArray)
            {
                Type et = classType.GetElementType();
                if (et == typeof(int))
                    CurrentFunction.IL.Emit(OpCodes.Stelem_I4);
                else if (et == typeof(double))
                    CurrentFunction.IL.Emit(OpCodes.Stelem_R8);
                else
                    CurrentFunction.IL.Emit(OpCodes.Stelem, et);
            }
            else if (classType.GetInterface(" IList") != null
                || classType.GetInterface("IDictionary") != null)
            {
                CurrentFunction.IL.Emit(OpCodes.Call,
                    classType.GetProperty("Item").GetSetMethod());
            }
            else
            {
                DoError(node.ChildNodes[0], "Cannot index " + classType.Name);
            }
        }
    }
}
