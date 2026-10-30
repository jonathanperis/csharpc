// The IDynamicMetaObjectProvider protocol of ExpandoObject and DynamicObject for `dynamic` operations, which
// csharpc binds at compile time (docs/adr/0019-dynamic.md). On .NET their meta-objects build expression trees
// that call these same members (ExpandoObject's dictionary; DynamicObject's TryGetMember & co. with a binder
// describing the operation); the code bound for a dynamic operation calls them directly. Only programs that
// apply a dynamic operation to one of these types reach this file.
#nullable disable
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;

namespace CsharpcCoreLib
{
    public static class DynamicObjects
    {
        // ExpandoObject: its members are its dictionary's entries (case-sensitive, as C# binds them).
        public static bool ExpandoTryGetMember(object expando, string name, out object value) =>
            ((IDictionary<string, object>)expando).TryGetValue(name, out value);

        public static void ExpandoSetMember(object expando, string name, object value) =>
            ((IDictionary<string, object>)expando)[name] = value;

        // DynamicObject: what its Try* overrides make of the operation (false: the C# binder's error).
        public static bool TryGetMember(object target, string name, out object result) =>
            ((DynamicObject)target).TryGetMember(new CSharpGetMemberBinder(name), out result);

        public static bool TrySetMember(object target, string name, object value) =>
            ((DynamicObject)target).TrySetMember(new CSharpSetMemberBinder(name), value);

        public static bool TryInvokeMember(object target, string name, object[] args, string[] names, out object result) =>
            ((DynamicObject)target).TryInvokeMember(new CSharpInvokeMemberBinder(name, new CallInfo(args.Length, names)), args, out result);

        public static bool TryGetIndex(object target, object[] indexes, string[] names, out object result) =>
            ((DynamicObject)target).TryGetIndex(new CSharpGetIndexBinder(new CallInfo(indexes.Length, names)), indexes, out result);

        public static bool TrySetIndex(object target, object[] indexes, string[] names, object value) =>
            ((DynamicObject)target).TrySetIndex(new CSharpSetIndexBinder(new CallInfo(indexes.Length, names)), indexes, value);

        public static bool TryInvoke(object target, object[] args, string[] names, out object result) =>
            ((DynamicObject)target).TryInvoke(new CSharpInvokeBinder(new CallInfo(args.Length, names)), args, out result);

        public static bool TryBinaryOperation(object target, ExpressionType operation, object arg, out object result) =>
            ((DynamicObject)target).TryBinaryOperation(new CSharpBinaryOperationBinder(operation), arg, out result);

        public static bool TryUnaryOperation(object target, ExpressionType operation, out object result) =>
            ((DynamicObject)target).TryUnaryOperation(new CSharpUnaryOperationBinder(operation), out result);

        public static bool TryConvert(object target, Type type, bool isExplicit, out object result) =>
            ((DynamicObject)target).TryConvert(new CSharpConvertBinder(type, isExplicit), out result);

        // The binders' Fallback* methods produce expression trees for the DLR; csharpc has already bound the
        // operation, so a DynamicObject calling them back has nothing to fall back to.
        internal static DynamicMetaObject NoFallback() =>
            throw new NotSupportedException("Not supported by csharpc yet: calling a dynamic binder's Fallback method (csharpc binds dynamic operations at compile time).");
    }

    // The names of Microsoft.CSharp's binders, which is what a DynamicObject sees on .NET.
    internal sealed class CSharpGetMemberBinder : GetMemberBinder
    {
        public CSharpGetMemberBinder(string name) : base(name, false) { }
        public override DynamicMetaObject FallbackGetMember(DynamicMetaObject target, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpSetMemberBinder : SetMemberBinder
    {
        public CSharpSetMemberBinder(string name) : base(name, false) { }
        public override DynamicMetaObject FallbackSetMember(DynamicMetaObject target, DynamicMetaObject value, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpInvokeMemberBinder : InvokeMemberBinder
    {
        public CSharpInvokeMemberBinder(string name, CallInfo callInfo) : base(name, false, callInfo) { }
        public override DynamicMetaObject FallbackInvokeMember(DynamicMetaObject target, DynamicMetaObject[] args, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
        public override DynamicMetaObject FallbackInvoke(DynamicMetaObject target, DynamicMetaObject[] args, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpGetIndexBinder : GetIndexBinder
    {
        public CSharpGetIndexBinder(CallInfo callInfo) : base(callInfo) { }
        public override DynamicMetaObject FallbackGetIndex(DynamicMetaObject target, DynamicMetaObject[] indexes, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpSetIndexBinder : SetIndexBinder
    {
        public CSharpSetIndexBinder(CallInfo callInfo) : base(callInfo) { }
        public override DynamicMetaObject FallbackSetIndex(DynamicMetaObject target, DynamicMetaObject[] indexes, DynamicMetaObject value, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpInvokeBinder : InvokeBinder
    {
        public CSharpInvokeBinder(CallInfo callInfo) : base(callInfo) { }
        public override DynamicMetaObject FallbackInvoke(DynamicMetaObject target, DynamicMetaObject[] args, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpBinaryOperationBinder : BinaryOperationBinder
    {
        public CSharpBinaryOperationBinder(ExpressionType operation) : base(operation) { }
        public override DynamicMetaObject FallbackBinaryOperation(DynamicMetaObject target, DynamicMetaObject arg, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpUnaryOperationBinder : UnaryOperationBinder
    {
        public CSharpUnaryOperationBinder(ExpressionType operation) : base(operation) { }
        public override DynamicMetaObject FallbackUnaryOperation(DynamicMetaObject target, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }

    internal sealed class CSharpConvertBinder : ConvertBinder
    {
        public CSharpConvertBinder(Type type, bool isExplicit) : base(type, isExplicit) { }
        public override DynamicMetaObject FallbackConvert(DynamicMetaObject target, DynamicMetaObject errorSuggestion) => DynamicObjects.NoFallback();
    }
}
