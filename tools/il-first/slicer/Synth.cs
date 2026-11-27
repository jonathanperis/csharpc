// A small IL library that calls CoreLib-internal members C# cannot name, for the tests of the IL front-end's intrinsics for them
// (tests/slicer.sh). Written with Cecil because the member is internal: no C# program can call it, and the framework's own callers
// are behind feature switches csharpc folds (ArraySortHelper<T>) or need reflection csharpc has not got (ComparerHelpers).
//
//   dotnet Slicer.dll synth-create-instance <out.dll>
//
// RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter (issue #224):
//   public class Holder<T> { ToString() => "Holder<" + typeof(T).Name + ">" }      public class Pair<A, B> { ToString() => "Pair<A,B>" with names }
//   public static class Synth {
//     static object Make<T>()      => CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(Holder<string>), (RuntimeType)typeof(T));
//     static object Make<T, U>()   => CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(Pair<string, string>), (RuntimeType)typeof(T), (RuntimeType)typeof(U));
//     static object Dynamic(Type t) => CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(Holder<string>), (RuntimeType)t);   // not a constant: a diagnostic
//   }
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Synth
{
    public static void CreateInstance(string output)
    {
        var cli = Cli.Parse(["--roots", "x"]);
        var systemRuntime = ModuleDefinition.ReadModule(Path.Combine(cli.RefDir, "System.Runtime.dll")).Assembly.Name;
        var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("SynthCreateInstance", new Version(1, 0, 0, 0)), "SynthCreateInstance", ModuleKind.Dll);
        var m = asm.MainModule;
        // System.Runtime first, so the module's TypeSystem takes it as the core library.
        var runtimeRef = new AssemblyNameReference(systemRuntime.Name, systemRuntime.Version) { PublicKeyToken = systemRuntime.PublicKeyToken };
        m.AssemblyReferences.Add(runtimeRef);
        var coreLib = new AssemblyNameReference("System.Private.CoreLib", new Version(11, 0, 0, 0)) { PublicKeyToken = systemRuntime.PublicKeyToken };
        m.AssemblyReferences.Add(coreLib);
        var ts = m.TypeSystem;
        TypeReference Ref(string ns, string name, IMetadataScope scope, bool value = false) => new(ns, name, m, scope, value);
        var type = Ref("System", "Type", runtimeRef);
        var runtimeType = Ref("System", "RuntimeType", coreLib);
        var handle = Ref("System", "RuntimeTypeHandle", runtimeRef, value: true);
        var getType = new MethodReference("GetTypeFromHandle", type, type) { Parameters = { new ParameterDefinition(handle) } };
        var getName = new MethodReference("get_Name", ts.String, type) { HasThis = true };
        var concat = new MethodReference("Concat", ts.String, ts.String) { Parameters = { new ParameterDefinition(ts.String), new ParameterDefinition(ts.String), new ParameterDefinition(ts.String) } };
        var objectCtor = new MethodReference(".ctor", ts.Void, ts.Object) { HasThis = true };

        TypeDefinition Generic(string name, params string[] parameters)
        {
            var t = new TypeDefinition("", name + "`" + parameters.Length, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, ts.Object);
            foreach (var p in parameters) t.GenericParameters.Add(new GenericParameter(p, t));
            var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, ts.Void);
            var il = ctor.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, objectCtor); il.Emit(OpCodes.Ret);
            t.Methods.Add(ctor);
            // ToString() => name + "<" + typeof(P0).Name + "," + typeof(P1).Name + ">"
            var toString = new MethodDefinition("ToString", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, ts.String);
            il = toString.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, name + "<");
            for (var i = 0; i < parameters.Length; i++)
            {
                il.Emit(OpCodes.Ldtoken, t.GenericParameters[i]); il.Emit(OpCodes.Call, getType); il.Emit(OpCodes.Callvirt, getName);
                il.Emit(OpCodes.Call, new MethodReference("Concat", ts.String, ts.String) { Parameters = { new ParameterDefinition(ts.String), new ParameterDefinition(ts.String) } });
                il.Emit(OpCodes.Ldstr, i + 1 < parameters.Length ? "," : ">");
                il.Emit(OpCodes.Call, new MethodReference("Concat", ts.String, ts.String) { Parameters = { new ParameterDefinition(ts.String), new ParameterDefinition(ts.String) } });
            }
            il.Emit(OpCodes.Ret);
            t.Methods.Add(toString);
            m.Types.Add(t);
            return t;
        }
        var holder = Generic("Holder", "T");
        var pair = Generic("Pair", "A", "B");

        var synth = new TypeDefinition("", "Synth", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, ts.Object);
        m.Types.Add(synth);
        MethodReference Create(int arity)
        {
            var r = new MethodReference("CreateInstanceForAnotherGenericParameter", ts.Object, handle);
            for (var i = 0; i < arity; i++) r.Parameters.Add(new ParameterDefinition(runtimeType));
            return r;
        }
        GenericInstanceType Close(TypeDefinition g, params TypeReference[] args)
        {
            var c = new GenericInstanceType(g);
            foreach (var a in args) c.GenericArguments.Add(a);
            return c;
        }
        void Typeof(ILProcessor il, TypeReference t) { il.Emit(OpCodes.Ldtoken, t); il.Emit(OpCodes.Call, getType); il.Emit(OpCodes.Castclass, runtimeType); }

        var make1 = new MethodDefinition("Make", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, ts.Object);
        var t1 = new GenericParameter("T", make1); make1.GenericParameters.Add(t1);
        var il1 = make1.Body.GetILProcessor();
        Typeof(il1, Close(holder, ts.String)); Typeof(il1, t1);
        il1.Emit(OpCodes.Call, Create(2)); il1.Emit(OpCodes.Ret);
        synth.Methods.Add(make1);

        var make2 = new MethodDefinition("Make", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, ts.Object);
        var a2 = new GenericParameter("T", make2); var b2 = new GenericParameter("U", make2);
        make2.GenericParameters.Add(a2); make2.GenericParameters.Add(b2);
        var il2 = make2.Body.GetILProcessor();
        Typeof(il2, Close(pair, ts.String, ts.String)); Typeof(il2, a2); Typeof(il2, b2);
        il2.Emit(OpCodes.Call, Create(3)); il2.Emit(OpCodes.Ret);
        synth.Methods.Add(make2);

        var dynamic = new MethodDefinition("Dynamic", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, ts.Object);
        dynamic.Parameters.Add(new ParameterDefinition("t", ParameterAttributes.None, type));
        var il3 = dynamic.Body.GetILProcessor();
        Typeof(il3, Close(holder, ts.String));
        il3.Emit(OpCodes.Ldarg_0); il3.Emit(OpCodes.Castclass, runtimeType);
        il3.Emit(OpCodes.Call, Create(2)); il3.Emit(OpCodes.Ret);
        synth.Methods.Add(dynamic);

        asm.Write(output);
        Console.WriteLine($"{output}: Synth.Make<T>(), Synth.Make<T, U>(), Synth.Dynamic(Type)");
    }
}
