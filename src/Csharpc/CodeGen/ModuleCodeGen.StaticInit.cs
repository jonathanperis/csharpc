using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Static initialization with .NET's timing (ECMA-335 II.10.5.3.3, docs/03-lowering.md):
///
///  * A type with an explicit static constructor runs its static field initializers and then the
///    constructor exactly once, on the first call of one of its static methods or constructors (or,
///    for a struct, instance methods — CoreCLR does that too). Those functions start with a check.
///  * A type with static field initializers only (C# marks it beforefieldinit) is initialized on the
///    first access to one of its static fields — CoreCLR's behavior; method calls don't trigger it.
///    If every initializer is a side-effect-free constant expression, the timing is unobservable
///    and the type is initialized at startup instead, so its fields need no checks.
///
/// Each lazily initialized type has a csrt_ClassInit { i32 state, ptr owner, ptr exception } global
/// (src/runtime/threads.c). The fast path is one acquire load and compare against "done"; the slow
/// path (`__csharpc_cctor_check.T`) runs the initializer through csrt_class_init_begin/end, which
/// serialize threads and let the initializing thread re-enter (cycles see default values, as in
/// .NET). An exception escaping the initializer becomes a TypeInitializationException that is
/// cached and rethrown on every later access, like the CLR does.
/// </summary>
public sealed partial class ModuleCodeGen
{
    public sealed record ClassInit(INamedTypeSymbol Type, LLVMValueRef State, LoweredFunction Check, LoweredFunction Body, bool Precise);

    private readonly Dictionary<INamedTypeSymbol, ClassInit?> _classInits = new(SymbolEqualityComparer.Default);
    private readonly Queue<ClassInit> _pendingClassInits = new();

    /// <summary>The lazy-initialization state of a user type, or null if it needs no checks.</summary>
    public ClassInit? ClassInitFor(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (_classInits.TryGetValue(type, out var existing)) return existing;
        ClassInit? init = null;
        if (IsUserType(type) && !CoreLib.IsCoreLib(type) && type.TypeKind is TypeKind.Class or TypeKind.Struct
            && !TypeSubstitution.IsGenericContext(type))
        {
            if (IsIlType(type))
            {
                // IL: the .cctor holds the field initializers too; beforefieldinit decides the trigger.
                if (HasExplicitStaticConstructor(type)) init = CreateClassInit(type, precise: !IlBeforeFieldInit(type));
            }
            else
            {
                var precise = HasExplicitStaticConstructor(type);
                if (precise || (StaticInitializers(type).Any() && !HasConstantStaticInitializers(type)))
                    init = CreateClassInit(type, precise);
            }
        }
        return _classInits[type] = init;
    }

    private static bool HasExplicitStaticConstructor(INamedTypeSymbol type) =>
        type.StaticConstructors.Any(c => !c.IsImplicitlyDeclared);

    /// <summary>Types initialized eagerly at startup: initializers only, all side-effect-free constants.</summary>
    private bool IsEagerlyInitialized(INamedTypeSymbol type) =>
        !HasExplicitStaticConstructor(type) && HasConstantStaticInitializers(type);

    /// <summary>Does this function trigger its type's static constructor on entry?</summary>
    public bool TriggersClassInitOnEntry(IMethodSymbol method) =>
        !IsNestedFunction(method) && method.MethodKind != MethodKind.StaticConstructor
        && (method.IsStatic || method.MethodKind == MethodKind.Constructor || method.ContainingType.IsValueType)
        && ClassInitFor(method.ContainingType) is { Precise: true };

    private IEnumerable<EqualsValueClauseSyntax> StaticInitializers(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (!member.IsStatic || member is IFieldSymbol { IsConst: true } || member.IsImplicitlyDeclared) continue;
            var init = member switch
            {
                IFieldSymbol f => f.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax v ? v.Initializer : null,
                IPropertySymbol p => p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax pd ? pd.Initializer : null,
                _ => null,
            };
            if (init is not null) yield return init;
        }
    }

    /// <summary>
    /// Every static initializer is a constant, default value, typeof, or an array of those: running
    /// it can't have side effects or observe other types, so when it runs is unobservable.
    /// </summary>
    private bool HasConstantStaticInitializers(INamedTypeSymbol type) =>
        StaticInitializers(type).All(init => ModelFor(init.SyntaxTree).GetOperation(init.Value) is { } op && IsConstantExpression(op));

    private static bool IsConstantExpression(IOperation op) => op switch
    {
        { ConstantValue.HasValue: true } => true,
        IDefaultValueOperation or ITypeOfOperation => true,
        IConversionOperation { OperatorMethod: null } c => IsConstantExpression(c.Operand),
        IArrayCreationOperation a => a.DimensionSizes.All(d => d.ConstantValue.HasValue)
            && (a.Initializer is null || a.Initializer.ElementValues.All(IsConstantExpression)),
        IArrayInitializerOperation i => i.ElementValues.All(IsConstantExpression),
        _ => false,
    };

    private ClassInit CreateClassInit(INamedTypeSymbol type, bool precise)
    {
        var name = Mangler.MetadataName(type);
        // csrt_ClassInit { i32 state, i32 pad, ptr owner, ptr exception } — a GC root (writable data).
        var stateType = Llvm.Struct(Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.Ptr);
        var state = Module.AddGlobal("csrt.cctor.state." + name, stateType, LlvmContext.Null(stateType), linkage: LLVMLinkage.Internal);
        Native.LLVMSetAlignment(state, 8);

        var bodyType = Llvm.FunctionType(Llvm.Void, Llvm.Ptr);
        var body = Module.AddFunction("__csharpc_cctor_body." + name, bodyType);
        Native.LLVMSetLinkage(body, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(body, "uwtable", 2);

        var checkType = Llvm.FunctionType(Llvm.Void);
        var check = Module.AddFunction("__csharpc_cctor_check." + name, checkType);
        Native.LLVMSetLinkage(check, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(check, "uwtable", 2);
        Llvm.AddFunctionAttribute(check, "noinline");
        Llvm.AddFunctionAttribute(check, "cold");

        var init = new ClassInit(type, state, new LoweredFunction(check, checkType), new LoweredFunction(body, bodyType), precise);
        _pendingClassInits.Enqueue(init);
        return init;
    }

    /// <summary>Emits the bodies of newly referenced class initializers; true if any.</summary>
    private bool EmitPendingClassInits()
    {
        var any = false;
        while (_pendingClassInits.TryDequeue(out var init))
        {
            any = true;
            Guard(init.Type, () => EmitClassInit(init));
        }
        return any;
    }

    private void EmitClassInit(ClassInit init)
    {
        // The initializer: static field initializers in declaration order, then the static constructor.
        var graphs = InitializerGraphs(init.Type, isStatic: true).ToList();
        var cctors = init.Type.StaticConstructors.Where(c => !c.IsImplicitlyDeclared).ToList();
        new FunctionCodeGen(this, null, init.Body, initializingType: init.Type).EmitStaticInit(graphs, cctors);

        // The slow path.
        var fn = init.Check.Fn;
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        var run = b.AppendBlock(fn, "run");
        var done = b.AppendBlock(fn, "done");
        var failed = b.AppendBlock(fn, "failed");
        var other = b.AppendBlock(fn, "other");
        var begin = Runtime("csrt_class_init_begin", Llvm.I32, Llvm.Ptr);
        var status = b.Call(begin.Type, begin.Fn, [init.State]);
        b.CondBr(b.ICmp(LLVMIntPredicate.EQ, status, Llvm.ConstInt(Llvm.I32, 0)), run, other);
        b.PositionAtEnd(other);
        b.CondBr(b.ICmp(LLVMIntPredicate.EQ, status, Llvm.ConstInt(Llvm.I32, 2)), failed, done);

        b.PositionAtEnd(run);
        EmitCallCatchingHelpers(); // the module's definition catches C# exceptions (the runtime's weak one can't)
        var catching = Runtime("csrt_call_catching_void", Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
        var exception = b.Call(catching.Type, catching.Fn, [init.Body.Fn, LlvmContext.Null(Llvm.Ptr)]);
        var end = Runtime("csrt_class_init_end", Llvm.Void, Llvm.Ptr, Llvm.Ptr);
        var threw = b.AppendBlock(fn, "threw");
        var succeeded = b.AppendBlock(fn, "succeeded");
        b.CondBr(b.ICmp(LLVMIntPredicate.EQ, exception, LlvmContext.Null(Llvm.Ptr)), succeeded, threw);
        b.PositionAtEnd(succeeded);
        b.Call(end.Type, end.Fn, [init.State, LlvmContext.Null(Llvm.Ptr)]);
        b.Br(done);

        b.PositionAtEnd(threw);
        var tieType = Compilation.GetTypeByMetadataName("System.TypeInitializationException")!;
        var layout = GetLayout(tieType);
        var alloc = Runtime("csrt_alloc", Llvm.Ptr, Llvm.Ptr, Llvm.I64);
        var tie = b.Call(alloc.Type, alloc.Fn, [layout.MethodTable, Llvm.ConstInt(Llvm.I64, (long)layout.InstanceSize)]);
        var exceptionInit = Runtime("csrt_exception_init", Llvm.Void, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
        b.Call(exceptionInit.Type, exceptionInit.Fn,
            [tie, StringLiteral($"The type initializer for '{Mangler.MetadataName(init.Type)}' threw an exception."), exception, LlvmContext.Null(Llvm.Ptr)]);
        b.Call(end.Type, end.Fn, [init.State, tie]);
        var throwObject = Runtime("csrt_throw_object", Llvm.Void, Llvm.Ptr);
        b.Call(throwObject.Type, throwObject.Fn, [tie]);
        b.Unreachable();

        b.PositionAtEnd(failed);
        // Every later access rethrows the same TypeInitializationException.
        var cached = b.Load(Llvm.Ptr, b.StructGep(Llvm.Struct(Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.Ptr), init.State, 3));
        b.Call(throwObject.Type, throwObject.Fn, [cached]);
        b.Unreachable();

        b.PositionAtEnd(done);
        b.RetVoid();
    }
}
