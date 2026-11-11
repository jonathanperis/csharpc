// The type argument of shared generic code (ADR 0026): a generic instantiation over reference types is compiled once, as its canonical form,
// with __Canon in place of every reference-type argument (NativeAOT's System.__Canon). Never instantiated; its representation is object's.
namespace CsharpcCoreLib
{
    public sealed class __Canon
    {
        private __Canon() { }
    }
}
