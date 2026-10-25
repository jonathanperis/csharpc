// The attribute classes of System.Private.CoreLib that compilers and the trimming/nullable analyses put
// on members (dotnet/runtime release/11.0 shapes: the same constructors, fields and properties), so
// reflection over members (Reflection/Members.cs) can create them as GetCustomAttributes does.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.CompilerServices.NullableAttribute")]
    public sealed class NullableAttributeImpl : Attribute
    {
        public readonly byte[] NullableFlags;
        public NullableAttributeImpl(byte value) { NullableFlags = new[] { value }; }
        public NullableAttributeImpl(byte[] value) { NullableFlags = value; }
    }

    [Shadows("System.Runtime.CompilerServices.NullableContextAttribute")]
    public sealed class NullableContextAttributeImpl : Attribute
    {
        public readonly byte Flag;
        public NullableContextAttributeImpl(byte value) { Flag = value; }
    }

    [Shadows("System.Runtime.CompilerServices.NullablePublicOnlyAttribute")]
    public sealed class NullablePublicOnlyAttributeImpl : Attribute
    {
        public readonly bool IncludesInternals;
        public NullablePublicOnlyAttributeImpl(bool value) { IncludesInternals = value; }
    }

    [Shadows("System.Runtime.CompilerServices.CompilerGeneratedAttribute")]
    public sealed class CompilerGeneratedAttributeImpl : Attribute { public CompilerGeneratedAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.ExtensionAttribute")]
    public sealed class ExtensionAttributeImpl : Attribute { public ExtensionAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.IsReadOnlyAttribute")]
    public sealed class IsReadOnlyAttributeImpl : Attribute { public IsReadOnlyAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.ScopedRefAttribute")]
    public sealed class ScopedRefAttributeImpl : Attribute { public ScopedRefAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.RequiresLocationAttribute")]
    public sealed class RequiresLocationAttributeImpl : Attribute { public RequiresLocationAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.EnumeratorCancellationAttribute")]
    public sealed class EnumeratorCancellationAttributeImpl : Attribute { public EnumeratorCancellationAttributeImpl() { } }

    [Shadows("System.ParamArrayAttribute")]
    public sealed class ParamArrayAttributeImpl : Attribute { public ParamArrayAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.ParamCollectionAttribute")]
    public sealed class ParamCollectionAttributeImpl : Attribute { public ParamCollectionAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.StateMachineAttribute")]
    public class StateMachineAttributeImpl : Attribute
    {
        public StateMachineAttributeImpl(Type stateMachineType) { StateMachineType = stateMachineType; }
        public Type StateMachineType { get; }
    }

    [Shadows("System.Runtime.CompilerServices.AsyncStateMachineAttribute")]
    public sealed class AsyncStateMachineAttributeImpl : System.Runtime.CompilerServices.StateMachineAttribute { public AsyncStateMachineAttributeImpl(Type stateMachineType) : base(stateMachineType) { } }

    [Shadows("System.Runtime.CompilerServices.IteratorStateMachineAttribute")]
    public sealed class IteratorStateMachineAttributeImpl : System.Runtime.CompilerServices.StateMachineAttribute { public IteratorStateMachineAttributeImpl(Type stateMachineType) : base(stateMachineType) { } }

    [Shadows("System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute")]
    public sealed class AsyncIteratorStateMachineAttributeImpl : System.Runtime.CompilerServices.StateMachineAttribute { public AsyncIteratorStateMachineAttributeImpl(Type stateMachineType) : base(stateMachineType) { } }

    [Shadows("System.Runtime.CompilerServices.TupleElementNamesAttribute")]
    public sealed class TupleElementNamesAttributeImpl : Attribute
    {
        private readonly string[] _transformNames;
        public TupleElementNamesAttributeImpl(string[] transformNames)
        {
            ArgumentNullException.ThrowIfNull(transformNames);
            _transformNames = transformNames;
        }
        public IList<string> TransformNames => _transformNames;
    }

    [Shadows("System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute")]
    public sealed class DynamicallyAccessedMembersAttributeImpl : Attribute
    {
        public DynamicallyAccessedMembersAttributeImpl(DynamicallyAccessedMemberTypes memberTypes) { MemberTypes = memberTypes; }
        public DynamicallyAccessedMemberTypes MemberTypes { get; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute")]
    public sealed class UnconditionalSuppressMessageAttributeImpl : Attribute
    {
        public UnconditionalSuppressMessageAttributeImpl(string category, string checkId) { Category = category; CheckId = checkId; }
        public string Category { get; }
        public string CheckId { get; }
        public string Scope { get; set; }
        public string Target { get; set; }
        public string MessageId { get; set; }
        public string Justification { get; set; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute")]
    public sealed class RequiresUnreferencedCodeAttributeImpl : Attribute
    {
        public RequiresUnreferencedCodeAttributeImpl(string message) { Message = message; }
        public bool ExcludeStatics { get; set; }
        public string Message { get; }
        public string Url { get; set; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute")]
    public sealed class RequiresDynamicCodeAttributeImpl : Attribute
    {
        public RequiresDynamicCodeAttributeImpl(string message) { Message = message; }
        public bool ExcludeStatics { get; set; }
        public string Message { get; }
        public string Url { get; set; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.AllowNullAttribute")]
    public sealed class AllowNullAttributeImpl : Attribute { public AllowNullAttributeImpl() { } }

    [Shadows("System.Diagnostics.CodeAnalysis.DisallowNullAttribute")]
    public sealed class DisallowNullAttributeImpl : Attribute { public DisallowNullAttributeImpl() { } }

    [Shadows("System.Diagnostics.CodeAnalysis.MaybeNullAttribute")]
    public sealed class MaybeNullAttributeImpl : Attribute { public MaybeNullAttributeImpl() { } }

    [Shadows("System.Diagnostics.CodeAnalysis.NotNullAttribute")]
    public sealed class NotNullAttributeImpl : Attribute { public NotNullAttributeImpl() { } }

    [Shadows("System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute")]
    public sealed class DoesNotReturnAttributeImpl : Attribute { public DoesNotReturnAttributeImpl() { } }

    [Shadows("System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute")]
    public sealed class MaybeNullWhenAttributeImpl : Attribute
    {
        public MaybeNullWhenAttributeImpl(bool returnValue) { ReturnValue = returnValue; }
        public bool ReturnValue { get; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute")]
    public sealed class NotNullWhenAttributeImpl : Attribute
    {
        public NotNullWhenAttributeImpl(bool returnValue) { ReturnValue = returnValue; }
        public bool ReturnValue { get; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute")]
    public sealed class DoesNotReturnIfAttributeImpl : Attribute
    {
        public DoesNotReturnIfAttributeImpl(bool parameterValue) { ParameterValue = parameterValue; }
        public bool ParameterValue { get; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute")]
    public sealed class NotNullIfNotNullAttributeImpl : Attribute
    {
        public NotNullIfNotNullAttributeImpl(string parameterName) { ParameterName = parameterName; }
        public string ParameterName { get; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.MemberNotNullAttribute")]
    public sealed class MemberNotNullAttributeImpl : Attribute
    {
        public MemberNotNullAttributeImpl(string member) { Members = new[] { member }; }
        public MemberNotNullAttributeImpl(params string[] members) { Members = members; }
        public string[] Members { get; }
    }

    [Shadows("System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute")]
    public sealed class MemberNotNullWhenAttributeImpl : Attribute
    {
        public MemberNotNullWhenAttributeImpl(bool returnValue, string member) { ReturnValue = returnValue; Members = new[] { member }; }
        public MemberNotNullWhenAttributeImpl(bool returnValue, params string[] members) { ReturnValue = returnValue; Members = members; }
        public bool ReturnValue { get; }
        public string[] Members { get; }
    }

    [Shadows("System.ObsoleteAttribute")]
    public sealed class ObsoleteAttributeImpl : Attribute
    {
        public ObsoleteAttributeImpl() { }
        public ObsoleteAttributeImpl(string message) { Message = message; }
        public ObsoleteAttributeImpl(string message, bool error) { Message = message; IsError = error; }
        public string Message { get; }
        public bool IsError { get; }
        public string DiagnosticId { get; set; }
        public string UrlFormat { get; set; }
    }

    [Shadows("System.ComponentModel.EditorBrowsableAttribute")]
    public sealed class EditorBrowsableAttributeImpl : Attribute
    {
        public EditorBrowsableAttributeImpl(EditorBrowsableState state) { State = state; }
        public EditorBrowsableAttributeImpl() : this(EditorBrowsableState.Always) { }
        public EditorBrowsableState State { get; }
    }

    [Shadows("System.Diagnostics.DebuggerHiddenAttribute")]
    public sealed class DebuggerHiddenAttributeImpl : Attribute { public DebuggerHiddenAttributeImpl() { } }

    [Shadows("System.Diagnostics.DebuggerStepThroughAttribute")]
    public sealed class DebuggerStepThroughAttributeImpl : Attribute { public DebuggerStepThroughAttributeImpl() { } }

    [Shadows("System.Diagnostics.DebuggerNonUserCodeAttribute")]
    public sealed class DebuggerNonUserCodeAttributeImpl : Attribute { public DebuggerNonUserCodeAttributeImpl() { } }

    [Shadows("System.Diagnostics.StackTraceHiddenAttribute")]
    public sealed class StackTraceHiddenAttributeImpl : Attribute { public StackTraceHiddenAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.CallerMemberNameAttribute")]
    public sealed class CallerMemberNameAttributeImpl : Attribute { public CallerMemberNameAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.CallerFilePathAttribute")]
    public sealed class CallerFilePathAttributeImpl : Attribute { public CallerFilePathAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.CallerLineNumberAttribute")]
    public sealed class CallerLineNumberAttributeImpl : Attribute { public CallerLineNumberAttributeImpl() { } }

    [Shadows("System.Runtime.CompilerServices.CallerArgumentExpressionAttribute")]
    public sealed class CallerArgumentExpressionAttributeImpl : Attribute
    {
        public CallerArgumentExpressionAttributeImpl(string parameterName) { ParameterName = parameterName; }
        public string ParameterName { get; }
    }
}
