using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// The runtime's and libraries' default event sources (#455). With event sources turned on (EventSourceSupport=true, the
/// System.Diagnostics.Tracing.EventSource.IsSupported switch), .NET creates them before the program runs: CoreCLR from coreclr_initialize,
/// NativeAOT through CoreLib's [ModuleInitializer] EventSource.InitializeDefaultEventSources. The framework's own method runs here, after the
/// static initialization and before the program's module initializers, so EventSource.GetSources() and an EventListener see
/// Microsoft-Windows-DotNETRuntime, FrameworkEventSource, ArrayPoolEventSource and System.Runtime, and System.Diagnostics.Metrics is
/// preregistered. Off (the file-based and PublishAot default, NativeAOT's), nothing is emitted, so such a program pays nothing.
/// A project that leaves the switch unset (a build without PublishAot, where .NET's JIT would create them) does not get them either:
/// csharpc is the AOT compiler, and PublishAot's default is off.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private bool DefaultEventSourcesOn => Frontend.FrameworkIl.DefaultEventSourcesOn(RuntimeConfig);

    private void EmitDefaultEventSources(IrBuilder b)
    {
        if (!DefaultEventSourcesOn) return;
        var owner = Compilation.GetTypeByMetadataName(CoreLib.Namespace + ".UpstreamTracing")
            ?? throw CompileError.Unsupported("corelib type 'UpstreamTracing' not found (is src/Csharpc/corelib shipped?)");
        var start = GetFunction(owner.GetMembers("StartDefaultEventSources").OfType<IMethodSymbol>().Single());
        b.Call(start.Type, start.Fn, []);
    }
}
