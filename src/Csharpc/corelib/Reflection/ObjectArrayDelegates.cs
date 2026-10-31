// NativeAOT's Internal.Runtime.Augments.DynamicDelegateAugments.CreateObjectArrayDelegate, which
// System.Linq.Expressions' interpreter calls (through an [UnsafeAccessor]) to turn a compiled expression
// tree into a delegate of its type: Invoke boxes the arguments into an object[] and calls the handler.
// The thunk per delegate type is generated at compile time (ModuleCodeGen.ObjectArrayDelegates.cs).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static unsafe class ObjectArrayDelegates
    {
        public static Delegate Create(Type delegateType, Func<object[], object> handler)
        {
            ArgumentNullException.ThrowIfNull(delegateType);
            ArgumentNullException.ThrowIfNull(handler);
            nint mt = CoreIntrinsics.csrt_type_handle(delegateType);
            byte* table = (byte*)CoreIntrinsics.ObjectArrayThunkTableAddress();
            long count = *(long*)table;
            nint* entries = (nint*)*(nint*)(table + 8);
            for (long i = 0; i < count; i++)
                if (entries[2 * i] == mt)
                    return CoreIntrinsics.ReinterpretAs<Delegate>(CoreIntrinsics.PointerToObject((nint)CoreIntrinsics.csrt_delegate_create(
                        mt, CoreIntrinsics.ObjectToPointer(handler), entries[2 * i + 1])));
            throw new NotSupportedException("A delegate of type '" + delegateType + "' for an interpreted expression tree is not supported by csharpc yet.");
        }
    }
}
