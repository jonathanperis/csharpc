// RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter, an internal member of System.Private.CoreLib that the framework's IL calls
// (ComparerHelpers.CreateDefaultComparer / CreateDefaultEqualityComparer, ArraySortHelper<T>.CreateArraySortHelper): "make a G<X> over
// this other type". The IL front-end lowers a call whose arguments are typeof constants to the allocation and default constructor of G<T>
// at compile time, as NativeAOT's ILC expands it (FunctionCodeGen.IlGenericInstantiation.cs). This declaration only gives the call a
// target to bind to (RuntimeType is System.Type to csharpc); a call it cannot expand at compile time ends here.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.RuntimeTypeHandle")]
    internal static class RuntimeTypeHandleMembers
    {
        public static object CreateInstanceForAnotherGenericParameter(Type type, Type genericParameter) =>
            throw new NotSupportedException("RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter needs typeof constants for both types: csharpc instantiates generics at compile time.");

        public static object CreateInstanceForAnotherGenericParameter(Type type, Type genericParameter1, Type genericParameter2) =>
            throw new NotSupportedException("RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter needs typeof constants for all types: csharpc instantiates generics at compile time.");
    }
}
