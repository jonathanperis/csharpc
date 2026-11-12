// The type argument of shared generic code (ADR 0026): a generic instantiation over reference types is compiled once, as its canonical form,
// with __Canon in place of every reference-type argument (NativeAOT's System.__Canon). Never instantiated; its representation is object's.
// Abstract and not sealed: a type parameter stands for any class, so no lowering may reason from the marker being a leaf type (a sealed
// element type needs no array covariance check, and a T[] seen as a __Canon[] would then take any store: issue #362).
namespace CsharpcCoreLib
{
    internal abstract class __Canon
    {
        private __Canon() { }
    }
}
