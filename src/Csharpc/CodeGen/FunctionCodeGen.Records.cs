using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Bodies for the members C# synthesizes for records (they have no syntax to lower):
/// ToString/PrintMembers, Equals(object)/Equals(R)/GetHashCode/==/!=, EqualityContract, the copy
/// constructor and &lt;Clone&gt;$ (used by `with`), Deconstruct, and positional properties.
/// Semantics follow the C# spec's description of the synthesized members.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private INamedTypeSymbol RecordType => (INamedTypeSymbol)S(_method!.ContainingType);

    private bool IsRecordClass => !RecordType.IsValueType;

    /// <summary>The base record type, if this record derives from another record.</summary>
    private INamedTypeSymbol? BaseRecord => RecordType.BaseType is { IsRecord: true } b && _m.IsUserType(b) ? b : null;

    private IEnumerable<IFieldSymbol> RecordFields =>
        RecordType.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst);

    public void EmitRecordMember()
    {
        var m = _method!;
        switch (m.Name)
        {
            case "ToString": EmitRecordToString(); break;
            case "PrintMembers": EmitRecordPrintMembers(); break;
            case "get_EqualityContract": _b.Ret(_m.GetLayout(RecordType).MethodTable); break; // stands in for typeof(R)
            case "Equals" when m.Parameters[0].Type.SpecialType == SpecialType.System_Object: EmitRecordEqualsObject(); break;
            case "Equals" when SymbolEqualityComparer.Default.Equals(S(m.Parameters[0].Type), RecordType): EmitRecordEqualsTyped(); break;
            case "Equals": EmitRecordEqualsBase(); break;
            case "GetHashCode": EmitRecordGetHashCode(); break;
            case "op_Equality" or "op_Inequality": EmitRecordEqualityOperator(m.Name == "op_Equality"); break;
            case "<Clone>$": EmitRecordClone(); break;
            case ".ctor" when m.Parameters.Length == 1: EmitRecordCopyConstructor(); break;
            case "Deconstruct": EmitRecordDeconstruct(); break;
            default: throw CompileError.Unsupported($"synthesized record member '{m.Name}'", m);
        }
        Finish();
    }

    /// <summary>Positional records: the primary constructor initializes properties from its parameters.</summary>
    private void InitializePositionalProperties()
    {
        if (!_method!.ContainingType.IsRecord) return;
        foreach (var p in _method.Parameters)
        {
            var property = RecordType.GetMembers(p.Name).OfType<IPropertySymbol>()
                .FirstOrDefault(prop => prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax);
            if (property is null) continue;
            var field = BackingField(property);
            StoreValue(ToStorage(LoadValue(_slots[p.OriginalDefinition], S(p.Type)), S(field.Type)), FieldAddressOn(_this, field));
        }
    }

    private LLVMValueRef SelfFieldValue(LLVMValueRef self, IFieldSymbol field) => LoadValue(FieldAddressOn(self, field), S(field.Type));

    private LLVMValueRef Sb(string fn, LLVMValueRef sb, LLVMValueRef arg, LLVMTypeRef argType) =>
        CallRuntime(fn, L.Ptr, [L.Ptr, argType], sb, arg);

    private LLVMValueRef SbText(LLVMValueRef sb, string text) => Sb("csrt_sb_append_string", sb, _m.StringLiteral(text), L.Ptr);

    // ToString() => "R { " + (PrintMembers(sb) ? " " : "") + "}"
    private void EmitRecordToString()
    {
        var sb = CallRuntime("csrt_sb_new", L.Ptr, [L.I32], L.ConstInt(L.I32, 16));
        SbText(sb, RecordType.Name);
        SbText(sb, " { ");
        var printMembers = RecordType.GetMembers("PrintMembers").OfType<IMethodSymbol>().First();
        var any = RecordCall(printMembers, _this, [_this, sb]);
        var space = NewBlock("space");
        var close = NewBlock("close");
        _b.CondBr(any, space, close);
        _b.PositionAtEnd(space);
        SbText(sb, " ");
        _b.Br(close);
        _b.PositionAtEnd(close);
        SbText(sb, "}");
        _b.Ret(CallRuntime("csrt_sb_to_string", L.Ptr, [L.Ptr], sb));
    }

    // PrintMembers(sb): base members first, then "Name = value" for public fields/properties.
    private void EmitRecordPrintMembers()
    {
        var sb = _fn.Fn.Param(1);
        var members = RecordType.GetMembers().Where(member => member.DeclaredAccessibility == Accessibility.Public && !member.IsStatic && member switch
        {
            IFieldSymbol { IsConst: false } => true,
            IPropertySymbol { IsIndexer: false, GetMethod: not null, Name: not "EqualityContract" } => true,
            _ => false,
        }).ToList();

        LLVMValueRef anyBefore = L.False;
        if (BaseRecord is { } baseRecord && baseRecord.GetMembers("PrintMembers").OfType<IMethodSymbol>().FirstOrDefault() is { } basePrint)
        {
            anyBefore = DirectCall(basePrint, [_this, sb]);
            if (members.Count > 0)
            {
                var comma = NewBlock("comma");
                var next = NewBlock("members");
                _b.CondBr(anyBefore, comma, next);
                _b.PositionAtEnd(comma);
                SbText(sb, ", ");
                _b.Br(next);
                _b.PositionAtEnd(next);
            }
        }
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            SbText(sb, (i > 0 ? ", " : "") + member.Name + " = ");
            var (value, type) = member switch
            {
                IFieldSymbol f => (SelfFieldValue(_this, f), S(f.Type)),
                IPropertySymbol p => (DirectCall(S(p.GetMethod!), [_this]), S(p.Type)),
                _ => throw new InvalidOperationException(),
            };
            Sb("csrt_sb_append_string", sb, FormatValue(value, type, null!), L.Ptr);
        }
        _b.Ret(members.Count > 0 ? L.True : anyBefore);
    }

    /// <summary>EqualityComparer&lt;T&gt;.Default.Equals(a, b) for one field.</summary>
    private LLVMValueRef FieldEquals(LLVMValueRef a, LLVMValueRef b, ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], a, b);
        if (a.Type.IsFloatingPoint) // double.Equals: NaN equals NaN
            return _b.BinOp(LLVMOpcode.Or, _b.FCmp(LLVMRealPredicate.OEQ, a, b),
                _b.BinOp(LLVMOpcode.And, _b.FCmp(LLVMRealPredicate.UNO, a, a), _b.FCmp(LLVMRealPredicate.UNO, b, b)));
        if (a.Type.IsInteger) return _b.ICmp(LLVMIntPredicate.EQ, a, b);
        if (type.IsReferenceType)
        {
            // (object)a == b || (a != null && a.Equals(b))
            var same = _b.ICmp(LLVMIntPredicate.EQ, a, b);
            var objectEquals = _m.Compilation.GetSpecialType(SpecialType.System_Object).GetMembers("Equals").OfType<IMethodSymbol>()
                .First(e => e.Parameters.Length == 1 && !e.IsStatic);
            return ShortCircuit(isAnd: false,
            [
                () => same,
                () => ShortCircuit(isAnd: true,
                [
                    () => _b.ICmp(LLVMIntPredicate.NE, a, LlvmContext.Null(L.Ptr)),
                    () => VirtualCall(objectEquals, a, [a, b]),
                ]),
            ]);
        }
        // Other value types (structs, nullables, tuples): box both and use the virtual Equals.
        var objEquals = _m.Compilation.GetSpecialType(SpecialType.System_Object).GetMembers("Equals").OfType<IMethodSymbol>()
            .First(e => e.Parameters.Length == 1 && !e.IsStatic);
        var boxedA = Box(a, type, null!);
        return VirtualCall(objEquals, boxedA, [boxedA, Box(b, type, null!)]);
    }

    private LLVMValueRef FieldHash(LLVMValueRef v, ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return _b.Select(_b.ICmp(LLVMIntPredicate.EQ, v, LlvmContext.Null(L.Ptr)), L.ConstInt(L.I32, 0),
                CallRuntime("csrt_string_get_hash_code_or_zero", L.I32, [L.Ptr], v));
        if (v.Type.IsFloatingPoint) v = _b.Cast(LLVMOpcode.BitCast, v, v.Type.Kind == LLVMTypeKind.Double ? L.I64 : L.I32);
        if (v.Type.IsInteger)
            return v.Type.IntWidth switch
            {
                64 => _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.Xor, v, _b.BinOp(LLVMOpcode.LShr, v, L.ConstInt(L.I64, 32))), L.I32),
                32 => v,
                _ => _b.Cast(TypeMapper.IsSigned(type) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, v, L.I32),
            };
        if (type.IsReferenceType) return CallRuntime("csrt_object_get_hash_code_or_zero", L.I32, [L.Ptr], v);
        var boxed = Box(v, type, null!);
        return CallRuntime("csrt_object_get_hash_code_or_zero", L.I32, [L.Ptr], boxed);
    }

    // Equals(R? other)
    private void EmitRecordEqualsTyped()
    {
        var other = _fn.Fn.Param(1);
        if (!IsRecordClass)
        {
            // record struct: `other` is the struct value; compare fields.
            var otherAddress = Spill(other);
            LLVMValueRef all = L.True;
            foreach (var f in RecordFields)
                all = _b.BinOp(LLVMOpcode.And, all, FieldEquals(SelfFieldValue(_this, f), SelfFieldValue(otherAddress, f), S(f.Type)));
            _b.Ret(all);
            return;
        }
        List<Func<LLVMValueRef>> conditions = [];
        if (BaseRecord is { } baseRecord)
        {
            var baseEquals = baseRecord.GetMembers("Equals").OfType<IMethodSymbol>()
                .First(e => e.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(e.Parameters[0].Type, baseRecord));
            conditions.Add(() => DirectCall(baseEquals, [_this, other]));
        }
        else
        {
            conditions.Add(() => _b.ICmp(LLVMIntPredicate.NE, other, LlvmContext.Null(L.Ptr)));
            // EqualityContract == other.EqualityContract  <=>  same runtime type
            conditions.Add(() => _b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, _this), _b.Load(L.Ptr, other)));
        }
        foreach (var field in RecordFields)
        {
            var f = field;
            conditions.Add(() => FieldEquals(SelfFieldValue(_this, f), SelfFieldValue(other, f), S(f.Type)));
        }
        var same = _b.ICmp(LLVMIntPredicate.EQ, _this, other);
        _b.Ret(ShortCircuit(isAnd: false, [() => same, () => ShortCircuit(isAnd: true, conditions)]));
    }

    /// <summary>Synthesized members of sealed records (and record structs) are not virtual: call them directly.</summary>
    private LLVMValueRef RecordCall(IMethodSymbol method, LLVMValueRef self, LLVMValueRef[] args) =>
        IsRecordClass && (method.IsVirtual || method.IsOverride || method.IsAbstract) && !method.IsSealed && !RecordType.IsSealed
            ? VirtualCall(method, self, args)
            : DirectCall(method, args);

    private IMethodSymbol TypedEquals => RecordType.GetMembers("Equals").OfType<IMethodSymbol>()
        .First(e => e.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(S(e.Parameters[0].Type), RecordType));

    // Equals(object? obj) => Equals(obj as R)
    private void EmitRecordEqualsObject()
    {
        var obj = _fn.Fn.Param(1);
        if (!IsRecordClass)
        {
            // record struct: obj is R r && Equals(r)
            var mt = _m.StructMethodTable(RecordType);
            var isR = _b.ICmp(LLVMIntPredicate.NE, CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], obj, mt), LlvmContext.Null(L.Ptr));
            _b.Ret(ShortCircuit(isAnd: true,
            [
                () => isR,
                () => DirectCall(TypedEquals, [_this, LoadValue(_b.InBoundsGep(L.I8, obj, [L.ConstInt(L.I64, 8)]), RecordType)]),
            ]));
            return;
        }
        var asR = CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], obj, _m.GetLayout(RecordType).MethodTable);
        _b.Ret(RecordCall(TypedEquals, _this, [_this, asR]));
    }

    // Derived record: sealed override bool Equals(Base? other) => Equals((object?)other)
    private void EmitRecordEqualsBase()
    {
        var objectEquals = RecordType.GetMembers("Equals").OfType<IMethodSymbol>().First(e => e.Parameters[0].Type.SpecialType == SpecialType.System_Object);
        _b.Ret(VirtualCall(objectEquals, _this, [_this, _fn.Fn.Param(1)]));
    }

    private void EmitRecordGetHashCode()
    {
        LLVMValueRef hash;
        if (BaseRecord is { } baseRecord)
            hash = DirectCall(baseRecord.GetMembers("GetHashCode").OfType<IMethodSymbol>().First(g => g.Parameters.IsEmpty), [_this]);
        else if (IsRecordClass)
            hash = CallRuntime("csrt_pointer_hash", L.I32, [L.Ptr], _b.Load(L.Ptr, _this)); // EqualityContract
        else
            hash = L.ConstInt(L.I32, 0);
        foreach (var f in RecordFields)
            hash = _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Mul, hash, L.ConstInt(L.I32, -1521134295)), FieldHash(SelfFieldValue(_this, f), S(f.Type)));
        _b.Ret(hash);
    }

    // operator ==(l, r) => (object)l == r || (l is not null && l.Equals(r))
    private void EmitRecordEqualityOperator(bool equality)
    {
        var (l, r) = (_fn.Fn.Param(0), _fn.Fn.Param(1));
        LLVMValueRef result;
        if (!IsRecordClass)
        {
            var spilled = Spill(l);
            result = DirectCall(TypedEquals, [spilled, r]);
        }
        else
        {
            var same = _b.ICmp(LLVMIntPredicate.EQ, l, r);
            result = ShortCircuit(isAnd: false,
            [
                () => same,
                () => ShortCircuit(isAnd: true, [() => _b.ICmp(LLVMIntPredicate.NE, l, LlvmContext.Null(L.Ptr)), () => RecordCall(TypedEquals, l, [l, r])]),
            ]);
        }
        _b.Ret(equality ? result : _b.Not(result));
    }

    // <Clone>$() => new R(this)
    private void EmitRecordClone()
    {
        var layout = _m.GetLayout(RecordType);
        var copy = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
        var copyCtor = RecordType.InstanceConstructors.First(c => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(S(c.Parameters[0].Type), RecordType));
        DirectCall(copyCtor, [copy, _this]);
        _b.Ret(copy);
    }

    // protected R(R original): base copy constructor, then copy this record's fields.
    private void EmitRecordCopyConstructor()
    {
        var original = _fn.Fn.Param(1);
        if (BaseRecord is { } baseRecord)
        {
            var baseCopy = baseRecord.InstanceConstructors.First(c => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, baseRecord));
            DirectCall(baseCopy, [_this, original]);
        }
        foreach (var f in RecordFields)
            StoreValue(ToStorage(SelfFieldValue(original, f), S(f.Type)), FieldAddressOn(_this, f));
        _b.RetVoid();
    }

    private IPropertySymbol FindRecordProperty(string name)
    {
        for (INamedTypeSymbol? t = RecordType; t is not null; t = t.BaseType)
            if (t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } found) return found;
        throw CompileError.Unsupported($"record property '{name}'", _method!);
    }

    // Deconstruct(out T1 p1, ...): from the positional properties.
    private void EmitRecordDeconstruct()
    {
        uint index = 1;
        foreach (var p in _method!.Parameters)
        {
            var property = FindRecordProperty(p.Name);
            var value = DirectCall(S(property.GetMethod!), [_this]);
            StoreValue(ToStorage(value, S(property.Type)), _fn.Fn.Param(index++));
        }
        _b.RetVoid();
    }
}
