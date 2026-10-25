namespace IlReachability;

// The shape of Microsoft.Extensions.Configuration's providers: an interface implemented by an abstract
// base class's virtual method, which the concrete class overrides. An interface call reaches the override.
public interface IProvider { string Load(); }

public abstract class ProviderBase : IProvider
{
    public virtual string Load() => "base";
}

public sealed class DynamicProvider : ProviderBase
{
    // Run-time code generation, which an ahead-of-time compiler can never support.
    public override string Load() => new System.Reflection.Emit.DynamicMethod("m", typeof(string), new Type[0]).Name;
}

public static class Providers
{
    public static IProvider Create() => new DynamicProvider();
}
