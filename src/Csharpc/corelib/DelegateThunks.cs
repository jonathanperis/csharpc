// Marshal.GetFunctionPointerForDelegate / GetDelegateForFunctionPointer. Native code calls a delegate through
// a code address, and csharpc generates no code at run time (as NativeAOT does not), so codegen emits a fixed
// pool of reverse P/Invoke thunks per delegate type that is marshalled (ModuleCodeGen.ReverseThunks.cs) and
// this class hands the slots out: slot i holds a weak GC handle to a delegate, and thunk i calls it. As in .NET,
// the delegate must stay reachable while native code can call it; a slot whose delegate was collected is reused.
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class DelegateThunks
    {
        private static readonly object s_lock = new object();

        [DllImport("__Internal")] private static extern IntPtr csrt_delegate_create(IntPtr methodTable, IntPtr target, IntPtr fn);

        // The pool is { count, thunk[count], handle[count] } as pointer-sized words.
        public static IntPtr GetPointer(Delegate d, IntPtr table)
        {
            if (d == null) throw new ArgumentNullException("d");
            CheckType(table, "delegate");
            IntPtr* pool = (IntPtr*)table;
            int count = (int)pool[0];
            IntPtr* thunks = pool + 1;
            IntPtr* handles = pool + 1 + count;
            lock (s_lock)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int free = -1;
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr handle = handles[i];
                        if (handle == IntPtr.Zero)
                        {
                            if (free < 0) free = i;
                            continue;
                        }
                        object target = WeakSlots.Target(handle);
                        if (ReferenceEquals(target, d)) return thunks[i];
                        if (target == null)
                        {
                            // Its delegate was collected: native code that still calls it is as wrong as in .NET.
                            WeakSlots.csrt_weak_free(handle);
                            handles[i] = IntPtr.Zero;
                            if (free < 0) free = i;
                        }
                    }
                    if (free >= 0)
                    {
                        handles[free] = WeakSlots.Alloc(d, null);
                        return thunks[free];
                    }
                    // Every slot holds a live delegate, unless some are garbage that no collection has found yet.
                    if (attempt == 0) GC.Collect();
                }
            }
            throw new InvalidOperationException("More than " + count + " delegates of type " + d.GetType().FullName
                + " are marshalled to native code at once; csharpc has a fixed number of native callback thunks per delegate type"
                + " (Marshal.GetFunctionPointerForDelegate), and a collected delegate frees its thunk.");
        }

        public static IntPtr GetPointerUntyped(Delegate d)
        {
            if (d == null) throw new ArgumentNullException("d");
            throw new NotSupportedException("Marshal.GetFunctionPointerForDelegate(Delegate) of a delegate known only as System.Delegate is not supported by csharpc yet:"
                + " use the generic overload GetFunctionPointerForDelegate<" + d.GetType().Name + ">, whose delegate type is known at compile time.");
        }

        public static object FromPointer<T>(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) throw new ArgumentNullException("ptr");
            IntPtr methodTable = CoreIntrinsics.DelegateTypeTable<T>();
            CheckType(methodTable, "t");
            // The delegate's target is the native pointer, boxed (the object the GC keeps); its code calls it.
            object boxed = ptr;
            IntPtr d = csrt_delegate_create(methodTable, CoreIntrinsics.ObjectToPointer(boxed), CoreIntrinsics.NativeInvokeThunk<T>());
            return CoreIntrinsics.PointerToObject(d);
        }

        private static void CheckType(IntPtr table, string parameter)
        {
            if (table == IntPtr.Zero) throw new ArgumentException("The specified Type must not be a generic type.", parameter);
            if (table == (IntPtr)(-1)) throw new ArgumentException("The specified Type must be a delegate type.", parameter);
        }
    }
}
