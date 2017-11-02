using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;

namespace GenericTest
{
    class Program
    {
        void hello()
        {
            try
            {
                Console.Write("h");
                return;
            }
            catch (Exception e)
            {
                Console.Write("o " + e.ToString());
            }
            Console.Write(".");
        }

        int f()
        {
            Console.Write("a");
            while (true)
            {
                try
                {
                    Console.Write(".");
                    return 2;
                }
                catch (Exception)
                {
                    Console.Write("e");
                }
            }
            Console.Write("b");
            return 0;
            Console.Write("c");
        }

        static void Main(string[] args)
        {
            List<int> a = new List<int>();
            if (a.Count < 5)
                a[0] = a[0];


            AssemblyBuilder ab = AppDomain.CurrentDomain
                .DefineDynamicAssembly(new AssemblyName("test"), AssemblyBuilderAccess.RunAndSave);
            ModuleBuilder mb = ab.DefineDynamicModule("test", "test.exe", true);

            Type bt = typeof(List<string>);
            var t = mb.DefineType("TestClass", TypeAttributes.Public, bt);

            ConstructorBuilder cb = t.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, Type.EmptyTypes);
            var il = cb.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, bt.GetConstructor(Type.EmptyTypes));
            il.EmitWriteLine("cake");
            il.Emit(OpCodes.Ret);

            t.CreateType();

            var main = mb.DefineGlobalMethod("main", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.SpecialName, typeof(void), Type.EmptyTypes);
            il = main.GetILGenerator();
            var list = il.DeclareLocal(t);
            list.SetLocalSymInfo("list");
            il.Emit(OpCodes.Newobj, cb);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ret);

            mb.CreateGlobalFunctions();
            ab.SetEntryPoint(main, PEFileKinds.ConsoleApplication);

            ab.Save("test.exe");
            mb.GetMethod("main").Invoke(null, null);
        }
    }
}
