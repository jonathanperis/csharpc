using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// CsharpcCoreLib.CoreIntrinsics: extern generic helpers whose bodies codegen synthesizes per
/// instantiation. They give the corelib's EqualityComparer&lt;T&gt;.Default / Comparer&lt;T&gt;.Default exact
/// .NET semantics without boxing primitives (Dictionary&lt;int, V&gt; hashes an int inline):
///
///   AreEqual&lt;T&gt;(a, b)  — EqualityComparer&lt;T&gt;.Default.Equals: IEquatable&lt;T&gt; if implemented, else Equals(object);
///                         double.Equals(NaN, NaN) is true; nulls compare equal to each other only.
///   HashOf&lt;T&gt;(a)       — EqualityComparer&lt;T&gt;.Default.GetHashCode (0 for null).
///   Compare&lt;T&gt;(a, b)   — Comparer&lt;T&gt;.Default.Compare: primitives directly, strings with culture (ICU root)
///                         collation, IComparable&lt;T&gt; / IComparable; null sorts first.
/// </summary>
/// <summary>A property/indexer reference captured as an assignment target: receiver and arguments, already evaluated.</summary>
public sealed record PropertyCapture(IPropertySymbol Property, LLVMValueRef Self, LLVMValueRef[] Arguments);

public sealed partial class FunctionCodeGen
{
    /// <summary>
    /// `dict[key] = c ? a : b`: Roslyn's CFG captures the indexer reference before the branches.
    /// Evaluate its receiver and arguments once (spilled to slots); later reads/writes of the capture
    /// call the getter/setter with them.
    /// </summary>
    private PropertyCapture CaptureProperty(IPropertyReferenceOperation p)
    {
        var property = S(p.Property);
        var accessor = property.SetMethod ?? property.GetMethod!;
        LLVMValueRef self = default;
        if (!property.IsStatic)
        {
            var value = Receiver(p.Instance!, accessor);
            self = NewTemp(L.Ptr, "prop.self");
            _b.Store(value, self);
        }
        var args = p.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(a =>
        {
            var v = EmitArgument(a, accessor);
            var slot = NewTemp(v.Type, "prop.arg");
            _b.Store(v, slot);
            return slot;
        }).ToArray();
        return new PropertyCapture(property, self, args);
    }

    private LLVMValueRef[] CapturedAccessorArgs(PropertyCapture pc, IMethodSymbol accessor, LLVMValueRef[] extra)
    {
        List<LLVMValueRef> args = [];
        if (pc.Self.Handle != 0) args.Add(_b.Load(L.Ptr, pc.Self));
        args.AddRange(pc.Arguments.Select(slot => _b.Load(Native.LLVMGetAllocatedType(slot), slot)));
        args.AddRange(extra);
        return [.. args];
    }

    private LLVMValueRef CallAccessorDirect(IMethodSymbol accessor, LLVMValueRef[] args)
    {
        if (accessor.ContainingType.TypeKind == TypeKind.Interface) return InterfaceDispatch(accessor, args[0], args);
        if (!_m.IsUserMethod(accessor)) throw CompileError.Unsupported($"captured library property accessor '{accessor}'", accessor);
        if (ModuleCodeGen.HasThis(accessor) && !accessor.ContainingType.IsValueType) NullCheck(args[0]);
        return CallMethod(accessor, args);
    }

    private LLVMValueRef GetCapturedProperty(PropertyCapture pc)
    {
        var getter = pc.Property.GetMethod ?? throw CompileError.Unsupported($"reading set-only property '{pc.Property.Name}'", pc.Property);
        return CallAccessorDirect(getter, CapturedAccessorArgs(pc, getter, []));
    }

    private LLVMValueRef SetCapturedProperty(PropertyCapture pc, LLVMValueRef value)
    {
        if (pc.Property.SetMethod is not { } setter)
        {
            // Get-only auto-property assigned in a constructor: write its backing field.
            var field = BackingField(pc.Property);
            var address = field.IsStatic ? StaticFieldAddress(field) : FieldAddressOn(_b.Load(L.Ptr, pc.Self), field);
            StoreValue(ToStorage(value, S(field.Type)), address);
            return value;
        }
        CallAccessorDirect(setter, CapturedAccessorArgs(pc, setter, [value]));
        return value;
    }

    private bool IsCoreIntrinsic(IMethodSymbol target) =>
        _m.CoreLib.IntrinsicsType is { } ci && SymbolEqualityComparer.Default.Equals(target.ContainingType.OriginalDefinition, ci);

    private LLVMValueRef EmitCoreIntrinsic(IMethodSymbol target, IInvocationOperation inv)
    {
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(a => EmitArgument(a, target)).ToArray();
        var t = target.TypeArguments.Length > 0 ? target.TypeArguments[0] : null;
        return target.Name switch
        {
            "AreEqual" => EqualsOf(t!, args[0], args[1], inv),
            "HashOf" => HashOf(t!, args[0], inv),
            "Compare" => CompareOf(t!, args[0], args[1], inv),
            "IsValueType" => t!.IsValueType ? L.True : L.False,
            "Box" => BoxOrThrow(t!, args[0], inv),
            "Unbox" => UnboxOrThrow(t!, args[0], inv),
            "CastOrThrow" => CastOrThrow(t!, args[0], inv),
            "FastAllocateString" => FastAllocateString(args[0]),
            "ObjectAddress" => args[0],
            "StringChars" => MakeSpan(_b.StructGep(T.StringHeader, args[0], 2), LoadStringLength(args[0])),
            "DoubleToBits" => _b.Cast(LLVMOpcode.BitCast, args[0], L.I64),
            "BitsToDouble" => _b.Cast(LLVMOpcode.BitCast, args[0], L.F64),
            "SingleToBits" => _b.Cast(LLVMOpcode.BitCast, args[0], L.I32),
            "BitsToSingle" => _b.Cast(LLVMOpcode.BitCast, args[0], L.F32),
            // decimal is { i32 flags, i32 hi, i64 lo } (runtime/decimal.c)
            "DecimalFlags" => _b.ExtractValue(args[0], 0),
            "DecimalHi" => _b.ExtractValue(args[0], 1),
            "DecimalLo" => _b.ExtractValue(args[0], 2),
            "MakeDecimal" => _b.InsertValue(_b.InsertValue(_b.InsertValue(LlvmContext.Null(DecimalType), args[0], 0), args[1], 1), args[2], 2),
            // DateTime is its i64 _dateData (BclValueTypes.cs)
            "DateTimeData" or "MakeDateTime" => args[0],
            // An object reference as a native-sized integer and back (the GC never moves objects).
            "ObjectToPointer" => args[0].Type.Kind == LLVMTypeKind.Pointer
                ? _b.Cast(LLVMOpcode.PtrToInt, args[0], T.Value(_m.Compilation.GetSpecialType(SpecialType.System_IntPtr))) : args[0],
            "ReinterpretAs" => args[0],
            "SizeOf" => L.ConstInt(L.I32, (long)T.Target.SizeOf(T.Storage(S(t!)))),
            "SpanAddress" => _b.Cast(LLVMOpcode.PtrToInt, SpanPointer(args[0]), T.NativeInt),
            "RefAddress" => _b.Cast(LLVMOpcode.PtrToInt, args[0], T.NativeInt),
            "PointerToObject" => args[0].Type.Kind == LLVMTypeKind.Pointer ? args[0] : _b.Cast(LLVMOpcode.IntToPtr, args[0], L.Ptr),
            "EnumToUInt64" => EnumToUInt64(t!, args[0]),
            "EnumFromUInt64" => EnumFromUInt64(t!, args[0]),
            "IsSignedEnum" => t is INamedTypeSymbol { EnumUnderlyingType: { } u } && TypeMapper.IsSigned(u) ? L.True : L.False,
            "IsFlagsEnum" => t!.GetAttributes().Any(a => a.AttributeClass is { Name: "FlagsAttribute", ContainingNamespace.Name: "System" }) ? L.True : L.False,
            "EnumMemberJsonNames" => EnumMemberJsonNames(t!),
            "IsAbstractType" => t!.IsAbstract ? L.True : L.False,
            "IsEnumType" => JsonEnumSwitch(args[0], null),
            "BuiltInJsonConverter" => BuiltInJsonConverter(S(t!)),
            "IsInstanceOfType" => CallRuntime("csrt_type_is_instance", L.I1, [L.Ptr, L.Ptr], args[0], args[1]),
            "IsTypeAssignableTo" => CallRuntime("csrt_type_is_assignable", L.I1, [L.Ptr, L.Ptr], args[0], args[1]),
            "IsInterfaceTypeObject" => CallRuntime("csrt_type_is_interface", L.I1, [L.Ptr], args[0]),
            "CreateEnumConverter" => JsonEnumSwitch(args[0], args),
            "CompareStrings" => CallRuntime("csrt_string_compare_culture", L.I32, [L.Ptr, L.Ptr, L.I1], args[0], args[1], L.False),
            "UnhandledException" => UnhandledException(args[0]),
            "CaptureStackTrace" => CallRuntime("csrt_exception_capture_trace", L.Ptr, [L.Ptr], args[0]),
            "LibraryFrames" => LibraryFrames(args[0], inv, preset: true),
            "AppendLibraryFrames" => LibraryFrames(args[0], inv, preset: false),
            "ThrowWithStackTrace" => ThrowWithStackTrace(args[0], args[1]),
            "ResourceStrings" => _m.ResourceStringsBlob(),
            "IsCoroutineIterator" => CallRuntime("csrt_is_iterator", L.I1, [L.Ptr], args[0]),
            "RuntimeConfig" => _m.AppContextBlob(),
            "LoadableAssemblies" => _m.LoadableAssembliesBlob(),
            // The shared framework the program was compiled against ("11.0.0-rc.2.26504.105"), as NativeAOT
            // bakes in its System.Private.CoreLib's version.
            "TargetArchitecture" => L.ConstInt(L.I32, _m.Types.Target.Triple.StartsWith("x86_64") ? 1 : _m.Types.Target.Triple.StartsWith("wasm32") ? 5
                : _m.Types.Target.Triple.StartsWith("aarch64") || _m.Types.Target.Triple.StartsWith("arm64") ? 3 : 1),
            "FrameworkVersion" => _m.StringLiteral(Path.GetFileName(Frontend.SharedFramework.Directory?.TrimEnd('/', '\\') ?? "") is { Length: > 0 } version ? version : "11.0.0"),
            "TypeInfoTableAddress" => _b.Cast(LLVMOpcode.PtrToInt, _m.TypeInfoTable(), T.NativeInt),
            "ReflectionTableAddress" => _b.Cast(LLVMOpcode.PtrToInt, _m.ReflectionTable(), T.NativeInt),
            "DelegateMethodTableAddress" => _b.Cast(LLVMOpcode.PtrToInt, _m.DelegateMethodTable(), T.NativeInt),
            "DelegateSignatureTableAddress" => _b.Cast(LLVMOpcode.PtrToInt, _m.DelegateSignatureTable(), T.NativeInt),
            "DelegateFunction" => _b.Cast(LLVMOpcode.PtrToInt, LastDelegateField(args[0], 2), T.NativeInt),
            "DelegateTargetObject" => LastDelegateField(args[0], 1),
            "TryFaultAsyncTask" => CallRuntime("csrt_task_try_fault_async", L.I1, [L.Ptr, L.Ptr], args[0], args[1]),
            "CompareStringsWith" => CallRuntime("csrt_string_compare_with", L.I32, [L.Ptr, L.Ptr, L.I32], args[0], args[1], args[2]),
            "JsonConfigureHook" => ProgramHook("JsonConfigure", target, args),
            "JsonBuiltInHook" => ProgramHook("JsonBuiltIn", target, args),
            "EventSourceAttribute" => _b.Call(_m.EventSourceAttributeFunction().Type, _m.EventSourceAttributeFunction().Fn, [args[0], args[1]]),
            // A CancellationToken is its source object (null: None), so these are reinterpretations.
            "TokenSource" or "SourceToken" => args[0],
            // `extern R csrt_name(...)`: a direct call of that C runtime function (runtime/*.c).
            _ when target.Name.StartsWith("csrt_", StringComparison.Ordinal) => CallRuntime(target.Name,
                target.ReturnsVoid ? L.Void : T.Value(target.ReturnType), [.. target.Parameters.Select(p => T.Value(p.Type))], args),
            _ => throw CompileError.Unsupported($"corelib intrinsic '{target.Name}'", inv),
        };
    }

    /// <summary>
    /// CoreIntrinsics.*Hook: calls the CsharpcCoreLib.ProgramHooks member the front end generated into the
    /// program (Frontend/JsonReflectionContexts.cs); without one, nothing happens (null for a value). A
    /// precompiled corelib (ADR 0015) can't name code of the program it is linked into.
    /// </summary>
    private LLVMValueRef ProgramHook(string name, IMethodSymbol intrinsic, LLVMValueRef[] args)
    {
        var hooks = _m.Compilation.Assembly.GetTypeByMetadataName(CoreLib.Namespace + ".ProgramHooks");
        if (hooks?.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault() is not { } hook)
            return intrinsic.ReturnsVoid ? default : LlvmContext.Null(L.Ptr);
        return DirectCall(hook, args);
    }

    /// <summary>CoreIntrinsics.LibraryFrames: preset the exception's trace with these library frames (no-op without stack traces).</summary>
    private LLVMValueRef LibraryFrames(LLVMValueRef exception, IInvocationOperation inv, bool preset)
    {
        if (_m.StackTraces is not { } traces) return preset ? exception : L.False;
        var text = inv.Arguments.First(a => a.Parameter!.Ordinal == 1).Value.ConstantValue.Value as string
            ?? throw CompileError.Unsupported("LibraryFrames with a non-constant frame list", inv);
        var sites = text.Split('\n').Select(frame => traces.SiteDescriptor(_m, traces.LibrarySite(frame))).ToArray();
        var table = LlvmContext.ConstArray(L.Ptr, sites);
        var global = _m.Module.AddGlobal("csharpc.trace.library", Native.LLVMTypeOf(table), table, constant: true, linkage: LLVMLinkage.Internal);
        CallRuntime(preset ? "csrt_trace_preset" : "csrt_trace_append_library", L.Void, [L.Ptr, L.Ptr, L.I32], exception, global, L.ConstInt(L.I32, sites.Length));
        return preset ? exception : L.False;
    }

    private LLVMValueRef ThrowWithStackTrace(LLVMValueRef exception, LLVMValueRef captured)
    {
        CallRuntime("csrt_exception_throw_captured", L.Void, [L.Ptr, L.Ptr], exception, captured);
        return default;
    }

    private LLVMValueRef UnhandledException(LLVMValueRef exception)
    {
        CallRuntime("csrt_unhandled_exception", L.Void, [L.Ptr], exception);
        return default;
    }

    private LLVMValueRef EnumToUInt64(ITypeSymbol t, LLVMValueRef value)
    {
        if (t is not INamedTypeSymbol { EnumUnderlyingType: { } underlying }) throw CompileError.Unsupported($"EnumToUInt64 of non-enum '{t}'", (ISymbol)t);
        if (value.Type.IntWidth == 64) return value;
        return _b.Cast(TypeMapper.IsSigned(underlying) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, L.I64);
    }

    private LLVMValueRef EnumFromUInt64(ITypeSymbol t, LLVMValueRef value)
    {
        var target = T.Value(t);
        return target.IntWidth == 64 ? value : _b.Cast(LLVMOpcode.Trunc, value, target);
    }

    /// <summary>
    /// Type → enum dispatch for the non-generic JsonStringEnumConverter (which .NET drives with
    /// reflection): compares the Type with every enum the program declares or serializes, and either
    /// answers "is an enum" or calls JsonEnumFactory.Create&lt;E&gt;(options, policy, serializerOptions).
    /// </summary>
    /// <summary>
    /// JsonMetadataServices' converter for a built-in T, chosen at compile time: a run-time lookup by Type
    /// would link every primitive converter into each program that uses JSON.
    /// </summary>
    private LLVMValueRef BuiltInJsonConverter(ITypeSymbol t)
    {
        string? name = t.SpecialType switch
        {
            SpecialType.System_Int32 => "Int32Converter",
            SpecialType.System_String => "StringConverter",
            SpecialType.System_Int64 => "Int64Converter",
            SpecialType.System_Boolean => "BooleanConverter",
            SpecialType.System_Double => "DoubleConverter",
            SpecialType.System_Decimal => "DecimalConverter",
            SpecialType.System_Single => "SingleConverter",
            SpecialType.System_Int16 => "Int16Converter",
            SpecialType.System_Byte => "ByteConverter",
            SpecialType.System_SByte => "SByteConverter",
            SpecialType.System_UInt16 => "UInt16Converter",
            SpecialType.System_UInt32 => "UInt32Converter",
            SpecialType.System_UInt64 => "UInt64Converter",
            SpecialType.System_Char => "CharConverter",
            SpecialType.System_DateTime => "DateTimeConverter",
            SpecialType.System_Object => "ObjectConverter",
            _ => null,
        };
        name ??= t switch
        {
            IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte } => "ByteArrayConverter",
            INamedTypeSymbol n => n.ToDisplayString() switch
            {
                "System.TimeSpan" => "TimeSpanConverter",
                "System.DateTimeOffset" or CoreLib.Namespace + ".DateTimeOffsetImpl" => "DateTimeOffsetConverter",
                "System.Text.Json.JsonElement" or CoreLib.Namespace + ".JsonElementImpl" => "JsonElementConverter",
                "System.Text.Json.JsonDocument" or CoreLib.Namespace + ".JsonDocumentImpl" => "JsonDocumentConverter",
                "System.Text.Json.Nodes.JsonNode" or CoreLib.Namespace + ".JsonNodeImpl" => "JsonNodeConverter",
                "System.Text.Json.Nodes.JsonObject" or CoreLib.Namespace + ".JsonObjectImpl" => "JsonObjectConverter",
                "System.Text.Json.Nodes.JsonArray" or CoreLib.Namespace + ".JsonArrayImpl" => "JsonArrayConverter",
                "System.Text.Json.Nodes.JsonValue" or CoreLib.Namespace + ".JsonValueImpl" => "JsonValueConverter",
                _ => null,
            },
            _ => null,
        };
        if (name is null) return LlvmContext.Null(L.Ptr);
        var services = _m.Compilation.GetTypeByMetadataName(CoreLib.Namespace + ".JsonMetadataServicesImpl")
            ?? throw CompileError.Unsupported("corelib JsonMetadataServicesImpl not found", (ISymbol?)null!);
        var getter = services.GetMembers(name).OfType<IPropertySymbol>().Single().GetMethod!;
        return DirectCall(getter, []);
    }

    private LLVMValueRef JsonEnumSwitch(LLVMValueRef type, LLVMValueRef[]? createArgs)
    {
        var enums = _m.JsonEnumTypes();
        var resultType = createArgs is null ? L.I1 : L.Ptr;
        var result = NewTemp(resultType, "enum.switch");
        _b.Store(createArgs is null ? L.False : LlvmContext.Null(L.Ptr), result);
        var done = NewBlock("enum.done");
        IMethodSymbol? create = null;
        if (createArgs is not null)
            create = _m.Compilation.GetTypeByMetadataName(CoreLib.Namespace + ".JsonEnumFactory")?.GetMembers("Create").OfType<IMethodSymbol>().Single()
                ?? throw CompileError.Unsupported("corelib JsonEnumFactory not found", (ISymbol?)null!);
        foreach (var e in enums)
        {
            var hit = NewBlock("enum.hit");
            var next = NewBlock("enum.next");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, type, TypeObject(e, null!)), hit, next);
            _b.PositionAtEnd(hit);
            _b.Store(createArgs is null ? L.True : DirectCall(create!.Construct(e), createArgs[1..]), result);
            _b.Br(done);
            _b.PositionAtEnd(next);
        }
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(resultType, result);
    }

    /// <summary>[JsonStringEnumMemberName("...")] on each enum member (Enum.GetNames order), read at compile time.</summary>
    private LLVMValueRef EnumMemberJsonNames(ITypeSymbol t)
    {
        if (t is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) throw CompileError.Unsupported($"EnumMemberJsonNames of non-enum '{t}'", (ISymbol)t);
        var members = _m.EnumMembers(enumType);
        var fields = enumType.OriginalDefinition.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToDictionary(f => f.Name);
        var stringType = _m.Compilation.GetSpecialType(SpecialType.System_String);
        var array = NewArray(stringType, L.ConstInt(L.I64, members.Count));
        for (var i = 0; i < members.Count; i++)
        {
            var name = fields[members[i].Name].GetAttributes()
                .FirstOrDefault(a => a.AttributeClass is { Name: "JsonStringEnumMemberNameAttribute" })?.ConstructorArguments.FirstOrDefault().Value as string;
            _b.Store(name is null ? LlvmContext.Null(L.Ptr) : _m.StringLiteral(name),
                _b.InBoundsGep(T.ArrayHeader(L.Ptr), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, i)]));
        }
        return array;
    }

    /// <summary>
    /// Calls a static CsharpcCoreLib.&lt;type&gt;.&lt;method&gt; (an implementation moved from the C runtime to C#),
    /// picked by name and parameter count. Its body is lowered on demand.
    /// </summary>
    /// <summary>A generic corelib helper instantiated for <paramref name="typeArgument"/>.</summary>
    internal LLVMValueRef CallCoreLibGeneric(string type, string method, ITypeSymbol typeArgument, params LLVMValueRef[] args)
    {
        var owner = _m.Compilation.GetTypeByMetadataName(CoreLib.Namespace + "." + type)
            ?? throw CompileError.Unsupported($"corelib type '{type}' not found", (ISymbol?)null!);
        var target = owner.GetMembers(method).OfType<IMethodSymbol>().Single(m => m.IsStatic && m.Parameters.Length == args.Length && m.IsGenericMethod);
        return DirectCall(target.Construct(S(typeArgument)), args);
    }

    internal LLVMValueRef CallCoreLib(string type, string method, params LLVMValueRef[] args)
    {
        var owner = _m.Compilation.GetTypeByMetadataName(CoreLib.Namespace + "." + type)
            ?? throw CompileError.Unsupported($"corelib type '{type}' not found (is src/Csharpc/corelib shipped?)", (ISymbol?)null!);
        var target = owner.GetMembers(method).OfType<IMethodSymbol>().Single(m => m.IsStatic && m.Parameters.Length == args.Length);
        return DirectCall(target, args);
    }

    /// <summary>A generic static method of a corelib type, e.g. ("ArrayChecks", "CopyToUntyped", [T]).</summary>
    internal LLVMValueRef CallCoreLibGenericMethod(string type, string method, ITypeSymbol[] typeArguments, params LLVMValueRef[] args)
    {
        var owner = _m.Compilation.GetTypeByMetadataName(CoreLib.Namespace + "." + type)
            ?? throw CompileError.Unsupported($"corelib type '{type}' not found (is src/Csharpc/corelib shipped?)", (ISymbol?)null!);
        var target = owner.GetMembers(method).OfType<IMethodSymbol>()
            .Single(m => m.IsStatic && m.Parameters.Length == args.Length && m.TypeParameters.Length == typeArguments.Length);
        return DirectCall(target.Construct(typeArguments), args);
    }

    /// <summary>A static method of a (possibly generic) corelib type, e.g. ("ValueTaskSourceBridge`1", "Create", [T]).</summary>
    internal LLVMValueRef CallCoreLibGeneric(string metadataName, string method, ITypeSymbol[] typeArguments, params LLVMValueRef[] args)
    {
        var owner = _m.Compilation.GetTypeByMetadataName(CoreLib.Namespace + "." + metadataName)
            ?? throw CompileError.Unsupported($"corelib type '{metadataName}' not found (is src/Csharpc/corelib shipped?)", (ISymbol?)null!);
        if (typeArguments.Length > 0) owner = owner.Construct(typeArguments);
        var target = owner.GetMembers(method).OfType<IMethodSymbol>().Single(m => m.IsStatic && m.Parameters.Length == args.Length);
        return DirectCall(target, args);
    }

    /// <summary>
    /// string with `length` uninitialized chars (the GC never scans strings). The runtime helper throws
    /// OutOfMemoryException for negative lengths and above .NET's maximum string length, so no size
    /// computation in the corelib can turn into an undersized allocation.
    /// </summary>
    internal LLVMValueRef FastAllocateString(LLVMValueRef length)
        => CallRuntime("csrt_string_alloc_checked", L.Ptr, [L.I64], _b.Cast(LLVMOpcode.SExt, length, L.I64));

    /// <summary>Throws a library exception; code emitted afterwards lands in an unreachable block.</summary>
    private void ThrowAndContinue(string typeName, string message)
    {
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName(typeName)!, message);
        _b.PositionAtEnd(NewBlock("after.throw"));
    }

    /// <summary>(object)value for any T; value types without a boxed representation yet throw at run time only.</summary>
    private LLVMValueRef BoxOrThrow(ITypeSymbol t, LLVMValueRef value, IOperation at)
    {
        if (!t.IsValueType) return value;
        try { return Box(value, t, at); }
        catch (CompileError)
        {
            ThrowAndContinue("System.NotSupportedException", $"csharpc: boxing '{t}' is not supported yet.");
            return LlvmContext.Null(L.Ptr);
        }
    }

    /// <summary>(T)obj with a checked cast; types csharpc can't type-test yet throw at run time only.</summary>
    private LLVMValueRef CastOrThrow(ITypeSymbol t, LLVMValueRef obj, IOperation at)
    {
        if (t.IsValueType) return UnboxOrThrow(t, obj, at);
        try { return CastClass(obj, t, at); }
        catch (CompileError)
        {
            ThrowAndContinue("System.NotSupportedException", $"csharpc: casting to '{t}' is not supported yet.");
            return LlvmContext.Null(L.Ptr);
        }
    }

    private LLVMValueRef UnboxOrThrow(ITypeSymbol t, LLVMValueRef obj, IOperation at)
    {
        if (!t.IsValueType) return obj; // reference types: the corelib only uses this where the type is known
        try
        {
            var payload = CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], obj, MethodTableFor(t, at));
            return LoadValue(payload, t);
        }
        catch (CompileError)
        {
            ThrowAndContinue("System.NotSupportedException", $"csharpc: unboxing '{t}' is not supported yet.");
            return LlvmContext.Null(T.Value(t));
        }
    }

    /// <summary>
    /// `t == null` / `t != null` where an unconstrained T was substituted with a non-nullable value
    /// type: constant false / true (C# semantics) instead of boxing t.
    /// </summary>
    private LLVMValueRef? FoldValueTypeNullComparison(IBinaryOperation op)
    {
        if (op.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)) return null;
        static IOperation Peel(IOperation o) { while (o is IConversionOperation c) o = c.Operand; return o; }
        var (l, r) = (Peel(op.LeftOperand), Peel(op.RightOperand));
        var other = l.ConstantValue is { HasValue: true, Value: null } ? r : r.ConstantValue is { HasValue: true, Value: null } ? l : null;
        if (other is null || TypeOf(other) is not { IsValueType: true } t || TypeMapper.IsNullable(t)
            || t.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer) return null;
        EmitValue(other); // keep side effects
        return op.OperatorKind == BinaryOperatorKind.Equals ? L.False : L.True;
    }

    private IMethodSymbol ObjectMethod(string name, int parameters) =>
        _m.Compilation.GetSpecialType(SpecialType.System_Object).GetMembers(name).OfType<IMethodSymbol>()
            .First(m => !m.IsStatic && m.Parameters.Length == parameters);

    /// <summary>Implementation of IEquatable&lt;T&gt;.Equals / IComparable&lt;T&gt;.CompareTo on a user/corelib type, if any.</summary>
    private IMethodSymbol? GenericInterfaceImpl(ITypeSymbol t, string interfaceMetadataName, string member)
    {
        if (_m.Compilation.GetTypeByMetadataName(interfaceMetadataName) is not { } itfDef) return null;
        var itf = itfDef.Construct(t);
        if (!t.AllInterfaces.Contains(itf, SymbolEqualityComparer.Default)) return null;
        var slot = itf.GetMembers(member).OfType<IMethodSymbol>().First();
        return t.FindImplementationForInterfaceMember(slot) as IMethodSymbol is { } impl && _m.IsUserMethod(impl) ? impl : null;
    }

    private LLVMValueRef CallMethod(IMethodSymbol method, LLVMValueRef[] args)
    {
        var dispatch = ModuleCodeGen.HasThis(method) && (method.IsVirtual || method.IsOverride || method.IsAbstract)
            && !method.IsSealed && !method.ContainingType.IsSealed && method.ContainingType.TypeKind == TypeKind.Class;
        return dispatch ? VirtualCall(method, args[0], args) : DirectCall(method, args);
    }

    /// <summary>if (v == null) whenNull() else whenNotNull(), as a value of <paramref name="type"/>.</summary>
    private LLVMValueRef BranchOnNull(LLVMValueRef v, LLVMTypeRef type, Func<LLVMValueRef> whenNull, Func<LLVMValueRef> whenNotNull)
    {
        var result = NewTemp(type, "nullsel");
        var isNull = NewBlock("isnull");
        var notNull = NewBlock("notnull");
        var done = NewBlock("nulldone");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, v, LlvmContext.Null(L.Ptr)), isNull, notNull);
        _b.PositionAtEnd(isNull);
        _b.Store(whenNull(), result);
        _b.Br(done);
        _b.PositionAtEnd(notNull);
        _b.Store(whenNotNull(), result);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(type, result);
    }

    private static bool IsIntegral(ITypeSymbol t) => t.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
        or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    private ITypeSymbol EnumBase(ITypeSymbol t) => t is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } u } ? u : t;

    // ======================================================================================
    // Equality
    // ======================================================================================

    private LLVMValueRef EqualsOf(ITypeSymbol t, LLVMValueRef a, LLVMValueRef b, IOperation at)
    {
        t = _m.CoreLib.Map(t);
        t = EnumBase(t);
        if (IsIntegral(t) || Int128Support.Is128(t)) return _b.ICmp(LLVMIntPredicate.EQ, a, b);
        if (BclValueTypes.IsTicks(t)) return _b.ICmp(LLVMIntPredicate.EQ, BclValueTypes.TicksOf(this, t, a), BclValueTypes.TicksOf(this, t, b));
        if (BclValueTypes.IsBigInteger(t))
            return _b.ICmp(LLVMIntPredicate.EQ, CallRuntime("csrt_bigint_compare", L.I32, [L.Ptr, L.Ptr], a, b), L.ConstInt(L.I32, 0));
        if (t.SpecialType == SpecialType.System_Decimal) // decimal equality is by value (1.0m == 1.00m)
            return _b.ICmp(LLVMIntPredicate.EQ, CallRuntime("csrt_decimal_compare", L.I32, [L.Ptr, L.Ptr], Spill(a), Spill(b)), L.ConstInt(L.I32, 0));
        if (TypeMapper.IsFloatingPoint(t))
            return _b.BinOp(LLVMOpcode.Or, _b.FCmp(LLVMRealPredicate.OEQ, a, b),
                _b.BinOp(LLVMOpcode.And, _b.FCmp(LLVMRealPredicate.UNO, a, a), _b.FCmp(LLVMRealPredicate.UNO, b, b)));
        if (t.SpecialType == SpecialType.System_String)
            return CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], a, b);
        if (TypeMapper.IsMemory(t)) return MemoryEquals(a, b);
        if (t is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            // ValueTuple.Equals: Item1 && Item2 && ... — short-circuit once an element's Equals may run user code.
            var elements = Enumerable.Range(0, tuple.TupleElements.Length).Select(i => (Func<LLVMValueRef>)(() =>
                EqualsOf(tuple.TupleElements[i].Type, TupleElement(a, tuple, i), TupleElement(b, tuple, i), at))).ToList();
            if (!RunsNoUserCode(tuple, hash: false)) return ShortCircuit(isAnd: true, elements);
            var result = L.True;
            foreach (var element in elements) result = _b.BinOp(LLVMOpcode.And, result, element());
            return result;
        }
        if (TypeMapper.IsNullable(t))
        {
            // Nullable<T>: both null, or both set with equal values. Payloads are zero when empty, but
            // only compare them when both are set (keeps user Equals out of the null case).
            var u = ((INamedTypeSymbol)t).TypeArguments[0];
            var (ha, hb) = (NullableHas(a), NullableHas(b));
            var bothSet = _b.BinOp(LLVMOpcode.And, ha, hb);
            return ShortCircuit(isAnd: false,
            [
                () => _b.BinOp(LLVMOpcode.And, _b.Not(ha), _b.Not(hb)),
                () => ShortCircuit(isAnd: true, [() => bothSet, () => EqualsOf(u, NullablePayload(a, u), NullablePayload(b, u), at)]),
            ]);
        }

        var equatable = GenericInterfaceImpl(t, "System.IEquatable`1", "Equals");
        if (t.IsValueType)
        {
            if (equatable is not null) return CallMethod(equatable, [Spill(a), b]);
            if (UserStruct(t) is { } s) return ObjectEquals(s, a, b);  // its Equals override, or ValueType.Equals without boxing
            var boxA = Box(a, t, at);
            return VirtualCall(ObjectMethod("Equals", 1), boxA, [boxA, Box(b, t, at)]);
        }
        // (Object|Generic)EqualityComparer<T>.Equals: x.Equals(y) runs only when both are non-null.
        return BranchOnNull(a, L.I1,
            () => _b.ICmp(LLVMIntPredicate.EQ, b, LlvmContext.Null(L.Ptr)),
            () => ShortCircuit(isAnd: true,
            [
                () => _b.ICmp(LLVMIntPredicate.NE, b, LlvmContext.Null(L.Ptr)),
                () => equatable is not null ? CallMethod(equatable, [a, b]) : VirtualCall(ObjectMethod("Equals", 1), a, [a, b]),
            ]));
    }

    // ======================================================================================
    // Hashing
    // ======================================================================================

    internal LLVMValueRef HashOf(ITypeSymbol t, LLVMValueRef a, IOperation at)
    {
        t = _m.CoreLib.Map(t);
        t = EnumBase(t);
        var i32 = L.I32;
        if (t.SpecialType == SpecialType.System_Decimal) return CallRuntime("csrt_decimal_get_hash_code", i32, [L.Ptr], Spill(a));
        if (Int128Support.Is128(t)) // .NET combines the halves with the randomized HashCode: any fold of the bits will do
        {
            var folded = _b.BinOp(LLVMOpcode.Xor, _b.Cast(LLVMOpcode.Trunc, a, L.I64),
                _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.LShr, a, L.ConstInt(a.Type, 64)), L.I64));
            return _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.Xor, folded, _b.BinOp(LLVMOpcode.LShr, folded, L.ConstInt(L.I64, 32))), i32);
        }
        if (BclValueTypes.IsBigInteger(t)) return CallRuntime("csrt_bigint_get_hash_code", i32, [L.Ptr], a);
        if (BclValueTypes.IsTicks(t)) // TimeSpan/DateTime.GetHashCode: (int)ticks ^ (int)(ticks >> 32), DateTime's Kind excluded
        {
            a = BclValueTypes.TicksOf(this, t, a);
            return _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.Xor, a, _b.BinOp(LLVMOpcode.AShr, a, L.ConstInt(L.I64, 32))), i32);
        }
        switch (t.SpecialType)
        {
            case SpecialType.System_Boolean: return _b.Cast(LLVMOpcode.ZExt, a, i32);
            case SpecialType.System_Char or SpecialType.System_UInt16 or SpecialType.System_Byte:
            {
                var v = _b.Cast(LLVMOpcode.ZExt, a, i32);
                return t.SpecialType == SpecialType.System_Char ? _b.BinOp(LLVMOpcode.Or, v, _b.BinOp(LLVMOpcode.Shl, v, L.ConstInt(i32, 16))) : v;
            }
            case SpecialType.System_SByte or SpecialType.System_Int16: return _b.Cast(LLVMOpcode.SExt, a, i32);
            case SpecialType.System_Int32 or SpecialType.System_UInt32: return a;
            case SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr:
            {
                var lo = _b.Cast(LLVMOpcode.Trunc, a, i32);
                var hi = _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.LShr, a, L.ConstInt(a.Type, 32)), i32);
                return _b.BinOp(LLVMOpcode.Xor, lo, hi);
            }
            case SpecialType.System_Double or SpecialType.System_Single:
            {
                // double/float.GetHashCode: ±0 and every NaN (all Equal to each other) hash alike —
                // if (((bits - 1) & 0x7FF...F) >= 0x7FEF...F) bits &= 0x7FF0...0 (also maps ±infinity to itself).
                var isDouble = a.Type.Kind == LLVMTypeKind.Double;
                var bt = isDouble ? L.I64 : L.I32;
                var bits = _b.Cast(LLVMOpcode.BitCast, a, bt);
                var special = _b.ICmp(LLVMIntPredicate.UGE,
                    _b.BinOp(LLVMOpcode.And, _b.BinOp(LLVMOpcode.Sub, bits, L.ConstInt(bt, 1)), L.ConstInt(bt, isDouble ? long.MaxValue : int.MaxValue)),
                    L.ConstInt(bt, isDouble ? 0x7FEFFFFFFFFFFFFF : 0x7F7FFFFF));
                bits = _b.Select(special, _b.BinOp(LLVMOpcode.And, bits, L.ConstInt(bt, isDouble ? 0x7FF0000000000000 : 0x7F800000)), bits);
                if (!isDouble) return bits;
                return _b.BinOp(LLVMOpcode.Xor, _b.Cast(LLVMOpcode.Trunc, bits, i32),
                    _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.LShr, bits, L.ConstInt(L.I64, 32)), i32));
            }
            case SpecialType.System_String:
                return BranchOnNull(a, i32, () => L.ConstInt(i32, 0),
                    () => CallRuntime("csrt_string_get_hash_code", i32, [L.Ptr], a));
        }
        if (TypeMapper.IsMemory(t)) return MemoryHash(a);
        if (t is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            LLVMValueRef h = L.ConstInt(i32, 17);
            for (var i = 0; i < tuple.TupleElements.Length; i++)
                h = _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Mul, h, L.ConstInt(i32, 31)), HashOf(tuple.TupleElements[i].Type, TupleElement(a, tuple, i), at));
            return h;
        }
        if (TypeMapper.IsNullable(t))
        {
            var u = ((INamedTypeSymbol)t).TypeArguments[0];
            var has = NullableHas(a);
            if (RunsNoUserCode(u, hash: true)) return _b.Select(has, HashOf(u, NullablePayload(a, u), at), L.ConstInt(i32, 0));
            // An empty T? hashes to 0 without calling T's GetHashCode.
            var result = NewTemp(i32, "nullable.hash");
            _b.Store(L.ConstInt(i32, 0), result);
            var call = NewBlock("nullable.hash.call");
            var done = NewBlock("nullable.hash.done");
            _b.CondBr(has, call, done);
            _b.PositionAtEnd(call);
            _b.Store(HashOf(u, NullablePayload(a, u), at), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(i32, result);
        }
        var getHashCode = ObjectMethod("GetHashCode", 0);
        if (UserStruct(t) is { } s)
        {
            if (ModuleCodeGen.StructOverride(s, "GetHashCode") is { } overrider) return CallMethod(overrider, [Spill(a)]);
            var hash = _m.StructDefaultHash(s);
            return _b.Call(hash.Type, hash.Fn, [Spill(a)]);
        }
        if (t.IsValueType && Coroutines.IsTaskHandleType(t))
        {
            // A task-handle struct is the task / cancellation source pointer (docs/10-async.md): hash its
            // identity, like .NET's (reference-field) hash of these structs, and consistent with TaskHandleEquals.
            var address = _b.Cast(LLVMOpcode.PtrToInt, a, L.I64);
            return _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.Mul, _b.BinOp(LLVMOpcode.LShr, address, L.ConstInt(L.I64, 3)), L.ConstInt(L.I64, 2654435761)), i32);
        }
        if (t.IsValueType)
        {
            var box = Box(a, t, at);
            return VirtualCall(getHashCode, box, [box]);
        }
        return BranchOnNull(a, i32, () => L.ConstInt(i32, 0), () => VirtualCall(getHashCode, a, [a]));
    }

    // ======================================================================================
    // Ordering
    // ======================================================================================

    /// <summary>
    /// Calls a non-generic static corelib helper by name whose parameters are (string, IEnumerable&lt;string&gt;)-like:
    /// picks the overload whose parameter count matches and whose first parameter is a string.
    /// </summary>
    internal LLVMValueRef CallCoreLib(string typeName, string methodName, IOperation at, ITypeSymbol elementType, params LLVMValueRef[] args)
    {
        var type = _m.Compilation.GetTypeByMetadataName(CoreLib.Namespace + "." + typeName)
            ?? throw CompileError.Unsupported($"corelib type {typeName}", at);
        // Join(string, IEnumerable<string>) for strings, Join<T>(string, IEnumerable<T>) otherwise.
        var element = S(elementType);
        var isString = element.SpecialType == SpecialType.System_String;
        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic && m.IsGenericMethod != isString
            && m.Parameters.Length == args.Length && m.Parameters[0].Type.SpecialType == SpecialType.System_String
            && m.Parameters[^1].Type is INamedTypeSymbol { IsGenericType: true })
            ?? throw CompileError.Unsupported($"corelib helper {typeName}.{methodName}", at);
        return DirectCall(isString ? method : method.Construct(element), args);
    }

    private LLVMValueRef CompareOf(ITypeSymbol t, LLVMValueRef a, LLVMValueRef b, IOperation at)
    {
        t = _m.CoreLib.Map(t);
        t = EnumBase(t);
        var i32 = L.I32;
        LLVMValueRef Sign(LLVMValueRef gt, LLVMValueRef lt) =>
            _b.BinOp(LLVMOpcode.Sub, _b.Cast(LLVMOpcode.ZExt, gt, i32), _b.Cast(LLVMOpcode.ZExt, lt, i32));

        if (IsIntegral(t)) return BclValueTypes.IntegerCompareTo(this, a, b, TypeMapper.IsSigned(t));
        if (TypeMapper.IsFloatingPoint(t))
        {
            // double.CompareTo: NaN is smaller than everything (and equal to NaN).
            var ordered = Sign(_b.FCmp(LLVMRealPredicate.OGT, a, b), _b.FCmp(LLVMRealPredicate.OLT, a, b));
            var aNaN = _b.FCmp(LLVMRealPredicate.UNO, a, a);
            var bNaN = _b.FCmp(LLVMRealPredicate.UNO, b, b);
            var nanResult = _b.Select(aNaN, _b.Select(bNaN, L.ConstInt(i32, 0), L.ConstInt(i32, -1)), L.ConstInt(i32, 1));
            return _b.Select(_b.BinOp(LLVMOpcode.Or, aNaN, bNaN), nanResult, ordered);
        }
        if (t.SpecialType == SpecialType.System_String)
            return CallRuntime("csrt_string_compare_culture", i32, [L.Ptr, L.Ptr, L.I1], a, b, L.False);
        if (t.SpecialType == SpecialType.System_Decimal)
            return CallRuntime("csrt_decimal_compare", i32, [L.Ptr, L.Ptr], Spill(a), Spill(b));
        if (BclValueTypes.IsTicks(t))
        {
            (a, b) = (BclValueTypes.TicksOf(this, t, a), BclValueTypes.TicksOf(this, t, b));
            return Sign(_b.ICmp(LLVMIntPredicate.SGT, a, b), _b.ICmp(LLVMIntPredicate.SLT, a, b));
        }
        if (TypeMapper.IsNullable(t))
        {
            // Comparer<T?>.Default: null < any value; two nulls are equal.
            var u = ((INamedTypeSymbol)t).TypeArguments[0];
            var (ha, hb) = (NullableHas(a), NullableHas(b));
            var nullOrder = Sign(_b.BinOp(LLVMOpcode.And, ha, _b.Not(hb)), _b.BinOp(LLVMOpcode.And, _b.Not(ha), hb));
            var valueOrder = CompareOf(u, NullablePayload(a, u), NullablePayload(b, u), at);
            return _b.Select(_b.BinOp(LLVMOpcode.And, ha, hb), valueOrder, nullOrder);
        }
        if (BclValueTypes.IsBigInteger(t)) return CallRuntime("csrt_bigint_compare", i32, [L.Ptr, L.Ptr], a, b);
        if (t is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            // ValueTuple.CompareTo: lexicographic over the elements with Comparer<Ti>.Default.
            var parts = tuple.TupleElements.Select((e, i) => CompareOf(e.Type, TupleElement(a, tuple, i), TupleElement(b, tuple, i), at)).ToList();
            var result = parts[^1];
            for (var i = parts.Count - 2; i >= 0; i--)
                result = _b.Select(_b.ICmp(LLVMIntPredicate.NE, parts[i], L.ConstInt(i32, 0)), parts[i], result);
            return result;
        }

        var comparable = GenericInterfaceImpl(t, "System.IComparable`1", "CompareTo");
        var nonGeneric = comparable is null ? NonGenericComparable(t) : null;
        if (comparable is null && nonGeneric is null && t.TypeKind is not (TypeKind.Interface or TypeKind.TypeParameter) && t.SpecialType != SpecialType.System_Object)
        {
            // .NET throws InvalidOperationException("Failed to compare two elements in the array.") at runtime.
            ThrowAndContinue("System.InvalidOperationException", "Failed to compare two elements in the array.");
            return L.ConstInt(i32, 0);
        }

        LLVMValueRef Call()
        {
            if (comparable is not null) return CallMethod(comparable, [t.IsValueType ? Spill(a) : a, b]);
            if (nonGeneric is not null)
                return CallMethod(nonGeneric, [t.IsValueType ? Spill(a) : a, t.IsValueType ? Box(b, t, at) : b]);
            // Interface-/object-typed T: dispatch IComparable<T> at runtime when T itself is one; otherwise
            // .NET's Comparer<T>.Default is an ObjectComparer, i.e. the non-generic Comparer.Default.
            var itf = _m.Compilation.GetTypeByMetadataName("System.IComparable`1")!.Construct(t);
            if (!SymbolEqualityComparer.Default.Equals(t, itf) && !t.AllInterfaces.Contains(itf, SymbolEqualityComparer.Default))
                return CallCoreLib("ObjectComparison", "Compare", a, b);
            return InterfaceDispatch(itf.GetMembers("CompareTo").OfType<IMethodSymbol>().First(), a, [a, b]);
        }
        if (t.IsValueType) return Call();
        return BranchOnNull(a, i32,
            () => _b.Select(_b.ICmp(LLVMIntPredicate.EQ, b, LlvmContext.Null(L.Ptr)), L.ConstInt(i32, 0), L.ConstInt(i32, -1)),
            () => BranchOnNull(b, i32, () => L.ConstInt(i32, 1), Call));
    }

    private IMethodSymbol? NonGenericComparable(ITypeSymbol t)
    {
        var itf = _m.Compilation.GetTypeByMetadataName("System.IComparable");
        if (itf is null || !t.AllInterfaces.Contains(itf, SymbolEqualityComparer.Default)) return null;
        return t.FindImplementationForInterfaceMember(itf.GetMembers("CompareTo").First()) as IMethodSymbol is { } impl && _m.IsUserMethod(impl) ? impl : null;
    }
}
