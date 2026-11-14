// The type arguments of shared generic code (ADR 0026): a generic instantiation over reference types is compiled once, as its canonical
// form, with a positional marker in place of each reference-type argument: __CanonN stands for the class's N-th type parameter (counted
// through the classes that contain it), so a canonical body can tell List<TKey> from List<TValue> (NativeAOT's lookups are keyed on the open
// signature for the same reason). Never instantiated, never observable; their representation is object's. Abstract and not sealed: a
// type parameter can stand for any class, so nothing may reason from the marker being a leaf (array covariance checks: issue #362).
namespace CsharpcCoreLib
{
    internal abstract class __Canon0 { private __Canon0() { } }
    internal abstract class __Canon1 { private __Canon1() { } }
    internal abstract class __Canon2 { private __Canon2() { } }
    internal abstract class __Canon3 { private __Canon3() { } }
    internal abstract class __Canon4 { private __Canon4() { } }
    internal abstract class __Canon5 { private __Canon5() { } }
    internal abstract class __Canon6 { private __Canon6() { } }
    internal abstract class __Canon7 { private __Canon7() { } }
}
