using System.Reflection.Metadata;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>IL instruction lowering (ECMA-335 partition III); see FunctionCodeGen.Il.cs for the block structure.</summary>
public sealed partial class FunctionCodeGen
{
    private ITypeSymbol Special(SpecialType type) => _m.Compilation.GetSpecialType(type);

    /// <summary>Lowers one instruction; true when it ended the block (branch, return, throw...).</summary>
    private bool EmitIlInstruction(IlInstruction instruction, Stack<IlEntry> stack, IlBlock block)
    {
        var il = _il!;
        IlEntry Pop() => stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");
        void Push(IlEntry e) => stack.Push(e);
        var op = instruction.OpCode;

        // Prefixes modify the next instruction only.
        var constrained = il.Constrained;
        var isVolatile = il.Volatile;
        if (op is not (ILOpCode.Constrained or ILOpCode.Volatile or ILOpCode.Unaligned or ILOpCode.Readonly or ILOpCode.Tail))
        {
            il.Constrained = null;
            il.Volatile = false;
            il.Tail = false;
        }

        switch (op)
        {
            case ILOpCode.Nop or ILOpCode.Break or ILOpCode.Readonly or ILOpCode.Unaligned: return false;
            case ILOpCode.Volatile: il.Volatile = true; return false;
            case ILOpCode.Tail:
                // F#-style code relies on `tail.` for loops: a normal call grows the stack where CoreCLR doesn't.
                if (!il.WarnedTail)
                {
                    il.WarnedTail = true;
                    _m.Warnings.Add(new CompileError("CSLLVM0006",
                        $"`tail.` calls are compiled as ordinary calls: deep tail recursion can overflow the stack where .NET would not [in '{IlBody.Method.OriginalDefinition}' ({IlAssembly.Name}.dll)]",
                        il.Syntax.GetLocation()));
                }
                il.Tail = true;
                return false;
            case ILOpCode.Constrained: il.Constrained = ResolveIlType(instruction.Token); return false;

            // ---- constants ----
            case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3
                or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8:
                Push(I4((int)op - (int)ILOpCode.Ldc_i4_0) with { Constant = (int)op - (int)ILOpCode.Ldc_i4_0 }); // ILOpCode is ushort-based: subtract as int (ldc.i4.m1)
                return false;
            case ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4: Push(I4((int)instruction.Int) with { Constant = (int)instruction.Int }); return false;
            case ILOpCode.Ldc_i8: Push(new IlEntry(IlKind.Int64, L.ConstInt(L.I64, instruction.Int), Special(SpecialType.System_Int64)) { Constant = instruction.Int }); return false;
            case ILOpCode.Ldc_r4: Push(new IlEntry(IlKind.Float, LlvmContext.ConstReal(L.F32, instruction.Real), Special(SpecialType.System_Single)) { Constant = (float)instruction.Real }); return false;
            case ILOpCode.Ldc_r8: Push(new IlEntry(IlKind.Float, LlvmContext.ConstReal(L.F64, instruction.Real), Special(SpecialType.System_Double)) { Constant = instruction.Real }); return false;
            case ILOpCode.Ldnull: Push(new IlEntry(IlKind.Ref, LlvmContext.Null(L.Ptr), null)); return false;
            case ILOpCode.Ldstr:
            {
                var text = IlAssembly.UserString(instruction.Token);
                Push(new IlEntry(IlKind.Ref, _m.StringLiteral(text), Special(SpecialType.System_String)) { NonNull = true, Constant = text });
                return false;
            }

            // ---- arguments and locals ----
            case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3: Push(IlLoadArg(op - ILOpCode.Ldarg_0)); return false;
            case ILOpCode.Ldarg_s or ILOpCode.Ldarg: Push(IlLoadArg((int)instruction.Int)); return false;
            case ILOpCode.Ldarga_s or ILOpCode.Ldarga: Push(IlArgAddress((int)instruction.Int)); return false;
            case ILOpCode.Starg_s or ILOpCode.Starg: IlStoreArg((int)instruction.Int, Pop()); return false;
            case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3: Push(IlLoadLocal(op - ILOpCode.Ldloc_0)); return false;
            case ILOpCode.Ldloc_s or ILOpCode.Ldloc: Push(IlLoadLocal((int)instruction.Int)); return false;
            case ILOpCode.Ldloca_s or ILOpCode.Ldloca:
            {
                var index = (int)instruction.Int;
                var local = IlLocal(index);
                if (local.IsByRef) throw new IlException("address of a by-ref local");
                Push(new IlEntry(IlKind.ByRef, il.Locals[index], local.Type));
                return false;
            }
            case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3: IlStoreLocal(op - ILOpCode.Stloc_0, Pop()); return false;
            case ILOpCode.Stloc_s or ILOpCode.Stloc: IlStoreLocal((int)instruction.Int, Pop()); return false;

            // ---- stack ----
            case ILOpCode.Dup: { var top = Pop(); Push(top); Push(top); return false; }
            case ILOpCode.Pop: Pop(); return false;

            // ---- arithmetic ----
            case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un or ILOpCode.Rem or ILOpCode.Rem_un
                or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf
                or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un:
            {
                var r = Pop();
                var l = Pop();
                Push(IlBinary(op, l, r));
                return false;
            }
            case ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un:
            {
                var amount = Pop();
                var value = Pop();
                Push(IlShift(op, value, amount));
                return false;
            }
            case ILOpCode.Neg:
            {
                var v = Pop();
                Push(v with { Value = v.Kind == IlKind.Float ? _b.FNeg(v.Value) : _b.Neg(IlInt(v).Value), Symbol = null, NonNull = false });
                return false;
            }
            case ILOpCode.Not: { var v = IlInt(Pop()); Push(v with { Value = _b.Not(v.Value) }); return false; }

            // ---- comparisons ----
            case ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un:
            {
                var r = Pop();
                var l = Pop();
                var result = IlCompare(op switch
                {
                    ILOpCode.Ceq => ILOpCode.Beq, ILOpCode.Cgt => ILOpCode.Bgt, ILOpCode.Cgt_un => ILOpCode.Bgt_un,
                    ILOpCode.Clt => ILOpCode.Blt, _ => ILOpCode.Blt_un,
                }, l, r);
                Push(I4Value(_b.Cast(LLVMOpcode.ZExt, result, L.I32), Special(SpecialType.System_Boolean)));
                return false;
            }

            // ---- branches ----
            case ILOpCode.Br or ILOpCode.Br_s:
            {
                var target = IlTarget(instruction.Target);
                IlTransfer(target, stack);
                _b.Br(target.Llvm);
                return true;
            }
            case ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s:
            {
                if (IlFoldedBranch(block, _ilIndex) is { } only) // a feature switch: the other way is never lowered
                {
                    Pop();
                    var target = IlTarget(only);
                    IlTransfer(target, stack);
                    _b.Br(target.Llvm);
                    return true;
                }
                var v = Pop();
                var nonZero = IlIsNonZero(v);
                IlCondBranch(op is ILOpCode.Brtrue or ILOpCode.Brtrue_s ? nonZero : _b.Not(nonZero), instruction, stack);
                return true;
            }
            case ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s or ILOpCode.Bge or ILOpCode.Bge_s
                or ILOpCode.Bge_un or ILOpCode.Bge_un_s or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s
                or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s or ILOpCode.Blt or ILOpCode.Blt_s
                or ILOpCode.Blt_un or ILOpCode.Blt_un_s:
            {
                var r = Pop();
                var l = Pop();
                // C# compilers branch *around* a `then` block on the negated condition. Branching on the
                // condition itself into the fall-through keeps the comparison in the shape the source
                // path (and clang) produce, which LLVM canonicalizes and selects identically.
                var forward = instruction.Target > instruction.Offset;
                IlCondBranch(IlCompare(LongForm(op), l, r, negate: forward), instruction, stack, swap: forward);
                return true;
            }
            case ILOpCode.Switch:
            {
                var value = IlInt(Pop());
                if (value.Kind != IlKind.Int32) value = value with { Value = _b.Cast(LLVMOpcode.Trunc, value.Value, L.I32) };
                var fallthrough = IlTarget(instruction.Next);
                var targets = instruction.Targets!.Select(IlTarget).ToList();
                foreach (var t in targets.Distinct()) IlTransfer(t, stack);
                IlTransfer(fallthrough, stack);
                _b.Switch(value.Value, fallthrough.Llvm, [.. targets.Select((t, i) => (L.ConstInt(L.I32, i), t.Llvm))]);
                return true;
            }
            case ILOpCode.Ret:
            {
                if (_coro is not null)
                {
                    IlCoroutineReturn(stack);
                    return true;
                }
                if (_method!.ReturnsVoid || _method.MethodKind == MethodKind.Constructor)
                {
                    if (stack.Count > 0 && _method.MethodKind != MethodKind.Constructor) throw new IlException("stack not empty at ret");
                    _b.RetVoid();
                    return true;
                }
                var value = Pop();
                var returnType = _method.OriginalDefinition.ReturnType;
                _b.Ret(_method.RefKind != RefKind.None ? IlAddress(value) : IlTyped(value, returnType));
                return true;
            }
            case ILOpCode.Leave or ILOpCode.Leave_s:
                stack.Clear();
                IlLeave(instruction.Offset, instruction.Target);
                return true;
            case ILOpCode.Endfinally:
            {
                var clause = IlHandlerOf(block);
                if (clause.Kind is not (ExceptionRegionKind.Finally or ExceptionRegionKind.Fault)) throw new IlException("endfinally outside a finally/fault handler");
                stack.Clear();
                il.FinallyEnds.Add((_b.InsertBlock, clause)); // terminated once every entry is known
                return true;
            }
            case ILOpCode.Endfilter:
            {
                var clause = IlHandlerOf(block);
                if (clause.Kind != ExceptionRegionKind.Filter) throw new IlException("endfilter outside a filter");
                var accept = IlIsNonZero(Pop());
                if (clause.Reject.IsNull) throw new IlException("filter without a dispatch"); // dispatches are created up front
                var enter = NewBlock("il.filter.accept");
                _b.CondBr(accept, enter, clause.Reject);
                _b.PositionAtEnd(enter);
                var handler = il.BlockAt[clause.HandlerStart];
                _b.Store(_b.Load(L.Ptr, clause.Slot), handler.Entry![0].Slot);
                _b.Br(handler.Llvm);
                return true;
            }
            case ILOpCode.Throw:
                CallRuntime("csrt_throw_object", L.Void, [L.Ptr], IlRef(Pop()));
                _b.Unreachable();
                return true;
            case ILOpCode.Rethrow:
            {
                var clause = IlHandlerOf(block);
                CallRuntime("csrt_rethrow", L.Void, [L.Ptr], _b.Load(L.Ptr, clause.Slot));
                _b.Unreachable();
                return true;
            }

            // ---- conversions ----
            case ILOpCode.Conv_i1: Push(IlConvert(Pop(), SpecialType.System_SByte, false, false)); return false;
            case ILOpCode.Conv_i2: Push(IlConvert(Pop(), SpecialType.System_Int16, false, false)); return false;
            case ILOpCode.Conv_i4: Push(IlConvert(Pop(), SpecialType.System_Int32, false, false)); return false;
            case ILOpCode.Conv_i8: Push(IlConvert(Pop(), SpecialType.System_Int64, false, false)); return false;
            case ILOpCode.Conv_u1: Push(IlConvert(Pop(), SpecialType.System_Byte, false, false)); return false;
            case ILOpCode.Conv_u2: Push(IlConvert(Pop(), SpecialType.System_UInt16, false, false)); return false;
            case ILOpCode.Conv_u4: Push(IlConvert(Pop(), SpecialType.System_UInt32, false, false)); return false;
            case ILOpCode.Conv_u8: Push(IlConvert(Pop(), SpecialType.System_UInt64, false, true)); return false;
            case ILOpCode.Conv_i: Push(IlConvert(Pop(), SpecialType.System_IntPtr, false, false)); return false;
            case ILOpCode.Conv_u: Push(IlConvert(Pop(), SpecialType.System_UIntPtr, false, true)); return false;
            case ILOpCode.Conv_r4: Push(IlConvert(Pop(), SpecialType.System_Single, false, false)); return false;
            case ILOpCode.Conv_r8: Push(IlConvert(Pop(), SpecialType.System_Double, false, false)); return false;
            case ILOpCode.Conv_r_un: Push(IlConvert(Pop(), SpecialType.System_Double, false, true)); return false;
            case ILOpCode.Conv_ovf_i1: Push(IlConvert(Pop(), SpecialType.System_SByte, true, false)); return false;
            case ILOpCode.Conv_ovf_i2: Push(IlConvert(Pop(), SpecialType.System_Int16, true, false)); return false;
            case ILOpCode.Conv_ovf_i4: Push(IlConvert(Pop(), SpecialType.System_Int32, true, false)); return false;
            case ILOpCode.Conv_ovf_i8: Push(IlConvert(Pop(), SpecialType.System_Int64, true, false)); return false;
            case ILOpCode.Conv_ovf_u1: Push(IlConvert(Pop(), SpecialType.System_Byte, true, false)); return false;
            case ILOpCode.Conv_ovf_u2: Push(IlConvert(Pop(), SpecialType.System_UInt16, true, false)); return false;
            case ILOpCode.Conv_ovf_u4: Push(IlConvert(Pop(), SpecialType.System_UInt32, true, false)); return false;
            case ILOpCode.Conv_ovf_u8: Push(IlConvert(Pop(), SpecialType.System_UInt64, true, false)); return false;
            case ILOpCode.Conv_ovf_i: Push(IlConvert(Pop(), SpecialType.System_IntPtr, true, false)); return false;
            case ILOpCode.Conv_ovf_u: Push(IlConvert(Pop(), SpecialType.System_UIntPtr, true, false)); return false;
            case ILOpCode.Conv_ovf_i1_un: Push(IlConvert(Pop(), SpecialType.System_SByte, true, true)); return false;
            case ILOpCode.Conv_ovf_i2_un: Push(IlConvert(Pop(), SpecialType.System_Int16, true, true)); return false;
            case ILOpCode.Conv_ovf_i4_un: Push(IlConvert(Pop(), SpecialType.System_Int32, true, true)); return false;
            case ILOpCode.Conv_ovf_i8_un: Push(IlConvert(Pop(), SpecialType.System_Int64, true, true)); return false;
            case ILOpCode.Conv_ovf_u1_un: Push(IlConvert(Pop(), SpecialType.System_Byte, true, true)); return false;
            case ILOpCode.Conv_ovf_u2_un: Push(IlConvert(Pop(), SpecialType.System_UInt16, true, true)); return false;
            case ILOpCode.Conv_ovf_u4_un: Push(IlConvert(Pop(), SpecialType.System_UInt32, true, true)); return false;
            case ILOpCode.Conv_ovf_u8_un: Push(IlConvert(Pop(), SpecialType.System_UInt64, true, true)); return false;
            case ILOpCode.Conv_ovf_i_un: Push(IlConvert(Pop(), SpecialType.System_IntPtr, true, true)); return false;
            case ILOpCode.Conv_ovf_u_un: Push(IlConvert(Pop(), SpecialType.System_UIntPtr, true, true)); return false;

            // ---- objects, fields, calls ----
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
                IlCall(instruction, stack, constrained);
                return false;
            case ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
                when IlAssembly.ResolveMember(instruction.Token, IlBody.Context) is IEventSymbol backing:
                IlEventBackingField(op, backing, stack);
                return false;
            case ILOpCode.Ldfld or ILOpCode.Ldflda:
            {
                var field = IlField(instruction.Token);
                var instance = Pop();
                if (op == ILOpCode.Ldflda) { Push(new IlEntry(IlKind.ByRef, IlInstanceFieldAddress(field, instance), field.Type)); return false; }
                if (IlLibraryInstanceField(field)) { Push(IlPush(EmitValue(new IlFieldReferenceOperation(il.Syntax, field, IlOperand(instance, field.ContainingType, lvalue: field.ContainingType.IsValueType))), field.Type)); return false; }
                var address = IlInstanceFieldAddress(field, instance);
                Push(IlPush(isVolatile || field.IsVolatile ? VolatileLoad(address, S(field.Type)) : LoadValue(address, S(field.Type)), field.Type));
                return false;
            }
            case ILOpCode.Stfld:
            {
                var field = IlField(instruction.Token);
                var value = Pop();
                var instance = Pop();
                if (IlLibraryInstanceField(field)) throw new IlException($"assignment to field '{field}' of a library type");
                var address = IlInstanceFieldAddress(field, instance);
                var typed = IlTyped(value, field.Type);
                if (isVolatile || field.IsVolatile) VolatileStore(typed, address, S(field.Type));
                else StoreValue(ToStorage(typed, S(field.Type)), address);
                return false;
            }
            case ILOpCode.Ldsfld:
            {
                var field = IlField(instruction.Token);
                if (!_m.IsUserType(_m.CoreLib.Map(S(field)).ContainingType))
                {
                    Push(IlPush(EmitValue(new IlFieldReferenceOperation(il.Syntax, field, null)), field.Type));
                    return false;
                }
                var address = StaticFieldAddress(S(field));
                Push(IlPush(isVolatile || field.IsVolatile ? VolatileLoad(address, S(field.Type)) : LoadValue(address, S(field.Type)), field.Type));
                return false;
            }
            case ILOpCode.Ldsflda:
            {
                var field = IlField(instruction.Token);
                if (IlRvaData(field) is { } data) { Push(new IlEntry(IlKind.ByRef, data.Global, field.Type) { NonNull = true }); return false; }
                if (!_m.IsUserType(_m.CoreLib.Map(S(field)).ContainingType))
                {
                    // A library's readonly static (DateTime.MaxValue) can't change after its initializer: its
                    // address is only taken to call members on it, so a copy behaves the same.
                    if (!field.IsReadOnly) throw new IlException($"address of static field '{field}' of a library type");
                    var copy = Spill(EmitValue(new IlFieldReferenceOperation(il.Syntax, field, null)));
                    Push(new IlEntry(IlKind.ByRef, copy, field.Type) { NonNull = true });
                    return false;
                }
                Push(new IlEntry(IlKind.ByRef, StaticFieldAddress(S(field)), field.Type));
                return false;
            }
            case ILOpCode.Stsfld:
            {
                var field = IlField(instruction.Token);
                var value = Pop();
                if (!_m.IsUserType(_m.CoreLib.Map(S(field)).ContainingType)) throw new IlException($"assignment to static field '{field}' of a library type");
                var address = StaticFieldAddress(S(field));
                var typed = IlTyped(value, field.Type);
                if (isVolatile || field.IsVolatile) VolatileStore(typed, address, S(field.Type));
                else StoreValue(ToStorage(typed, S(field.Type)), address);
                return false;
            }
            case ILOpCode.Ldftn or ILOpCode.Ldvirtftn:
            {
                var method = IlMethod(instruction.Token);
                var target = op == ILOpCode.Ldvirtftn ? Pop() : null;
                Push(new IlEntry(IlKind.MethodPtr, default, null) { Symbol = method, Target = target });
                return false;
            }
            case ILOpCode.Ldtoken:
            {
                var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instruction.Token);
                ISymbol symbol = handle.Kind is HandleKind.FieldDefinition or HandleKind.MethodDefinition or HandleKind.MemberReference or HandleKind.MethodSpecification
                    ? IlAssembly.ResolveMember(instruction.Token, IlBody.Context)
                    : ResolveIlType(instruction.Token);
                Push(new IlEntry(IlKind.Token, default, null) { Symbol = symbol });
                return false;
            }

            // ---- type operations ----
            case ILOpCode.Box:
            {
                var type = ResolveIlType(instruction.Token);
                var value = Pop();
                var st = S(type);
                if (st.IsReferenceType) { Push(value); return false; }
                // `value != null` on a T that is a value type: C# compilers box only to test the result for
                // null, which a non-nullable value never is. Skip the allocation, as the source path folds it.
                if (st.IsValueType && !TypeMapper.IsNullable(st) && IlBoxOnlyNullTested(block))
                {
                    Push(new IlEntry(IlKind.Ref, _m.ExternalGlobal("csrt_Object_MT"), Special(SpecialType.System_Object)) { NonNull = true });
                    return false;
                }
                Push(new IlEntry(IlKind.Ref, Box(IlTyped(value, type), st, IlAt()), Special(SpecialType.System_Object)));
                return false;
            }
            case ILOpCode.Unbox_any:
            {
                var type = ResolveIlType(instruction.Token);
                var obj = IlRef(Pop());
                var st = S(type);
                if (st.IsReferenceType) { Push(new IlEntry(IlKind.Ref, IlCastClass(obj, type), type)); return false; }
                Push(IlPush(IlUnboxValue(obj, st), type));
                return false;
            }
            case ILOpCode.Unbox:
            {
                var type = ResolveIlType(instruction.Token);
                var obj = IlRef(Pop());
                var st = S(type);
                if (TypeMapper.IsNullable(st)) throw new IlException("unbox to Nullable<T> (by address)");
                Push(new IlEntry(IlKind.ByRef, CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], obj, MethodTableFor(st, IlAt())), type));
                return false;
            }
            case ILOpCode.Castclass:
            {
                var type = ResolveIlType(instruction.Token);
                var value = Pop();
                // `(D)Delegate.Combine(a, b)` in compiled event accessors: both operands are D, and so is the result.
                if (S(type).TypeKind == TypeKind.Delegate && value.Symbol is IMethodSymbol { Name: "Combine" or "Remove", ContainingType.Name: "Delegate" })
                {
                    Push(value with { Type = type, Symbol = null });
                    return false;
                }
                Push(new IlEntry(IlKind.Ref, IlCastClass(IlRef(value), type, value.Type), type));
                return false;
            }
            case ILOpCode.Isinst:
            {
                var type = ResolveIlType(instruction.Token);
                var value = Pop();
                var st = S(type);
                if (st is INamedTypeSymbol { TypeKind: TypeKind.Interface } itf && MayHoldArray(value.Type is null ? null : S(value.Type)))
                    _m.RequestArrayInterface(itf);
                Push(new IlEntry(IlKind.Ref, IsInst(IlRef(value), TypeMapper.IsNullable(st) ? TypeMapper.NullableUnderlying(st) : st, IlAt()), Special(SpecialType.System_Object)));
                return false;
            }
            case ILOpCode.Initobj:
            {
                var type = ResolveIlType(instruction.Token);
                var address = IlAddress(Pop());
                var storage = T.Storage(S(type));
                _b.Store(LlvmContext.Null(storage), address);
                return false;
            }
            case ILOpCode.Ldobj:
            {
                var type = ResolveIlType(instruction.Token);
                var address = IlAddress(Pop());
                Push(IlPush(isVolatile ? VolatileLoad(address, S(type)) : LoadValue(address, S(type)), type));
                return false;
            }
            case ILOpCode.Stobj:
            {
                var type = ResolveIlType(instruction.Token);
                var value = Pop();
                var address = IlAddress(Pop());
                var typed = IlTyped(value, type);
                if (isVolatile) VolatileStore(typed, address, S(type));
                else StoreValue(ToStorage(typed, S(type)), address);
                return false;
            }
            case ILOpCode.Cpobj:
            {
                var type = ResolveIlType(instruction.Token);
                var source = IlAddress(Pop());
                var destination = IlAddress(Pop());
                _b.Store(_b.Load(T.Storage(S(type)), source), destination);
                return false;
            }
            case ILOpCode.Sizeof:
                Push(I4((int)T.Target.SizeOf(T.Storage(S(ResolveIlType(instruction.Token))))));
                return false;

            // ---- indirect access ----
            case ILOpCode.Ldind_i1: Push(IlLoadIndirect(Pop(), SpecialType.System_SByte, isVolatile)); return false;
            case ILOpCode.Ldind_u1: Push(IlLoadIndirect(Pop(), SpecialType.System_Byte, isVolatile)); return false;
            case ILOpCode.Ldind_i2: Push(IlLoadIndirect(Pop(), SpecialType.System_Int16, isVolatile)); return false;
            case ILOpCode.Ldind_u2: Push(IlLoadIndirect(Pop(), SpecialType.System_UInt16, isVolatile)); return false;
            case ILOpCode.Ldind_i4: Push(IlLoadIndirect(Pop(), SpecialType.System_Int32, isVolatile)); return false;
            case ILOpCode.Ldind_u4: Push(IlLoadIndirect(Pop(), SpecialType.System_UInt32, isVolatile)); return false;
            case ILOpCode.Ldind_i8: Push(IlLoadIndirect(Pop(), SpecialType.System_Int64, isVolatile)); return false;
            case ILOpCode.Ldind_i: Push(IlLoadIndirect(Pop(), SpecialType.System_IntPtr, isVolatile)); return false;
            case ILOpCode.Ldind_r4: Push(IlLoadIndirect(Pop(), SpecialType.System_Single, isVolatile)); return false;
            case ILOpCode.Ldind_r8: Push(IlLoadIndirect(Pop(), SpecialType.System_Double, isVolatile)); return false;
            case ILOpCode.Ldind_ref:
            {
                var address = Pop();
                var type = address.Type ?? Special(SpecialType.System_Object);
                var loaded = isVolatile ? VolatileLoad(IlAddress(address), Special(SpecialType.System_Object)) : _b.Load(L.Ptr, IlAddress(address));
                Push(new IlEntry(IlKind.Ref, loaded, type));
                return false;
            }
            case ILOpCode.Stind_i1: IlStoreIndirect(stack, SpecialType.System_SByte, isVolatile); return false;
            case ILOpCode.Stind_i2: IlStoreIndirect(stack, SpecialType.System_Int16, isVolatile); return false;
            case ILOpCode.Stind_i4: IlStoreIndirect(stack, SpecialType.System_Int32, isVolatile); return false;
            case ILOpCode.Stind_i8: IlStoreIndirect(stack, SpecialType.System_Int64, isVolatile); return false;
            case ILOpCode.Stind_i: IlStoreIndirect(stack, SpecialType.System_IntPtr, isVolatile); return false;
            case ILOpCode.Stind_r4: IlStoreIndirect(stack, SpecialType.System_Single, isVolatile); return false;
            case ILOpCode.Stind_r8: IlStoreIndirect(stack, SpecialType.System_Double, isVolatile); return false;
            case ILOpCode.Stind_ref:
            {
                var value = IlRef(Pop());
                var address = IlAddress(Pop());
                if (isVolatile) VolatileStore(value, address, Special(SpecialType.System_Object));
                else StoreValue(value, address);
                return false;
            }

            // ---- arrays ----
            case ILOpCode.Newarr:
            {
                var elementType = ResolveIlType(instruction.Token);
                var length = IlInt(Pop());
                var length64 = IlToI64(length, signed: true); // native int is 32-bit on wasm32
                Push(new IlEntry(IlKind.Ref, NewArray(S(elementType), length64), _m.Compilation.CreateArrayTypeSymbol(elementType))
                {
                    NonNull = true,
                    FreshLength = length.Constant as int?,
                });
                return false;
            }
            case ILOpCode.Ldlen:
            {
                // A native int: the 64-bit length field narrowed on wasm32 (lengths are below 2^31).
                var length = ArrayLength(IlRef(Pop()));
                if (T.NativeInt.IntWidth < 64) length = _b.Cast(LLVMOpcode.Trunc, length, T.NativeInt);
                Push(new IlEntry(IlKind.NativeInt, length, Special(SpecialType.System_UIntPtr)));
                return false;
            }
            case ILOpCode.Ldelema:
            {
                var index = Pop();
                var array = Pop();
                var elementType = ResolveIlType(instruction.Token);
                Push(new IlEntry(IlKind.ByRef, IlElementAddress(array, index, elementType), elementType));
                return false;
            }
            case ILOpCode.Ldelem or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_i4
                or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_i or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref:
            {
                var index = Pop();
                var array = Pop();
                var elementType = IlElementType(op, array, instruction);
                Push(IlPush(LoadValue(IlElementAddress(array, index, elementType), S(elementType)), elementType));
                return false;
            }
            case ILOpCode.Stelem or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8
                or ILOpCode.Stelem_i or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref:
            {
                var value = Pop();
                var index = Pop();
                var array = Pop();
                var elementType = IlElementType(op, array, instruction);
                var address = IlElementAddress(array, index, elementType);
                StoreValue(ToStorage(IlTyped(value, elementType), S(elementType)), address);
                return false;
            }

            // ---- memory ----
            case ILOpCode.Localloc:
            {
                var size = IlInt(Pop());
                // i64 whatever the native int width (wasm32's is 32 bits): llvm.memset takes an i64 length.
                var bytes = IlToI64(size, signed: false);
                var memory = Native.LLVMBuildArrayAlloca(_b.Ref, L.I8, bytes, "localloc");
                Native.LLVMSetAlignment(memory, 16);
                if (IlBody.InitLocals) IlMemset(memory, bytes);
                Push(new IlEntry(IlKind.Pointer, memory, null));
                return false;
            }
            case ILOpCode.Initblk:
            {
                var size = IlInt(Pop());
                var value = IlInt(Pop());
                var address = IlAddress(Pop());
                var f = _m.Intrinsic("llvm.memset.p0.i64", L.Void, L.Ptr, L.I8, L.I64, L.I1);
                _b.Call(f.Type, f.Fn, [address, _b.Cast(LLVMOpcode.Trunc, value.Value, L.I8), IlToI64(size, signed: false), L.False]);
                return false;
            }
            case ILOpCode.Cpblk:
            {
                var size = IlInt(Pop());
                var source = IlAddress(Pop());
                var destination = IlAddress(Pop());
                var f = _m.Intrinsic("llvm.memmove.p0.p0.i64", L.Void, L.Ptr, L.Ptr, L.I64, L.I1);
                _b.Call(f.Type, f.Fn, [destination, source, IlToI64(size, signed: false), L.False]);
                return false;
            }

            case ILOpCode.Calli:
                IlCallIndirect(instruction, stack);
                return false;

            default:
                throw new IlException($"opcode '{IlInstruction.Name(op)}'");
        }
    }

    /// <summary>
    /// The box being lowered (instruction <c>_ilIndex</c>) is only compared with null: the next instructions,
    /// in the same block, are `brtrue`/`brfalse`, or `ldnull` and `ceq`/`cgt.un`.
    /// </summary>
    private bool IlBoxOnlyNullTested(IlBlock block)
    {
        var instructions = IlBody.Instructions;
        if (_ilIndex + 1 > block.Last) return false;
        var next = instructions[_ilIndex + 1].OpCode;
        if (next is ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s) return true;
        return next == ILOpCode.Ldnull && _ilIndex + 2 <= block.Last
            && instructions[_ilIndex + 2].OpCode is ILOpCode.Ceq or ILOpCode.Cgt_un;
    }

    /// <summary>
    /// `calli`: a call through a function pointer (C# `delegate*`), with the call site's signature. Like
    /// the source path's function pointer invocation, every calling convention is csharpc's own.
    /// </summary>
    private void IlCallIndirect(IlInstruction instruction, Stack<IlEntry> stack)
    {
        IlEntry Pop() => stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");
        var signature = IlAssembly.DecodeCallSiteSignature(instruction.Token, IlBody.Context);
        if (signature.Header.IsInstance) throw new IlException("calli with an instance signature");
        if (signature.Header.CallingConvention == SignatureCallingConvention.VarArgs) throw new IlException("vararg calli");
        var pointer = Pop();
        var fn = pointer.Kind == IlKind.MethodPtr ? IlTyped(pointer, Special(SpecialType.System_IntPtr)) : pointer.Value;
        if (Native.LLVMTypeOf(fn).Kind != LLVMTypeKind.Pointer) fn = _b.Cast(LLVMOpcode.IntToPtr, fn, L.Ptr);
        var parameters = signature.ParameterTypes;
        var args = new LLVMValueRef[parameters.Length];
        for (var i = parameters.Length - 1; i >= 0; i--)
        {
            var e = Pop();
            args[i] = parameters[i].IsByRef ? IlAddress(e) : IlTyped(e, parameters[i].Type);
        }
        var ret = signature.ReturnType;
        var returnsVoid = !ret.IsByRef && ret.Type.SpecialType == SpecialType.System_Void;
        var type = L.FunctionType(ret.IsByRef ? L.Ptr : returnsVoid ? L.Void : T.Value(S(ret.Type)),
            [.. parameters.Select(p => p.IsByRef ? L.Ptr : T.Value(S(p.Type)))]);
        var result = _b.Call(type, fn, args);
        if (returnsVoid) return;
        stack.Push(ret.IsByRef ? new IlEntry(IlKind.ByRef, result, ret.Type) : IlPush(result, ret.Type));
    }

    private static ILOpCode LongForm(ILOpCode op) => op switch
    {
        ILOpCode.Beq_s => ILOpCode.Beq, ILOpCode.Bne_un_s => ILOpCode.Bne_un, ILOpCode.Bge_s => ILOpCode.Bge,
        ILOpCode.Bge_un_s => ILOpCode.Bge_un, ILOpCode.Bgt_s => ILOpCode.Bgt, ILOpCode.Bgt_un_s => ILOpCode.Bgt_un,
        ILOpCode.Ble_s => ILOpCode.Ble, ILOpCode.Ble_un_s => ILOpCode.Ble_un, ILOpCode.Blt_s => ILOpCode.Blt,
        ILOpCode.Blt_un_s => ILOpCode.Blt_un, _ => op,
    };

    /// <param name="swap">The condition is the branch's negation: it selects the fall-through.</param>
    private void IlCondBranch(LLVMValueRef condition, IlInstruction instruction, Stack<IlEntry> stack, bool swap = false)
    {
        var taken = IlTarget(instruction.Target);
        var fallthrough = IlTarget(instruction.Next);
        IlTransfer(taken, stack);
        IlTransfer(fallthrough, stack);
        if (swap) _b.CondBr(condition, fallthrough.Llvm, taken.Llvm);
        else _b.CondBr(condition, taken.Llvm, fallthrough.Llvm);
    }

    // ======================================================================================
    // Stack entries <-> typed values
    // ======================================================================================

    private IlEntry I4(int value) => new(IlKind.Int32, L.ConstInt(L.I32, value), Special(SpecialType.System_Int32));

    private static IlEntry I4Value(LLVMValueRef value, ITypeSymbol type) => new(IlKind.Int32, value, type);

    /// <summary>A typed value (T.Value of S(type)) as it sits on the evaluation stack.</summary>
    private IlEntry IlPush(LLVMValueRef value, ITypeSymbol type)
    {
        var st = S(type);
        switch (st)
        {
            case { SpecialType: SpecialType.System_Boolean }:
                return new IlEntry(IlKind.Int32, _b.Cast(LLVMOpcode.ZExt, value, L.I32), type);
            case IPointerTypeSymbol or IFunctionPointerTypeSymbol:
                return new IlEntry(IlKind.Pointer, value, st is IPointerTypeSymbol p ? p.PointedAtType : null);
            case { IsReferenceType: true }:
                return new IlEntry(IlKind.Ref, value, type);
        }
        var llvmType = Native.LLVMTypeOf(value);
        var integral = st.SpecialType is >= SpecialType.System_Char and <= SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr
            || st.TypeKind == TypeKind.Enum;
        if (integral && llvmType.IsInteger)
        {
            if (llvmType.IntWidth < 32)
                return new IlEntry(IlKind.Int32, _b.Cast(TypeMapper.IsSigned(st) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, L.I32), type);
            var kind = st.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr ? IlKind.NativeInt
                : llvmType.IntWidth == 32 ? IlKind.Int32 : IlKind.Int64;
            return new IlEntry(kind, value, type);
        }
        if (st.SpecialType is SpecialType.System_Single or SpecialType.System_Double) return new IlEntry(IlKind.Float, value, type);
        return new IlEntry(IlKind.Value, value, type);
    }

    /// <summary>The value of a stack entry converted to `type` (implicit conversions of ECMA-335 III.1.6).</summary>
    private LLVMValueRef IlTyped(IlEntry e, ITypeSymbol type)
    {
        var st = S(type);
        var target = T.Value(st);
        var v = e.Value;
        // `&M` in C# (`ldftn` not consumed by a delegate constructor): the code pointer, as in the source path.
        if (e is { Kind: IlKind.MethodPtr, Target: null, Symbol: IMethodSymbol pointed } && (st is IFunctionPointerTypeSymbol or IPointerTypeSymbol
                || st.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr))
        {
            var code = FunctionPointerTo(S(pointed), IlAt());
            return target.IsPointer ? code : _b.Cast(LLVMOpcode.PtrToInt, code, target);
        }
        var source = e.Kind is IlKind.MethodPtr or IlKind.Token ? throw new IlException($"{e.Kind} value used as '{st}'") : Native.LLVMTypeOf(v);
        switch (e.Kind)
        {
            case IlKind.Ref or IlKind.ByRef or IlKind.Pointer:
                if (e.Kind == IlKind.Ref && e.Type is not null && st.IsReferenceType && S(e.Type) is { IsReferenceType: true } from)
                    Coerce(v, from, st); // T[] / string flowing into an interface: make the interface tables exist
                if (target.IsPointer) return v;
                if (target.IsInteger) return _b.Cast(LLVMOpcode.PtrToInt, v, target);
                break;
            case IlKind.Int32 or IlKind.Int64 or IlKind.NativeInt:
                if (st.SpecialType == SpecialType.System_Boolean) return _b.ICmp(LLVMIntPredicate.NE, v, L.ConstInt(source, 0));
                if (target.IsInteger)
                {
                    if (target.IntWidth == source.IntWidth) return v;
                    if (target.IntWidth < source.IntWidth) return _b.Cast(LLVMOpcode.Trunc, v, target);
                    // int32 -> native int sign-extends, -> native unsigned int zero-extends (ECMA-335 III.1.6).
                    return _b.Cast(TypeMapper.IsSigned(st) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, v, target);
                }
                if (target.IsPointer)
                    return _b.Cast(LLVMOpcode.IntToPtr, source.IntWidth == 64 ? v : _b.Cast(LLVMOpcode.ZExt, v, L.I64), target);
                break;
            case IlKind.Float:
                if (target.IsFloatingPoint)
                    return source.Handle == target.Handle ? v : _b.Cast(source.Kind == LLVMTypeKind.Double ? LLVMOpcode.FPTrunc : LLVMOpcode.FPExt, v, target);
                break;
            case IlKind.Value:
                if (source.Handle == target.Handle) return v;
                break;
        }
        throw new IlException($"a {e.Kind} value ({e.Type?.ToDisplayString() ?? "?"}) used as '{st}'");
    }

    private LLVMValueRef IlRef(IlEntry e) => e.Kind switch
    {
        IlKind.Ref or IlKind.ByRef or IlKind.Pointer => e.Value,
        IlKind.NativeInt => _b.Cast(LLVMOpcode.IntToPtr, e.Value, L.Ptr),
        _ => throw new IlException($"object reference expected, found {e.Kind}"),
    };

    /// <summary>The address held by a managed/unmanaged pointer (or a native int used as one).</summary>
    private LLVMValueRef IlAddress(IlEntry e) => e.Kind switch
    {
        IlKind.ByRef or IlKind.Pointer => e.Value,
        IlKind.NativeInt => _b.Cast(LLVMOpcode.IntToPtr, e.Value, L.Ptr),
        IlKind.Int32 => _b.Cast(LLVMOpcode.IntToPtr, _b.Cast(LLVMOpcode.ZExt, e.Value, L.I64), L.Ptr),
        _ => throw new IlException($"address expected, found {e.Kind}"),
    };

    /// <summary>Integer operand (pointers used in integer arithmetic become native ints).</summary>
    private IlEntry IlInt(IlEntry e) => e.Kind switch
    {
        IlKind.Int32 or IlKind.Int64 or IlKind.NativeInt => e,
        IlKind.ByRef or IlKind.Pointer or IlKind.Ref => new IlEntry(IlKind.NativeInt, _b.Cast(LLVMOpcode.PtrToInt, e.Value, T.NativeInt), Special(SpecialType.System_UIntPtr)),
        _ => throw new IlException($"integer expected, found {e.Kind}"),
    };

    private LLVMValueRef IlToI64(IlEntry e, bool signed)
    {
        var v = IlInt(e).Value;
        var type = Native.LLVMTypeOf(v);
        return type.IntWidth == 64 ? v : _b.Cast(signed ? LLVMOpcode.SExt : LLVMOpcode.ZExt, v, L.I64);
    }

    private LLVMValueRef IlIsNonZero(IlEntry e) => e.Kind switch
    {
        IlKind.Ref or IlKind.ByRef or IlKind.Pointer => _b.ICmp(LLVMIntPredicate.NE, e.Value, LlvmContext.Null(L.Ptr)),
        IlKind.Int32 or IlKind.Int64 or IlKind.NativeInt => _b.ICmp(LLVMIntPredicate.NE, e.Value, L.ConstInt(Native.LLVMTypeOf(e.Value), 0)),
        _ => throw new IlException($"branch on a {e.Kind} value"),
    };

    /// <summary>
    /// A stack entry as an operand of a synthetic operation, typed as <paramref name="type"/>. An
    /// <paramref name="lvalue"/> operand (a receiver, a by-ref argument) is the location a managed pointer
    /// designates; any other operand is the entry's value (a pointer passed as `void*` stays a pointer).
    /// </summary>
    private IlValueOperation IlOperand(IlEntry e, ITypeSymbol type, bool lvalue = false)
    {
        var il = _il!;
        if (lvalue && e.Kind is IlKind.ByRef or IlKind.Pointer or IlKind.NativeInt)
        {
            var address = IlAddress(e);
            return new IlValueOperation(il.Syntax, type, () => LoadValue(address, S(type)), address, nonNull: true);
        }
        if (e.Kind == IlKind.Value && lvalue)
        {
            // A value-type receiver held by value: give the callee a temporary to work on.
            var spilled = Spill(e.Value);
            return new IlValueOperation(il.Syntax, type, () => LoadValue(spilled, S(type)), spilled, nonNull: true);
        }
        // Library members taking System.Array (Array.Copy, Array.Clear, ...) are lowered per element type:
        // hand them the array type the IL value is known to have.
        if (type.SpecialType == SpecialType.System_Array && e.Kind == IlKind.Ref && e.Type is IArrayTypeSymbol) type = e.Type;
        return new IlValueOperation(il.Syntax, type, () => IlTyped(e, type), nonNull: e.NonNull || e.IsThis)
        {
            Constant = e.Constant is null ? default : TypedConstant(e.Constant, type),
        };
    }

    /// <summary>An IL constant as the C# constant of an operand of <paramref name="type"/> (enums: their underlying value), if exact.</summary>
    private static Optional<object?> TypedConstant(object constant, ITypeSymbol type)
    {
        var special = type is INamedTypeSymbol { EnumUnderlyingType: { } underlying } ? underlying.SpecialType : type.SpecialType;
        return (constant, special) switch
        {
            (string s, SpecialType.System_String) => s,
            (int i, SpecialType.System_Int32) => i,
            (int i, SpecialType.System_Boolean) when i is 0 or 1 => i == 1,
            (int i, SpecialType.System_Char) when i is >= char.MinValue and <= char.MaxValue => (char)i,
            (int i, SpecialType.System_SByte) when i is >= sbyte.MinValue and <= sbyte.MaxValue => (sbyte)i,
            (int i, SpecialType.System_Byte) when i is >= byte.MinValue and <= byte.MaxValue => (byte)i,
            (int i, SpecialType.System_Int16) when i is >= short.MinValue and <= short.MaxValue => (short)i,
            (int i, SpecialType.System_UInt16) when i is >= ushort.MinValue and <= ushort.MaxValue => (ushort)i,
            (int i, SpecialType.System_UInt32) => unchecked((uint)i),
            (long l, SpecialType.System_Int64) => l,
            (long l, SpecialType.System_UInt64) => unchecked((ulong)l),
            (double d, SpecialType.System_Double) => d,
            (float f, SpecialType.System_Single) => f,
            _ => default(Optional<object?>),
        };
    }

    // ======================================================================================
    // Arguments and locals
    // ======================================================================================

    private bool IlHasThis => _method is not null && ModuleCodeGen.HasThis(_method);

    private IlType IlLocal(int index) =>
        index < IlBody.Locals.Length ? IlBody.Locals[index] : throw new IlException($"local {index} does not exist");

    private IlEntry IlLoadLocal(int index)
    {
        var local = IlLocal(index);
        var slot = _il!.Locals[index];
        if (local.IsByRef) return new IlEntry(IlKind.ByRef, _b.Load(L.Ptr, slot), local.Type);
        return IlPush(LoadValue(slot, S(local.Type)), local.Type);
    }

    private void IlStoreLocal(int index, IlEntry value)
    {
        var local = IlLocal(index);
        var slot = _il!.Locals[index];
        if (local.IsByRef) { _b.Store(IlAddress(value), slot); return; }
        StoreValue(ToStorage(IlTyped(value, local.Type), S(local.Type)), slot);
    }

    private IParameterSymbol IlParameter(int index)
    {
        var parameters = _method!.OriginalDefinition.Parameters;
        var i = IlHasThis ? index - 1 : index;
        return i >= 0 && i < parameters.Length ? parameters[i] : throw new IlException($"argument {index} does not exist");
    }

    private IlEntry IlLoadArg(int index)
    {
        if (IlHasThis && index == 0)
        {
            var owner = _method!.OriginalDefinition.ContainingType;
            return owner.IsValueType
                ? new IlEntry(IlKind.ByRef, _this, owner) { IsThis = true, NonNull = true }
                : new IlEntry(IlKind.Ref, _this, owner) { IsThis = true, NonNull = true };
        }
        var parameter = IlParameter(index);
        var slot = _slots[parameter];
        if (parameter.RefKind != RefKind.None) return new IlEntry(IlKind.ByRef, slot, parameter.Type);
        return IlPush(LoadValue(slot, S(parameter.Type)), parameter.Type);
    }

    private IlEntry IlArgAddress(int index)
    {
        if (IlHasThis && index == 0)
        {
            // A struct's `this` is already a reference to the value. A class's `this` can't be reassigned
            // (C# compiles `ldarga.s 0` to pass the reference by `in`), so a copy of it is faithful.
            var owner = _method!.OriginalDefinition.ContainingType;
            if (owner.IsValueType) return new IlEntry(IlKind.ByRef, _this, owner) { IsThis = true, NonNull = true };
            var copy = NewTemp(L.Ptr, "this.copy");
            _b.Store(_this, copy);
            return new IlEntry(IlKind.ByRef, copy, owner) { NonNull = true };
        }
        var parameter = IlParameter(index);
        if (parameter.RefKind != RefKind.None) throw new IlException("address of a by-ref argument");
        return new IlEntry(IlKind.ByRef, _slots[parameter], parameter.Type);
    }

    private void IlStoreArg(int index, IlEntry value)
    {
        if (IlHasThis && index == 0) throw new IlException("assignment to 'this'");
        var parameter = IlParameter(index);
        if (parameter.RefKind != RefKind.None) throw new IlException("starg on a by-ref argument");
        StoreValue(ToStorage(IlTyped(value, parameter.Type), S(parameter.Type)), _slots[parameter]);
    }

    // ======================================================================================
    // Arithmetic, comparisons, conversions
    // ======================================================================================

    /// <summary>Brings two integer operands to a common width (int32 + native int -> native int, sign-extended).</summary>
    private (LLVMValueRef L, LLVMValueRef R, IlKind Kind) IlWiden(IlEntry l, IlEntry r)
    {
        l = IlInt(l);
        r = IlInt(r);
        var lw = Native.LLVMTypeOf(l.Value).IntWidth;
        var rw = Native.LLVMTypeOf(r.Value).IntWidth;
        var kind = l.Kind == r.Kind ? l.Kind : l.Kind == IlKind.Int32 ? r.Kind : r.Kind == IlKind.Int32 ? l.Kind : IlKind.NativeInt;
        if (lw == rw) return (l.Value, r.Value, kind);
        return lw < rw
            ? (_b.Cast(LLVMOpcode.SExt, l.Value, Native.LLVMTypeOf(r.Value)), r.Value, kind)
            : (l.Value, _b.Cast(LLVMOpcode.SExt, r.Value, Native.LLVMTypeOf(l.Value)), kind);
    }

    private (LLVMValueRef L, LLVMValueRef R) IlFloats(IlEntry l, IlEntry r)
    {
        if (l.Kind != IlKind.Float || r.Kind != IlKind.Float) throw new IlException($"arithmetic on {l.Kind} and {r.Kind}");
        var lt = Native.LLVMTypeOf(l.Value);
        var rt = Native.LLVMTypeOf(r.Value);
        if (lt.Handle == rt.Handle) return (l.Value, r.Value);
        return (lt.Kind == LLVMTypeKind.Double ? l.Value : _b.Cast(LLVMOpcode.FPExt, l.Value, L.F64),
                rt.Kind == LLVMTypeKind.Double ? r.Value : _b.Cast(LLVMOpcode.FPExt, r.Value, L.F64));
    }

    private IlEntry IlBinary(ILOpCode op, IlEntry l, IlEntry r)
    {
        if (l.Kind == IlKind.Float || r.Kind == IlKind.Float)
        {
            var (a, b) = IlFloats(l, r);
            var result = op switch
            {
                ILOpCode.Add => _b.BinOp(LLVMOpcode.FAdd, a, b),
                ILOpCode.Sub => _b.BinOp(LLVMOpcode.FSub, a, b),
                ILOpCode.Mul => _b.BinOp(LLVMOpcode.FMul, a, b),
                ILOpCode.Div => _b.BinOp(LLVMOpcode.FDiv, a, b),
                ILOpCode.Rem => _b.BinOp(LLVMOpcode.FRem, a, b),
                _ => throw new IlException($"{IlInstruction.Name(op)} on floating-point values"),
            };
            return new IlEntry(IlKind.Float, result, Native.LLVMTypeOf(result).Kind == LLVMTypeKind.Double ? Special(SpecialType.System_Double) : Special(SpecialType.System_Single));
        }
        // Pointer arithmetic: byref/pointer +/- integer stays a pointer (GEP keeps provenance for LLVM).
        var lPtr = l.Kind is IlKind.ByRef or IlKind.Pointer;
        var rPtr = r.Kind is IlKind.ByRef or IlKind.Pointer;
        if ((op is ILOpCode.Add or ILOpCode.Add_ovf_un && (lPtr ^ rPtr)) || (op is ILOpCode.Sub or ILOpCode.Sub_ovf_un && lPtr && !rPtr))
        {
            var (pointer, offset) = lPtr ? (l, r) : (r, l);
            // `p + i * sizeof(T)` on a T*: index T elements, as the source path does (LLVM's loop and
            // addressing-mode optimizations see an element index instead of a byte offset).
            if (op == ILOpCode.Add && offset.Scaled is { } scaled && pointer.Type is { SpecialType: not SpecialType.System_Void } pointee
                && Native.LLVMTypeOf(offset.Value).IntWidth == 64 && T.Storage(S(pointee)) is var storage
                && (long)T.Target.SizeOf(storage) == scaled.Factor)
                return new IlEntry(pointer.Kind, _b.InBoundsGep(storage, pointer.Value, [IlToI64(scaled.Index, signed: true)]), pointer.Type);
            var delta = IlToI64(offset, signed: true);
            if (op is ILOpCode.Sub or ILOpCode.Sub_ovf_un) delta = _b.Neg(delta);
            var moved = _b.InBoundsGep(L.I8, pointer.Value, [delta]);
            return new IlEntry(pointer.Kind, moved, pointer.Type);
        }
        var (x, y, kind) = IlWiden(l, r);
        var value = op switch
        {
            ILOpCode.Add => _b.BinOp(LLVMOpcode.Add, x, y),
            ILOpCode.Sub => _b.BinOp(LLVMOpcode.Sub, x, y),
            ILOpCode.Mul => _b.BinOp(LLVMOpcode.Mul, x, y),
            ILOpCode.Div => CheckedDivide(true, x, y, signed: true),
            ILOpCode.Div_un => CheckedDivide(true, x, y, signed: false),
            ILOpCode.Rem => CheckedDivide(false, x, y, signed: true),
            ILOpCode.Rem_un => CheckedDivide(false, x, y, signed: false),
            ILOpCode.And => _b.BinOp(LLVMOpcode.And, x, y),
            ILOpCode.Or => _b.BinOp(LLVMOpcode.Or, x, y),
            ILOpCode.Xor => _b.BinOp(LLVMOpcode.Xor, x, y),
            ILOpCode.Add_ovf => CheckedArithmetic("add", x, y, signed: true),
            ILOpCode.Add_ovf_un => CheckedArithmetic("add", x, y, signed: false),
            ILOpCode.Sub_ovf => CheckedArithmetic("sub", x, y, signed: true),
            ILOpCode.Sub_ovf_un => CheckedArithmetic("sub", x, y, signed: false),
            ILOpCode.Mul_ovf => CheckedArithmetic("mul", x, y, signed: true),
            ILOpCode.Mul_ovf_un => CheckedArithmetic("mul", x, y, signed: false),
            _ => throw new IlException($"binary {IlInstruction.Name(op)}"),
        };
        // Bitwise operations on two values of the same small/enum/bool type keep that type (for later stores).
        var type = l.Type is { } lt && r.Type is { } rt && SymbolEqualityComparer.Default.Equals(lt, rt) && l.Kind == r.Kind ? lt : KindType(kind);
        if (op == ILOpCode.Mul && Native.LLVMTypeOf(value).IntWidth == 64)
        {
            if (r.Constant is int or long) return new IlEntry(kind, value, type) { Scaled = (l, Convert.ToInt64(r.Constant)) };
            if (l.Constant is int or long) return new IlEntry(kind, value, type) { Scaled = (r, Convert.ToInt64(l.Constant)) };
        }
        return new IlEntry(kind, value, type);
    }

    private ITypeSymbol KindType(IlKind kind) => kind switch
    {
        IlKind.Int32 => Special(SpecialType.System_Int32),
        IlKind.Int64 => Special(SpecialType.System_Int64),
        IlKind.NativeInt => Special(SpecialType.System_IntPtr),
        _ => Special(SpecialType.System_Object),
    };

    private IlEntry IlShift(ILOpCode op, IlEntry value, IlEntry amount)
    {
        var v = IlInt(value);
        var type = Native.LLVMTypeOf(v.Value);
        var count = IlInt(amount).Value;
        var countType = Native.LLVMTypeOf(count);
        if (countType.IntWidth != type.IntWidth)
            count = _b.Cast(countType.IntWidth < type.IntWidth ? LLVMOpcode.ZExt : LLVMOpcode.Trunc, count, type);
        // Counts >= the width are unspecified in IL; mask like the x64/arm64 hardware (and C#) so LLVM never sees poison.
        count = _b.BinOp(LLVMOpcode.And, count, L.ConstInt(type, type.IntWidth - 1));
        var result = _b.BinOp(op switch { ILOpCode.Shl => LLVMOpcode.Shl, ILOpCode.Shr => LLVMOpcode.AShr, _ => LLVMOpcode.LShr }, v.Value, count);
        return new IlEntry(v.Kind, result, KindType(v.Kind));
    }

    /// <summary>The i1 result of a comparison branch opcode (beq, bge.un, ...), also used for ceq/cgt/clt.</summary>
    /// <param name="negate">Return the comparison's exact negation (for floats: ordered ↔ unordered).</param>
    private LLVMValueRef IlCompare(ILOpCode op, IlEntry l, IlEntry r, bool negate = false)
    {
        if (l.Kind == IlKind.Float || r.Kind == IlKind.Float)
        {
            var (a, b) = IlFloats(l, r);
            var real = op switch
            {
                ILOpCode.Beq => LLVMRealPredicate.OEQ, ILOpCode.Bne_un => LLVMRealPredicate.UNE,
                ILOpCode.Bge => LLVMRealPredicate.OGE, ILOpCode.Bge_un => LLVMRealPredicate.UGE,
                ILOpCode.Bgt => LLVMRealPredicate.OGT, ILOpCode.Bgt_un => LLVMRealPredicate.UGT,
                ILOpCode.Ble => LLVMRealPredicate.OLE, ILOpCode.Ble_un => LLVMRealPredicate.ULE,
                ILOpCode.Blt => LLVMRealPredicate.OLT, _ => LLVMRealPredicate.ULT,
            };
            if (negate)
                real = real switch
                {
                    LLVMRealPredicate.OEQ => LLVMRealPredicate.UNE, LLVMRealPredicate.UNE => LLVMRealPredicate.OEQ,
                    LLVMRealPredicate.OGE => LLVMRealPredicate.ULT, LLVMRealPredicate.ULT => LLVMRealPredicate.OGE,
                    LLVMRealPredicate.OGT => LLVMRealPredicate.ULE, LLVMRealPredicate.ULE => LLVMRealPredicate.OGT,
                    LLVMRealPredicate.OLE => LLVMRealPredicate.UGT, LLVMRealPredicate.UGT => LLVMRealPredicate.OLE,
                    LLVMRealPredicate.OLT => LLVMRealPredicate.UGE, _ => LLVMRealPredicate.OLT, // UGE
                };
            return _b.FCmp(real, a, b);
        }
        var predicate = op switch
        {
            ILOpCode.Beq => LLVMIntPredicate.EQ, ILOpCode.Bne_un => LLVMIntPredicate.NE,
            ILOpCode.Bge => LLVMIntPredicate.SGE, ILOpCode.Bge_un => LLVMIntPredicate.UGE,
            ILOpCode.Bgt => LLVMIntPredicate.SGT, ILOpCode.Bgt_un => LLVMIntPredicate.UGT,
            ILOpCode.Ble => LLVMIntPredicate.SLE, ILOpCode.Ble_un => LLVMIntPredicate.ULE,
            ILOpCode.Blt => LLVMIntPredicate.SLT, _ => LLVMIntPredicate.ULT,
        };
        if (negate)
            predicate = predicate switch
            {
                LLVMIntPredicate.EQ => LLVMIntPredicate.NE, LLVMIntPredicate.NE => LLVMIntPredicate.EQ,
                LLVMIntPredicate.SGE => LLVMIntPredicate.SLT, LLVMIntPredicate.SLT => LLVMIntPredicate.SGE,
                LLVMIntPredicate.SGT => LLVMIntPredicate.SLE, LLVMIntPredicate.SLE => LLVMIntPredicate.SGT,
                LLVMIntPredicate.UGE => LLVMIntPredicate.ULT, LLVMIntPredicate.ULT => LLVMIntPredicate.UGE,
                LLVMIntPredicate.UGT => LLVMIntPredicate.ULE, _ => LLVMIntPredicate.UGT, // ULE
            };
        var lRef = l.Kind is IlKind.Ref or IlKind.ByRef or IlKind.Pointer;
        var rRef = r.Kind is IlKind.Ref or IlKind.ByRef or IlKind.Pointer;
        if (lRef && rRef) return _b.ICmp(predicate, l.Value, r.Value);
        var (x, y, _) = IlWiden(l, r);
        return _b.ICmp(predicate, x, y);
    }

    /// <summary>conv.* / conv.ovf.*: <paramref name="unsignedSource"/> for the .un forms (and the zero-extending conv.u/u8).</summary>
    private IlEntry IlConvert(IlEntry e, SpecialType to, bool check, bool unsignedSource)
    {
        var toType = Special(to);
        var target = T.Value(toType);
        if (e.Kind is IlKind.ByRef or IlKind.Pointer or IlKind.Ref)
        {
            if (to is SpecialType.System_IntPtr or SpecialType.System_UIntPtr && !check) // conv.u / conv.i on a pointer
                return new IlEntry(IlKind.NativeInt, _b.Cast(LLVMOpcode.PtrToInt, e.Value, T.NativeInt), toType);
            e = IlInt(e);
            unsignedSource = true;
        }
        ITypeSymbol from = e.Kind switch
        {
            IlKind.Int32 => Special(unsignedSource ? SpecialType.System_UInt32 : SpecialType.System_Int32),
            IlKind.Int64 => Special(unsignedSource ? SpecialType.System_UInt64 : SpecialType.System_Int64),
            IlKind.NativeInt => Special(unsignedSource ? SpecialType.System_UIntPtr : SpecialType.System_IntPtr),
            IlKind.Float => Special(Native.LLVMTypeOf(e.Value).Kind == LLVMTypeKind.Double ? SpecialType.System_Double : SpecialType.System_Single),
            _ => throw new IlException($"conversion of a {e.Kind} value"),
        };
        LLVMValueRef result;
        var source = Native.LLVMTypeOf(e.Value);
        if (!check && source.IsInteger && target.IsInteger)
        {
            // Unchecked integer -> integer: truncate, or extend by the *target's* signedness (conv.u8 of
            // an int32 zero-extends, conv.i8 sign-extends), as the CLI defines.
            result = source.IntWidth == target.IntWidth ? e.Value
                : source.IntWidth > target.IntWidth ? _b.Cast(LLVMOpcode.Trunc, e.Value, target)
                : _b.Cast(TypeMapper.IsSigned(toType) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, e.Value, target);
            // Keep what pointer arithmetic recognizes (IlBinary): widened constants and same-width scaled values.
            var pushed = IlPush(result, toType);
            if (source.IntWidth == target.IntWidth && e.Scaled is { } scaled) return pushed with { Scaled = scaled };
            if (source.IntWidth <= target.IntWidth && e.Constant is int or long)
            {
                var v = Convert.ToInt64(e.Constant);
                if (!TypeMapper.IsSigned(toType) && source.IntWidth == 32) v = (uint)(int)v;
                return pushed with { Constant = target.IntWidth == 64 ? v : (int)v };
            }
            return pushed;
        }
        else result = ConvertNumeric(e.Value, from, toType, IlAt(), isChecked: check);
        return IlPush(result, toType);
    }

    // ======================================================================================
    // Fields, arrays, indirect access
    // ======================================================================================

    private ITypeSymbol ResolveIlType(int token) => IlAssembly.ResolveType(token, IlBody.Context);

    private IFieldSymbol IlField(int token) =>
        IlAssembly.ResolveMember(token, IlBody.Context) as IFieldSymbol ?? throw new IlException($"token 0x{token:X8} is not a field");

    private IMethodSymbol IlMethod(int token) =>
        IlAssembly.ResolveMember(token, IlBody.Context) as IMethodSymbol ?? throw new IlException($"token 0x{token:X8} is not a method");

    /// <summary>A placeholder operation for helpers that want an IOperation for diagnostics.</summary>
    private IOperation IlAt() => new IlValueOperation(_il!.Syntax, Special(SpecialType.System_Object), () => default);

    /// <summary>Instance fields csharpc does not lay out (library classes): read through the IOperation path.</summary>
    private bool IlLibraryInstanceField(IFieldSymbol field)
    {
        var owner = _m.CoreLib.Map(S(field)).ContainingType;
        return !owner.IsValueType && !_m.IsUserType(owner);
    }

    /// <summary>A field-like event's backing field (Roslyn exposes only the event): its slot in csharpc's layout.</summary>
    private void IlEventBackingField(ILOpCode op, IEventSymbol e, Stack<IlEntry> stack)
    {
        IlEntry Pop() => stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");
        var value = op is ILOpCode.Stfld or ILOpCode.Stsfld ? Pop() : null;
        LLVMValueRef address;
        if (op is ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld)
        {
            if (TypeSubstitution.IsGenericContext(e.ContainingType)) throw new IlException($"static event '{e}' of a generic type");
            address = _m.StaticEventField(e);
        }
        else
        {
            var instance = Pop();
            if (instance.Kind != IlKind.Ref) throw new IlException($"event field '{e.Name}' of a {instance.Kind} value");
            if (!instance.NonNull) NullCheck(instance.Value);
            var layout = _m.GetLayout((INamedTypeSymbol)S(e.ContainingType));
            var index = layout.EventFieldIndex.FirstOrDefault(kv => kv.Key.Name == e.Name
                && SymbolEqualityComparer.Default.Equals(kv.Key.OriginalDefinition, e.OriginalDefinition)).Value;
            if (index == 0) throw new IlException($"event '{e.Name}' has no storage in the layout of '{layout.Type}'");
            address = _b.StructGep(layout.InstanceType, instance.Value, index, e.Name);
        }
        switch (op)
        {
            case ILOpCode.Ldflda or ILOpCode.Ldsflda: stack.Push(new IlEntry(IlKind.ByRef, address, e.Type)); break;
            case ILOpCode.Stfld or ILOpCode.Stsfld: _b.Store(IlRef(value!), address); break;
            default: stack.Push(new IlEntry(IlKind.Ref, _b.Load(L.Ptr, address), e.Type)); break;
        }
    }

    private LLVMValueRef IlInstanceFieldAddress(IFieldSymbol field, IlEntry instance)
    {
        var f = S(field);
        if (f.IsStatic) return StaticFieldAddress(f); // ldfld on a static field is legal IL
        switch (instance.Kind)
        {
            case IlKind.Ref when !f.ContainingType.IsValueType:
                if (!instance.NonNull) NullCheck(instance.Value);
                return FieldAddressOn(instance.Value, f);
            case IlKind.ByRef or IlKind.Pointer or IlKind.NativeInt when f.ContainingType.IsValueType:
                return FieldAddressOn(IlAddress(instance), f);
            case IlKind.Value when f.ContainingType.IsValueType:
                return FieldAddressOn(Spill(instance.Value), f);
            default:
                throw new IlException($"field '{field}' accessed through a {instance.Kind} value");
        }
    }

    private ITypeSymbol IlElementType(ILOpCode op, IlEntry array, IlInstruction instruction)
    {
        if (op is ILOpCode.Ldelem or ILOpCode.Stelem) return ResolveIlType(instruction.Token);
        var declared = array.Type is IArrayTypeSymbol arrayType ? arrayType.ElementType : null;
        var byOpcode = op switch
        {
            ILOpCode.Ldelem_i1 or ILOpCode.Stelem_i1 => SpecialType.System_SByte,
            ILOpCode.Ldelem_u1 => SpecialType.System_Byte,
            ILOpCode.Ldelem_i2 or ILOpCode.Stelem_i2 => SpecialType.System_Int16,
            ILOpCode.Ldelem_u2 => SpecialType.System_UInt16,
            ILOpCode.Ldelem_i4 or ILOpCode.Stelem_i4 => SpecialType.System_Int32,
            ILOpCode.Ldelem_u4 => SpecialType.System_UInt32,
            ILOpCode.Ldelem_i8 or ILOpCode.Stelem_i8 => SpecialType.System_Int64,
            ILOpCode.Ldelem_i or ILOpCode.Stelem_i => SpecialType.System_IntPtr,
            ILOpCode.Ldelem_r4 or ILOpCode.Stelem_r4 => SpecialType.System_Single,
            ILOpCode.Ldelem_r8 or ILOpCode.Stelem_r8 => SpecialType.System_Double,
            _ => SpecialType.System_Object,
        };
        // The array's static element type when it has the opcode's storage (bool[] via ldelem.u1, char[] via
        // ldelem.u2, enums, int[] read with ldelem.u4...): loads then extend by the element's real type.
        if (declared is not null)
        {
            var sd = S(declared);
            if (byOpcode == SpecialType.System_Object ? sd.IsReferenceType : T.Storage(sd).Handle == T.Storage(Special(byOpcode)).Handle)
                return declared;
        }
        return byOpcode == SpecialType.System_Object ? declared ?? Special(SpecialType.System_Object) : Special(byOpcode);
    }

    private LLVMValueRef IlElementAddress(IlEntry array, IlEntry index, ITypeSymbol elementType)
    {
        var il = _il!;
        // Array initializers (`newarr; dup; ldc i; ...; stelem`): a constant index into the array just
        // created with a constant length needs neither the null nor the bounds check.
        if (array.FreshLength is int length && index.Constant is int constant && constant >= 0 && constant < length)
        {
            var header = T.ArrayHeader(T.Storage(S(elementType)));
            return DataAddress(_b.InBoundsGep(header, IlRef(array), [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, constant)]), DataKind.ArrayElement);
        }
        var arrayType = array.Type as IArrayTypeSymbol ?? _m.Compilation.CreateArrayTypeSymbol(elementType);
        var i = IlInt(index);
        var indexType = Special(i.Kind == IlKind.Int32 ? SpecialType.System_Int32 : SpecialType.System_Int64);
        var reference = new IlArrayElementReferenceOperation(il.Syntax, elementType,
            new IlValueOperation(il.Syntax, arrayType, () => IlRef(array)),
            new IlValueOperation(il.Syntax, indexType, () => i.Value));
        return ElementAddress(reference);
    }

    private IlEntry IlLoadIndirect(IlEntry address, SpecialType type, bool isVolatile)
    {
        var t = Special(type);
        var pointer = IlAddress(address);
        // ldind on a pointer to bool/char/enum: keep the pointee's type when the storage matches.
        var pointee = address.Type is { } pt && T.Storage(S(pt)).Handle == T.Storage(t).Handle && !S(pt).IsReferenceType ? pt : t;
        return IlPush(isVolatile ? VolatileLoad(pointer, S(pointee)) : LoadValue(pointer, S(pointee)), pointee);
    }

    private void IlStoreIndirect(Stack<IlEntry> stack, SpecialType type, bool isVolatile)
    {
        if (stack.Count < 2) throw new IlException("evaluation stack underflow");
        var value = stack.Pop();
        var address = stack.Pop();
        var t = Special(type);
        var pointee = address.Type is { } pt && T.Storage(S(pt)).Handle == T.Storage(t).Handle && !S(pt).IsReferenceType ? pt : t;
        var typed = IlTyped(value, pointee);
        if (isVolatile) VolatileStore(typed, IlAddress(address), S(pointee));
        else StoreValue(ToStorage(typed, S(pointee)), IlAddress(address));
    }

    private void IlMemset(LLVMValueRef memory, LLVMValueRef bytes)
    {
        var f = _m.Intrinsic("llvm.memset.p0.i64", L.Void, L.Ptr, L.I8, L.I64, L.I1);
        _b.Call(f.Type, f.Fn, [memory, L.ConstInt(L.I8, 0), bytes, L.False]);
    }

    // ======================================================================================
    // Type operations
    // ======================================================================================

    private LLVMValueRef IlCastClass(LLVMValueRef value, ITypeSymbol type, ITypeSymbol? from = null)
    {
        var st = S(type);
        if (st is INamedTypeSymbol { TypeKind: TypeKind.Interface } itf && MayHoldArray(from is null ? null : S(from)))
            _m.RequestArrayInterface(itf);
        return CastClass(value, st, IlAt());
    }

    /// <summary>unbox.any for a value type: the payload of a boxed T (Nullable&lt;T&gt;: null is no value).</summary>
    private LLVMValueRef IlUnboxValue(LLVMValueRef obj, ITypeSymbol st)
    {
        if (!TypeMapper.IsNullable(st))
            return LoadValue(CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], obj, MethodTableFor(st, IlAt())), st);
        var underlying = TypeMapper.NullableUnderlying(st);
        var result = NewTemp(T.Value(st), "unbox.nullable");
        _b.Store(LlvmContext.Null(T.Value(st)), result);
        var some = NewBlock("unbox.value");
        var done = NewBlock("unbox.done");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.NE, obj, LlvmContext.Null(L.Ptr)), some, done);
        _b.PositionAtEnd(some);
        var payload = LoadValue(CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], obj, MethodTableFor(underlying, IlAt())), underlying);
        _b.Store(MakeNullable(st, L.True, payload), result);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(T.Value(st), result);
    }

    // ======================================================================================
    // RVA data (array initializers, span literals)
    // ======================================================================================

    /// <summary>A constant global with a field's RVA data (null if the field has none).</summary>
    private (LLVMValueRef Global, int Size)? IlRvaData(IFieldSymbol field)
    {
        if (!field.IsStatic || _m.Il.OwnerOf(field) is not { } owner) return null;
        var definition = owner.FieldDefinitionOf(field);
        if ((definition.Attributes & System.Reflection.FieldAttributes.HasFieldRVA) == 0) return null;
        if (_m.IlRvaGlobals.TryGetValue(field.OriginalDefinition, out var existing)) return existing;
        var type = S(field.Type);
        var size = type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, owner.Symbol) ? owner.ClassSize(named) : 0;
        if (size == 0) size = (int)T.Target.SizeOf(T.Storage(type));
        var data = owner.FieldData(field, size);
        var init = LlvmContext.ConstArray(L.I8, [.. data.Select(b => L.ConstInt(L.I8, b))]);
        var global = _m.Module.AddGlobal(".rva", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        Native.LLVMSetAlignment(global, 8);
        return _m.IlRvaGlobals[field.OriginalDefinition] = (global, size);
    }
}
