using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>More BCL members: primitive Equals(object)/GetHashCode() and Delegate.GetInvocationList().</summary>
internal static partial class BclValueTypes
{
    private static void AddMoreMethods(Dictionary<string, BclHandler> t)
    {
        // ---- primitive Equals(object) / GetHashCode() ------------------------------------------------
        foreach (var type in new[] { "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort", "char", "bool", "double", "float" })
        {
            t[$"{type}.Equals(object)"] = (g, m, self, a, at) =>
            {
                var boxed = g.Box(self, m.ContainingType, at);
                return Rt(g, "csrt_object_equals_static", g.Llvm.I1, (boxed, g.Llvm.Ptr), (a[0], g.Llvm.Ptr));
            };
            t[$"{type}.GetHashCode()"] = (g, m, self, _, at) => g.HashOf(m.ContainingType, self, at);
        }

        // Delegate.Combine / Remove called as methods (compiled event accessors, the IL front-end).
        t["System.Delegate.Combine(System.Delegate, System.Delegate)"] = (g, _, _, a, _) => g.CombineDelegates(a[0], a[1]);
        t["System.Delegate.Remove(System.Delegate, System.Delegate)"] = (g, _, _, a, _) => g.RemoveDelegate(a[0], a[1]);

        t["System.Delegate.GetInvocationList()"] = (g, _, self, _, _) =>
        {
            var array = Rt(g, "csrt_delegate_get_invocation_list", g.Llvm.Ptr, (self, g.Llvm.Ptr));
            g.TagArray(array, g.Module.Compilation.GetTypeByMetadataName("System.Delegate")!);
            return array;
        };
    }
}
