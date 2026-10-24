// A virtual call whose only implementation is inherited from a generic base class instantiation
// (Roslyn: CommonPEModuleBuilder -> PEModuleBuilder<TCompilation, ...> -> CSharp PEModuleBuilder):
// devirtualization must call that instantiation, not the open generic method.
ModuleBuilder builder = new CSharpModuleBuilder();
Console.WriteLine(builder.Describe());
Console.WriteLine(builder.Translate(41));
Console.WriteLine(builder.Name);
Console.WriteLine(Call(builder));

static string Call(ModuleBuilder b) => b.Describe() + "/" + b.Translate(1);

abstract class ModuleBuilder
{
    public abstract string Describe();
    public abstract int Translate(int value);
    public virtual string Name => "module";
}

abstract class ModuleBuilder<TCompilation, TSymbol> : ModuleBuilder where TCompilation : new()
{
    private readonly TCompilation _compilation = new();
    public override string Describe() => $"{typeof(TCompilation).Name}+{typeof(TSymbol).Name}: {_compilation}";
    public override int Translate(int value) => value + Offset;
    protected abstract int Offset { get; }
    public override string Name => "generic " + base.Name;
}

sealed class CSharpModuleBuilder : ModuleBuilder<CSharpCompilation, string>
{
    protected override int Offset => 1;
}

sealed class CSharpCompilation
{
    public override string ToString() => "compilation";
}
