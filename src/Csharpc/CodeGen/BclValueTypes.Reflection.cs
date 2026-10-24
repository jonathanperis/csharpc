using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// System.Type reflection over the compile-time metadata (ModuleCodeGen.TypeInfo.cs, src/runtime/type.c):
/// predicates, BaseType, generic arguments, assignability, AssemblyQualifiedName, Type.Assembly.
/// Members not listed here stay "not supported by csharpc yet" compile errors (MakeGenericType,
/// GetMethods, GetProperties, ...).
/// </summary>
internal static partial class BclValueTypes
{
    private static void AddReflectionMethods(Dictionary<string, BclHandler> t)
    {
        const string Type = "System.Type";
        LLVMValueRef Table(FunctionCodeGen g) => g.Module.TypeInfoTable();
        LLVMValueRef TypeResult(FunctionCodeGen g, string fn, LLVMValueRef self) =>
            Rt(g, fn, g.Llvm.Ptr, (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));

        foreach (var (property, mask) in ModuleCodeGen.TypeFlagProperties)
            t[$"{Type}.get_{property}()"] = (g, _, self, _, _) =>
                Rt(g, "csrt_type_flag", g.Llvm.I1, (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr), (g.Llvm.ConstUInt(g.Llvm.I64, mask), g.Llvm.I64));
        // Kinds of types csharpc never creates Type objects for: by-refs, method generic parameters,
        // function pointers, COM objects, remoting.
        foreach (var property in new[] { "IsByRef", "IsGenericMethodParameter", "IsFunctionPointer", "IsUnmanagedFunctionPointer",
                     "IsCOMObject", "IsContextful", "IsMarshalByRef", "IsSignatureType" })
            t[$"{Type}.get_{property}()"] = (g, _, self, _, _) => { g.NullCheck(self); return g.Llvm.False; };

        t[$"{Type}.get_BaseType()"] = (g, _, self, _, _) => TypeResult(g, "csrt_type_base", self);
        t[$"{Type}.GetElementType()"] = (g, _, self, _, _) => TypeResult(g, "csrt_type_element", self);
        t[$"{Type}.get_DeclaringType()"] = t["System.Reflection.MemberInfo.get_DeclaringType()"] =
            (g, _, self, _, _) => TypeResult(g, "csrt_type_declaring", self);
        t[$"{Type}.GetGenericTypeDefinition()"] = (g, _, self, _, _) => TypeResult(g, "csrt_type_generic_definition", self);
        t[$"{Type}.get_UnderlyingSystemType()"] = (g, _, self, _, _) => { g.NullCheck(self); return self; };
        BclHandler arguments(bool constructedOnly) => (g, _, self, _, _) =>
        {
            var array = Rt(g, "csrt_type_generic_arguments", g.Llvm.Ptr, (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr),
                (constructedOnly ? g.Llvm.True : g.Llvm.False, g.Llvm.I1));
            g.TagArray(array, g.Module.Compilation.GetTypeByMetadataName(Type)!);
            return array;
        };
        t[$"{Type}.GetGenericArguments()"] = arguments(false);
        t[$"{Type}.get_GenericTypeArguments()"] = arguments(true);
        t[$"{Type}.GetArrayRank()"] = (g, _, self, _, _) =>
            Rt(g, "csrt_type_array_rank", g.Llvm.I32, (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));
        t[$"{Type}.get_GenericParameterPosition()"] = (g, _, self, _, _) =>
            Rt(g, "csrt_type_generic_parameter_position", g.Llvm.I32, (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));

        BclHandler relation(string fn) => (g, _, self, a, _) =>
            Rt(g, fn, g.Llvm.I1, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));
        t[$"{Type}.IsAssignableFrom(System.Type)"] = relation("csrt_type_is_assignable_from");
        t[$"{Type}.IsAssignableTo(System.Type)"] = relation("csrt_type_is_assignable_to");
        t[$"{Type}.IsInstanceOfType(object)"] = relation("csrt_type_is_instance_of");
        t[$"{Type}.IsSubclassOf(System.Type)"] = relation("csrt_type_is_subclass_of");
        t["System.Nullable.GetUnderlyingType(System.Type)"] = (g, _, _, a, _) => TypeResult(g, "csrt_type_nullable_underlying", a[0]);

        // Type.FullName differs from ToString() for generic instantiations (assembly-qualified type
        // arguments) and generic parameters (null): a small table of just those types.
        t[$"{Type}.get_FullName()"] = (g, _, self, _, _) =>
            Rt(g, "csrt_type_full_name_of", g.Llvm.Ptr, (self, g.Llvm.Ptr), (g.Module.TypeNameTable(), g.Llvm.Ptr));
        t[$"{Type}.get_AssemblyQualifiedName()"] = (g, _, self, _, _) => Rt(g, "csrt_type_assembly_qualified_name", g.Llvm.Ptr,
            (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr), (g.Module.TypeNameTable(), g.Llvm.Ptr));

        // Assembly objects are corelib C# (corelib/Reflection/Assembly.cs), one per assembly name.
        t[$"{Type}.get_Assembly()"] = (g, _, self, _, _) =>
            g.CallCoreLib("AssemblyImpl", "ForName", TypeResult(g, "csrt_type_assembly_name", self));
        t["System.Reflection.Assembly.GetExecutingAssembly()"] = (g, _, _, _, at) =>
        {
            var owner = g.CurrentMethod?.ContainingType;
            var name = owner is null ? g.Module.ProgramAssemblyName(null) : g.Module.AssemblyOf(owner);
            return name is null
                ? throw CompileError.Unsupported("Assembly.GetExecutingAssembly() outside the program's own code", at)
                : g.CallCoreLib("AssemblyImpl", "ForName", g.Module.StringLiteral(name));
        };
        t["System.Reflection.Assembly.GetEntryAssembly()"] = (g, _, _, _, at) =>
            g.Module.ProgramAssemblyName(g.Module.Compilation.GetEntryPoint(CancellationToken.None)) is { } name
                ? g.CallCoreLib("AssemblyImpl", "ForName", g.Module.StringLiteral(name))
                : throw CompileError.Unsupported("Assembly.GetEntryAssembly() of a signed or wildcard-versioned assembly", at);
    }
}
