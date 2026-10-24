// The assembly-level attributes Assembly.GetCustomAttribute<T>() hands out (FunctionCodeGen.AssemblyAttributes.cs
// constructs them from IL assemblies' metadata at compile time): plain classes over their constructor
// arguments, as in .NET. Roslyn reads its own version and commit hash this way.
#nullable disable
using System;
using System.Reflection;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.AssemblyInformationalVersionAttribute")]
    public sealed class AssemblyInformationalVersionAttributeImpl : Attribute
    {
        public AssemblyInformationalVersionAttributeImpl(string informationalVersion) { InformationalVersion = informationalVersion; }
        public string InformationalVersion { get; }
    }

    [Shadows("System.Reflection.AssemblyFileVersionAttribute")]
    public sealed class AssemblyFileVersionAttributeImpl : Attribute
    {
        public AssemblyFileVersionAttributeImpl(string version) { Version = version ?? throw new ArgumentNullException("version"); }
        public string Version { get; }
    }

    [Shadows("System.Reflection.AssemblyVersionAttribute")]
    public sealed class AssemblyVersionAttributeImpl : Attribute
    {
        public AssemblyVersionAttributeImpl(string version) { Version = version; }
        public string Version { get; }
    }

    [Shadows("System.Reflection.AssemblyProductAttribute")]
    public sealed class AssemblyProductAttributeImpl : Attribute
    {
        public AssemblyProductAttributeImpl(string product) { Product = product; }
        public string Product { get; }
    }

    [Shadows("System.Reflection.AssemblyCompanyAttribute")]
    public sealed class AssemblyCompanyAttributeImpl : Attribute
    {
        public AssemblyCompanyAttributeImpl(string company) { Company = company; }
        public string Company { get; }
    }

    [Shadows("System.Reflection.AssemblyTitleAttribute")]
    public sealed class AssemblyTitleAttributeImpl : Attribute
    {
        public AssemblyTitleAttributeImpl(string title) { Title = title; }
        public string Title { get; }
    }

    [Shadows("System.Reflection.AssemblyConfigurationAttribute")]
    public sealed class AssemblyConfigurationAttributeImpl : Attribute
    {
        public AssemblyConfigurationAttributeImpl(string configuration) { Configuration = configuration; }
        public string Configuration { get; }
    }

    [Shadows("System.Reflection.AssemblyCopyrightAttribute")]
    public sealed class AssemblyCopyrightAttributeImpl : Attribute
    {
        public AssemblyCopyrightAttributeImpl(string copyright) { Copyright = copyright; }
        public string Copyright { get; }
    }

    [Shadows("System.Reflection.AssemblyDescriptionAttribute")]
    public sealed class AssemblyDescriptionAttributeImpl : Attribute
    {
        public AssemblyDescriptionAttributeImpl(string description) { Description = description; }
        public string Description { get; }
    }

    [Shadows("System.Reflection.AssemblyMetadataAttribute")]
    public sealed class AssemblyMetadataAttributeImpl : Attribute
    {
        public AssemblyMetadataAttributeImpl(string key, string value) { Key = key; Value = value; }
        public string Key { get; }
        public string Value { get; }
    }

    [Shadows("System.Runtime.Versioning.TargetFrameworkAttribute")]
    public sealed class TargetFrameworkAttributeImpl : Attribute
    {
        public TargetFrameworkAttributeImpl(string frameworkName) { FrameworkName = frameworkName ?? throw new ArgumentNullException("frameworkName"); }
        public string FrameworkName { get; }
        public string FrameworkDisplayName { get; set; }
    }
}
