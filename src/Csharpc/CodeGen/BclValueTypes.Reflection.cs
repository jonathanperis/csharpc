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
        t[$"{Type}.GetEnumUnderlyingType()"] = (g, _, self, _, _) => TypeResult(g, "csrt_type_enum_underlying", self);
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
        t[$"{Type}.GetInterfaces()"] = (g, _, self, _, _) =>
        {
            g.Module.NoteGetInterfaces();
            var array = Rt(g, "csrt_type_interfaces", g.Llvm.Ptr, (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));
            g.TagArray(array, g.Module.Compilation.GetTypeByMetadataName(Type)!);
            return array;
        };
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

        // Member reflection over compile-time metadata (ModuleCodeGen.Reflection.cs, corelib/Reflection/Members.cs).
        BclHandler core(string method) => (g, _, self, a, _) => g.CallCoreLib("ReflectionRuntime", method, [self, .. a]);
        t["System.Reflection.TypeInfo.get_GenericTypeParameters()"] = core("GenericTypeParameters");
        t[$"{Type}.GetConstructors()"] = core("GetConstructorsAll");
        t[$"{Type}.GetConstructors(System.Reflection.BindingFlags)"] = core("GetConstructorsFlags");
        t[$"{Type}.GetConstructor(System.Type[])"] = core("GetConstructorTypes");
        t[$"{Type}.GetConstructor(System.Reflection.BindingFlags, System.Type[])"] = core("GetConstructorFlagsTypes");
        t[$"{Type}.GetConstructor(System.Reflection.BindingFlags, System.Reflection.Binder, System.Type[], System.Reflection.ParameterModifier[])"] = core("GetConstructor");
        t[$"{Type}.GetMethods()"] = core("GetMethodsAll");
        t[$"{Type}.GetMethods(System.Reflection.BindingFlags)"] = core("GetMethodsFlags");
        t[$"{Type}.GetMethod(string)"] = core("GetMethodName");
        t[$"{Type}.GetMethod(string, System.Reflection.BindingFlags)"] = core("GetMethodNameFlags");
        t[$"{Type}.GetMethod(string, System.Type[])"] = core("GetMethodNameTypes");
        t[$"{Type}.GetMethod(string, System.Reflection.BindingFlags, System.Type[])"] = core("GetMethodNameFlagsTypes");
        BclHandler propertyLookup(string method) => (g, _, self, a, _) => g.CallCoreLib("PropertyReflection", method, [self, .. a]);
        t[$"{Type}.GetProperties()"] = propertyLookup("GetPropertiesAll");
        t[$"{Type}.GetProperties(System.Reflection.BindingFlags)"] = propertyLookup("GetPropertiesFlags");
        t[$"{Type}.GetProperty(string)"] = propertyLookup("GetPropertyName");
        t[$"{Type}.GetProperty(string, System.Reflection.BindingFlags)"] = propertyLookup("GetPropertyNameFlags");
        t[$"{Type}.GetProperty(string, System.Type)"] = propertyLookup("GetPropertyNameReturn");
        t[$"{Type}.GetProperty(string, System.Type, System.Type[])"] = propertyLookup("GetPropertyNameReturnTypes");
        t[$"{Type}.GetProperty(string, System.Reflection.BindingFlags, System.Reflection.Binder, System.Type, System.Type[], System.Reflection.ParameterModifier[])"] = propertyLookup("GetPropertyFull");
        BclHandler fieldLookup(string method) => (g, _, self, a, _) => g.CallCoreLib("FieldReflection", method, [self, .. a]);
        t[$"{Type}.GetFields()"] = fieldLookup("GetFieldsAll");
        t[$"{Type}.GetFields(System.Reflection.BindingFlags)"] = fieldLookup("GetFieldsFlags");
        t[$"{Type}.GetField(string)"] = fieldLookup("GetFieldName");
        t[$"{Type}.GetField(string, System.Reflection.BindingFlags)"] = fieldLookup("GetFieldNameFlags");
        // A type's own attributes: the program keeps those of every type and generic parameter once it asks.
        BclHandler typeAttributes(string method) => (g, _, self, a, _) =>
        {
            g.Module.NoteTypeAttributesQueried();
            return g.CallCoreLib("ReflectionRuntime", method, [self, .. a]);
        };
        t[$"{Type}.GetCustomAttributes(bool)"] = typeAttributes("TypeAttributesAll");
        t[$"{Type}.GetCustomAttributes(System.Type, bool)"] = typeAttributes("TypeAttributesOf");
        t[$"{Type}.IsDefined(System.Type, bool)"] = typeAttributes("IsTypeDefined");
        t[$"{Type}.GetCustomAttributesData()"] = typeAttributes("GetTypeCustomAttributesData");
        t[$"{Type}.MakeGenericType(System.Type[])"] = (g, _, self, a, _) =>
            Rt(g, "csrt_type_make_generic", g.Llvm.Ptr, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));
        t[$"{Type}.MakeArrayType()"] = (g, _, self, _, _) => TypeResult(g, "csrt_type_make_array", self);
        t[$"{Type}.MakeByRefType()"] = core("MakeByRefType");
        // System.Linq.Expressions' interpreter and other reflection users (corelib/Reflection/TypeExtras.cs).
        BclHandler extras(string method) => (g, _, self, a, _) => g.CallCoreLib("TypeExtras", method, [self, .. a]);
        BclHandler staticExtras(string method) => (g, _, _, a, _) => g.CallCoreLib("TypeExtras", method, a);
        t[$"{Type}.GetMethod(string, System.Reflection.BindingFlags, System.Reflection.Binder, System.Type[], System.Reflection.ParameterModifier[])"] = extras("GetMethodBinder");
        t[$"{Type}.GetMemberWithSameMetadataDefinitionAs(System.Reflection.MemberInfo)"] = extras("GetMemberWithSameMetadataDefinitionAs");
        t[$"{Type}.MakeArrayType(int)"] = extras("MakeArrayTypeRank");
        t["System.Array.CreateInstance(System.Type, int[])"] = staticExtras("CreateArrayInstance");
        t["System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(object)"] = staticExtras("GetObjectValue");
        t["System.Array.SetValue(object, int, int)"] = (g, _, self, a, _) => g.CallCoreLib("TypeExtras", "ArraySetValueRank", self, a[0], LlvmContext.Null(g.Llvm.Ptr));
        t["System.Array.SetValue(object, int, int, int)"] = (g, _, self, a, _) => g.CallCoreLib("TypeExtras", "ArraySetValueRank", self, a[0], LlvmContext.Null(g.Llvm.Ptr));
        // No collectible assemblies (closed world); no COM type equivalence: identity.
        t[$"{Type}.get_IsCollectible()"] = t["System.Reflection.MemberInfo.get_IsCollectible()"] = (g, _, self, _, _) => { g.NullCheck(self); return g.Llvm.False; };
        t[$"{Type}.IsEquivalentTo(System.Type)"] = (g, _, self, a, _) =>
        {
            g.NullCheck(self);
            return g.Builder.ICmp(LLVMIntPredicate.EQ, self, a[0]);
        };
        // Delegate.DynamicInvoke: the delegate type's Invoke through reflection (kept for every delegate type
        // of programs that call it, ModuleCodeGen.ObjectArrayDelegates.cs).
        t["System.Delegate.DynamicInvoke(object[])"] = (g, _, self, a, _) =>
        {
            g.Module.NoteDynamicInvoke();
            return g.CallCoreLib("TypeExtras", "DynamicInvoke", self, a[0]);
        };
        // Exception.Data (corelib/ExceptionData.cs): csrt_Exception has no field for it.
        t["System.Exception.get_Data()"] = (g, _, self, _, _) => g.CallCoreLib("ExceptionData", "Get", self);
        t[$"{Type}.get_GenericParameterAttributes()"] = (g, _, self, _, _) =>
            Rt(g, "csrt_type_generic_parameter_attributes", g.Llvm.I32, (self, g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));
        t["System.Array.CreateInstance(System.Type, int)"] = (g, _, _, a, _) =>
        {
            // DI/config binders CreateInstance types kept for reflection: ensure T[] (with IList<T>)
            // for those, not every TypeInfo type (that blew compile time on TracingConfiguration).
            g.Module.NoteArrayCreateInstance();
            return g.CallCoreLib("ReflectionRuntime", "CreateInstance", a[0], a[1]);
        };
        // Configuration.Binder and similar: Activator.CreateInstance(Type) via kept public .ctor metadata.
        t["System.Type.GetType(string)"] = (g, _, _, a, _) => g.CallCoreLib("ReflectionRuntime", "GetTypeByName", a[0]);
        t["System.Type.GetType(string, bool)"] = (g, _, _, a, _) => g.CallCoreLib("ReflectionRuntime", "GetTypeByName", a[0], a[1]);
        t["System.Type.GetType(string, bool, bool)"] = (g, _, _, a, _) => g.CallCoreLib("ReflectionRuntime", "GetTypeByName", a[0], a[1], a[2]);
        t["System.Type.GetTypeCode(System.Type)"] = (g, _, _, a, _) => g.CallCoreLib("ReflectionRuntime", "GetTypeCode", a[0]);
        t["System.Activator.CreateInstance(System.Type)"] = (g, _, _, a, _) =>
            g.CallCoreLib("ReflectionRuntime", "ActivatorCreateInstance", a[0]);
        t["System.Activator.CreateInstance(System.Type, System.Reflection.BindingFlags, System.Reflection.Binder, object[], System.Globalization.CultureInfo)"] = (g, _, _, a, _) =>
            g.CallCoreLib("ReflectionRuntime", "ActivatorCreateInstanceWith", a[0], a[1], a[2], a[3], a[4]);
        t["System.Array.SetValue(object, int)"] = (g, _, self, a, _) => g.CallCoreLib("ReflectionRuntime", "SetValue", self, a[0], a[1]);
        t["System.Enum.ToObject(System.Type, object)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_enum_to_object", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));
        foreach (var integral in new[] { "int", "long", "uint", "ulong", "short", "ushort", "byte", "sbyte" })
            t[$"System.Enum.ToObject(System.Type, {integral})"] = (g, m, _, a, at) =>
                Rt(g, "csrt_enum_to_object", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (g.Box(a[1], m.Parameters[1].Type, at), g.Llvm.Ptr), (Table(g), g.Llvm.Ptr));
        // `GetUninitializedObject(typeof(C))` constructs a C (its vtable slots are kept; for Nullable<T>, a boxed
        // T). With a type only known at run time, a class nothing else allocates has a pruned method table
        // (instance size 0), and csrt reports that explicitly — as NativeAOT does for a type whose MethodTable
        // isn't constructed. .NET's argument checks are corelib C# (ReflectionRuntime.GetUninitializedObject).
        t["System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(System.Type)"] = (g, _, _, a, at) =>
        {
            if (at is Microsoft.CodeAnalysis.Operations.IInvocationOperation { Arguments: [var argument] } && FunctionCodeGen.TypeOfOperand(argument.Value) is { } created)
                g.NoteConstructedType(created is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] } ? underlying : created);
            return g.CallCoreLib("ReflectionRuntime", "GetUninitializedObject", a[0]);
        };
        // Exception.HResult and OperationCanceledException.CancellationToken: fields of csrt_Exception (eh.c).
        t["System.Exception.get_HResult()"] = (g, _, self, _, _) => Rt(g, "csrt_exception_get_hresult", g.Llvm.I32, (self, g.Llvm.Ptr));
        t["System.Exception.set_HResult(int)"] = (g, _, self, a, _) => { Rt(g, "csrt_exception_set_hresult", g.Llvm.Void, (self, g.Llvm.Ptr), (a[0], g.Llvm.I32)); return default; };
        t["System.OperationCanceledException.get_CancellationToken()"] = (g, _, self, _, _) => Rt(g, "csrt_exception_get_token", g.Llvm.Ptr, (self, g.Llvm.Ptr));
        t["System.Delegate.get_Method()"] = (g, _, self, _, _) => g.CallCoreLib("ReflectionRuntime", "DelegateMethod", self);
        t["System.Delegate.get_Target()"] = (g, _, self, _, _) => g.CallCoreLib("ReflectionRuntime", "DelegateTarget", self);
        t["System.Reflection.RuntimeReflectionExtensions.GetMethodInfo(System.Delegate)"] = (g, _, _, a, _) => g.CallCoreLib("ReflectionRuntime", "DelegateMethod", a[0]);

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
            g.Module.ProgramAssemblyName(g.Module.ProgramEntryPoint) is { } name
                ? g.CallCoreLib("AssemblyImpl", "ForName", g.Module.StringLiteral(name))
                : throw CompileError.Unsupported("Assembly.GetEntryAssembly() of a signed or wildcard-versioned assembly", at);
    }
}
