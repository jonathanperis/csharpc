// System.Reflection.Assembly and System.Version (AssemblyName: AssemblyName.cs): what Type.Assembly,
// Assembly.GetExecutingAssembly() and GetEntryAssembly() hand out. csharpc compiles a closed world,
// so an assembly is just its identity: the full name, computed at compile time
// (ModuleCodeGen.TypeInfo.cs: the program's own assemblies, IL assemblies, and the shared framework's
// implementation assemblies reached through type forwards, e.g. System.Private.CoreLib).
#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace CsharpcCoreLib
{
    /// <summary>
    /// System.Reflection.Module: an assembly's one (manifest) module. Its name is the assembly's file name as .NET
    /// loads it ("App.dll", "System.Private.CoreLib.dll"); an IL assembly's module id comes from its metadata
    /// (compiled in: ModuleCodeGen.ModuleVersionIdsBlob). The module's path (FullyQualifiedName) and the module id of
    /// an assembly csharpc compiled from source stay "not supported": assemblies are compiled into the program.
    /// </summary>
    [Shadows("System.Reflection.Module")]
    public abstract class ModuleImpl
    {
        protected ModuleImpl() { }
        public virtual Guid ModuleVersionId => throw new NotSupportedException("Module.ModuleVersionId is not supported by csharpc yet.");
        public virtual string ScopeName => throw new NotSupportedException("Module.ScopeName is not supported by csharpc yet.");
        public virtual Assembly Assembly => throw new NotSupportedException("Module.Assembly is not supported by csharpc yet.");
        public virtual string Name => throw new NotSupportedException("Module.Name is not supported by csharpc yet (the module has no file).");
        public virtual string FullyQualifiedName => throw new NotSupportedException("Module.FullyQualifiedName is not supported by csharpc yet (the module has no file).");
        /// <summary>The manifest module's token (0x00000001), the only module an assembly of csharpc's has.</summary>
        public virtual int MetadataToken => 1;
        public virtual bool IsResource() => false;
        /// <summary>
        /// The module's custom attributes: none are modelled (a source program's module carries none of csc's, and
        /// [NullablePublicOnly], the one NullabilityInfoContext reads, is not kept for IL assemblies yet).
        /// </summary>
        public virtual IList<CustomAttributeData> GetCustomAttributesData() => new System.Collections.ObjectModel.ReadOnlyCollection<CustomAttributeData>(new CustomAttributeData[0]);
        public virtual Type[] GetTypes() => Assembly.GetTypes();

        public virtual Type GetType(string className) => GetType(className, false, false);
        public virtual Type GetType(string className, bool ignoreCase) => GetType(className, false, ignoreCase);
        public virtual Type GetType(string className, bool throwOnError, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(className);
            return Assembly.GetType(className, throwOnError, ignoreCase);
        }

        public override string ToString() => ScopeName;
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(ModuleImpl left, ModuleImpl right)
        {
            if ((object)right == null) return (object)left == null;
            return ReferenceEquals(left, right);
        }
        public static bool operator !=(ModuleImpl left, ModuleImpl right) => !(left == right);
    }

    [Shadows("System.Reflection.RuntimeModule")]
    internal sealed class RuntimeModuleImpl : ModuleImpl
    {
        private readonly Assembly _assembly;
        private readonly string _scopeName;
        private readonly Guid _mvid;
        private readonly bool _hasMvid;

        internal RuntimeModuleImpl(Assembly assembly, string scopeName, Guid mvid, bool hasMvid) { _assembly = assembly; _scopeName = scopeName; _mvid = mvid; _hasMvid = hasMvid; }

        public override Guid ModuleVersionId => _hasMvid ? _mvid
            : throw new NotSupportedException("Module.ModuleVersionId of '" + _scopeName + "' is not supported by csharpc yet (only assemblies compiled from IL carry their module id).");
        public override string ScopeName => _scopeName;
        public override string Name => _scopeName;
        public override Assembly Assembly => _assembly;
    }

    /// <summary>What .NET hands out for an embedded resource: a read-only UnmanagedMemoryStream over the image's bytes.</summary>
    [Shadows("System.Reflection.RuntimeAssembly+ManifestResourceStream")]
    internal sealed unsafe class ManifestResourceStreamImpl : System.IO.UnmanagedMemoryStream
    {
        internal ManifestResourceStreamImpl(byte* pointer, long length) : base(pointer, length) { }
    }

    [Shadows("System.Reflection.Assembly")]
    public class AssemblyImpl
    {
        private readonly string _fullName;
        private AssemblyImpl _next;
        private static AssemblyImpl s_first;

        private AssemblyImpl(string fullName) { _fullName = fullName; }

        /// <summary>The one Assembly object for a full name, so == and ReferenceEquals hold as in .NET.</summary>
        internal static Assembly ForName(string fullName)
        {
            AssemblyImpl created = null;
            while (true)
            {
                var head = Volatile.Read(ref s_first);
                for (var a = head; a != null; a = a._next)
                    if (a._fullName == fullName) return (Assembly)(object)a;
                created ??= new AssemblyImpl(fullName);
                created._next = head;
                if (Interlocked.CompareExchange(ref s_first, created, head) == head) return (Assembly)(object)created;
            }
        }

        /// <summary>Assembly.EntryPoint of a program whose entry method csc bridges (async or Task-returning Main).</summary>
        internal static MethodInfo EntryPointUnsupported() =>
            throw new NotSupportedException("Assembly.EntryPoint of an async or Task-returning Main is not supported by csharpc yet (csc's entry point is a method it synthesizes).");

        /// <summary>Type.Module: the assembly's manifest module.</summary>
        internal static Module ModuleOf(Assembly assembly) => assembly.ManifestModule;

        /// <summary>The identity compiled-in attribute lookups dispatch on (FunctionCodeGen.AssemblyAttributes.cs).</summary>
        internal static string NameOf(Assembly assembly) => ((AssemblyImpl)(object)assembly)._fullName;

        /// <summary>NotSupportedException's text for an assembly whose attributes are not compiled in (it names the assembly).</summary>
        internal static string UnavailableAttributesMessage(string assemblyName) =>
            "The custom attributes of the assembly '" + assemblyName + "' are not available to csharpc yet (only those of assemblies compiled from IL are).";

        /// <summary>
        /// Assembly.Load in a closed world: the program's own assemblies and the IL assemblies csharpc compiled
        /// are found by simple name (case-insensitively, as the binder matches); a reference csharpc can't
        /// answer as .NET would (another assembly, which .NET might find on disk; a higher version; a
        /// satellite culture; another public key token) throws NotSupportedException rather than guessing.
        /// </summary>
        public static Assembly Load(AssemblyName assemblyRef)
        {
            ArgumentNullException.ThrowIfNull(assemblyRef);
            string name = assemblyRef.Name;
            if (name != null)
                foreach (string fullName in CompiledBlob.Fields(CoreIntrinsics.LoadableAssemblies()))
                {
                    int comma = fullName.IndexOf(',');
                    string simple = comma < 0 ? fullName : fullName.Substring(0, comma);
                    if (!string.Equals(simple, name, StringComparison.OrdinalIgnoreCase)) continue;
                    AssemblyName found = AssemblyNameImpl.FromFullName(fullName);
                    if (assemblyRef.Version != null && found.Version != null && assemblyRef.Version > found.Version)
                        break;
                    if (!string.IsNullOrEmpty(assemblyRef.CultureName)) break;
                    byte[] token = assemblyRef.GetPublicKeyToken();
                    if (token != null && token.Length > 0 && !SameBytes(token, found.GetPublicKeyToken())) break;
                    return ForName(fullName);
                }
            throw new NotSupportedException("Assembly.Load('" + assemblyRef.FullName + "') is not supported by csharpc yet: only the program's own and its IL assemblies can be loaded.");
        }

        public static Assembly GetAssembly(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.Assembly;
        }

        public static string CreateQualifiedName(string assemblyName, string typeName) => typeName + ", " + assemblyName;

        public static Assembly Load(string assemblyString)
        {
            ArgumentNullException.ThrowIfNull(assemblyString);
            return Load(new AssemblyName(assemblyString));
        }

        // Assemblies are compiled in: a file cannot be loaded at run time (the same explicit stop as NativeAOT's missing dynamic loading).
        public static Assembly LoadFrom(string assemblyFile)
        {
            ArgumentNullException.ThrowIfNull(assemblyFile);
            return NotLoadable(assemblyFile);
        }

        public static Assembly LoadFile(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return NotLoadable(path);
        }

        private static Assembly NotLoadable(string path) =>
            throw new NotSupportedException("Loading the assembly file '" + path + "' at run time is not supported by csharpc: the program's assemblies are compiled in.");

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public virtual string FullName => _fullName;

        /// <summary>A type of this assembly by name: the types the program has (constant names are kept at compile time).</summary>
        public virtual Type GetType(string name) => GetType(name, false, false);
        public virtual Type GetType(string name, bool throwOnError) => GetType(name, throwOnError, false);
        public virtual Type GetType(string name, bool throwOnError, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(name);
            Type found;
            // The types only the assembly's list knows: a compiler-synthesized one (its name has '<': closures, state machines)
            // or a generic type definition by its metadata name (`Registry`1`, no type arguments).
            if (name.IndexOf('<') >= 0 || (name.IndexOf('`') >= 0 && name.IndexOf('[') < 0))
            {
                found = null;
                var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                // An incomplete list (csc's generic synthesized types aren't in it) can only vouch for what it holds.
                var all = TypeList(out var failure);
                foreach (var type in all)
                    if (string.Equals(type.FullName, name, comparison)) { found = type; break; }
                if (found == null && failure != null) throw new NotSupportedException(failure);
            }
            else found = ReflectionRuntime.GetTypeByName(name, false, ignoreCase);
            if (found != null && !ReferenceEquals(found.Assembly, this)) found = null;
            if (found == null && throwOnError)
                throw new TypeLoadException("Could not resolve type '" + name + "' in assembly '" + _fullName + "'.");
            return found;
        }

        // Enumerating an assembly's types (hosting's UseStartup(assemblyName) / startupAssembly setting looks for a
        // Startup class this way): the type lists of the program's own assembly and of the IL assemblies compiled in are
        // known at compile time (ModuleCodeGen.AssemblyTypes.cs), in metadata order; any other assembly (the shared
        // framework's) has none and throws NotSupportedException rather than answer with a partial list.
        public virtual Type[] GetTypes() => (Type[])TypeList().Clone();

        private Type[] TypeList() => TypeList(out var failure) is var list && failure == null ? list : throw new NotSupportedException(failure);

        public virtual IEnumerable<TypeInfo> DefinedTypes
        {
            get
            {
                var types = TypeList();
                var list = new TypeInfo[types.Length];
                for (int i = 0; i < types.Length; i++) list[i] = types[i].GetTypeInfo();
                return list;
            }
        }

        public virtual Type[] GetExportedTypes()
        {
            // The public types are all there even when csc's generic synthesized ones (never public) aren't.
            var types = TypeList(out _);
            var exported = new List<Type>();
            foreach (var type in types) if (type.IsVisible) exported.Add(type);
            return exported.ToArray();
        }

        public virtual IEnumerable<Type> ExportedTypes => GetExportedTypes();

        private Type[] _types;
        private Type[] _partialTypes;
        private string _typeListFailure;

        /// <summary>
        /// The compiled-in type list; <paramref name="failure"/> says why it isn't complete (the list then holds what it can,
        /// right for the public types only when the failure is csc's generic synthesized types) and throws when there is none.
        /// </summary>
        private unsafe Type[] TypeList(out string failure)
        {
            if (_types != null) { failure = null; return _types; }
            if (_partialTypes != null) { failure = _typeListFailure; return _partialTypes; }
            var names = CompiledBlob.Fields(CoreIntrinsics.AssemblyTypeNames());
            for (int i = 0; i + 2 < names.Count; i += 3)
            {
                if (names[i] != _fullName) continue;
                bool partial = names[i + 2].Length > 0;
                if (names[i + 1].Length > 0 && !partial) throw new NotSupportedException(names[i + 1]);
                int pointer = ReflectionRecords.P;
                int stride = (pointer + 8 + pointer - 1) / pointer * pointer;
                byte* entry = (byte*)CoreIntrinsics.AssemblyTypeTableAddress() + (i / 3) * stride;
                nint* methodTables = *(nint**)entry;
                int count = *(int*)(entry + pointer);
                var list = new Type[count];
                for (int k = 0; k < count; k++) list[k] = ReflectionRuntime.TypeOf(methodTables[k]);
                if (!partial) { failure = null; return _types = list; }
                failure = _typeListFailure = names[i + 1];
                return _partialTypes = list;
            }
            throw new NotSupportedException("Assembly.GetTypes() of '" + _fullName + "' is not supported by csharpc yet (only the program's own and its IL assemblies have a type list).");
        }

        private Module _manifestModule;

        /// <summary>
        /// The manifest module: an IL assembly's own (its metadata name and MVID), else the file name .NET loads the assembly from,
        /// "&lt;name&gt;.dll" (the program's own assembly and the shared framework's), without a module id; one object per assembly.
        /// </summary>
        public virtual Module ManifestModule
        {
            get
            {
                if ((object)_manifestModule != null) return _manifestModule;
                var ids = CompiledBlob.Fields(CoreIntrinsics.ModuleVersionIds());
                Module module = null;
                for (int i = 0; i + 2 < ids.Count; i += 3)
                    if (ids[i] == _fullName)
                    {
                        module = (Module)(object)new RuntimeModuleImpl((Assembly)(object)this, ids[i + 1], Guid.Parse(ids[i + 2]), true);
                        break;
                    }
                if ((object)module == null)
                {
                    int comma = _fullName.IndexOf(',');
                    string simple = comma < 0 ? _fullName : _fullName.Substring(0, comma);
                    module = (Module)(object)new RuntimeModuleImpl((Assembly)(object)this, simple + ".dll", default, false);
                }
                Interlocked.CompareExchange(ref _manifestModule, module, null);
                return _manifestModule;
            }
        }

        public virtual Module[] GetModules() => GetModules(false);
        public virtual Module[] GetModules(bool getResourceModules) => new Module[] { ManifestModule };
        public virtual Module[] GetLoadedModules() => GetLoadedModules(false);
        public virtual Module[] GetLoadedModules(bool getResourceModules) => new Module[] { ManifestModule };
        public virtual IEnumerable<Module> Modules => GetLoadedModules(true);

        public virtual Module GetModule(string name)
        {
            if (name == null) throw new ArgumentNullException(null, "File name cannot be null.");
            if (name.Length == 0) throw new ArgumentException("Empty file name is not legal.");
            Module module = ManifestModule;
            return string.Equals(module.ScopeName, name, StringComparison.OrdinalIgnoreCase) ? module : null;
        }

        /// <summary>The assemblies this one references, from its metadata (a new array of new names per call, as in .NET).</summary>
        public virtual AssemblyName[] GetReferencedAssemblies()
        {
            var fields = CompiledBlob.Fields(CoreIntrinsics.AssemblyReferences());
            for (int i = 0; i + 1 < fields.Count; i += 2 + int.Parse(fields[i + 1]))
            {
                if (fields[i] != _fullName) continue;
                int count = int.Parse(fields[i + 1]);
                var names = new AssemblyName[count];
                for (int k = 0; k < count; k++) names[k] = AssemblyNameImpl.FromFullName(fields[i + 2 + k]);
                return names;
            }
            throw new NotSupportedException("Assembly.GetReferencedAssemblies of '" + _fullName + "' is not supported by csharpc yet (only the program's own assembly and assemblies compiled from IL have their references).");
        }

        // ---- manifest resources (compiled in: ModuleCodeGen.Resources.cs) ----

        /// <summary>An embedded resource of this assembly as a read-only stream over the compiled-in bytes; null if it has none of that name.</summary>
        public virtual unsafe System.IO.Stream GetManifestResourceStream(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            var index = CompiledBlob.Fields(CoreIntrinsics.ManifestResourceIndex());
            for (int i = 0; i + 3 < index.Count; i += 4)
                if (index[i] == _fullName && index[i + 1] == name)
                    return new ManifestResourceStreamImpl(CoreIntrinsics.ManifestResourceData() + long.Parse(index[i + 2]), long.Parse(index[i + 3]));
            return null;
        }

        public virtual System.IO.Stream GetManifestResourceStream(Type type, string name)
        {
            if (type == null && name == null) throw new ArgumentNullException("type");
            string nameSpace = type?.Namespace;
            string resourceName = nameSpace != null && name != null ? nameSpace + "." + name : string.Concat(nameSpace, name);
            return GetManifestResourceStream(resourceName);
        }

        public virtual string[] GetManifestResourceNames()
        {
            var index = CompiledBlob.Fields(CoreIntrinsics.ManifestResourceIndex());
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i + 3 < index.Count; i += 4)
                if (index[i] == _fullName) names.Add(index[i + 1]);
            return names.ToArray();
        }

        /// <summary>NativeAOT's answer: assemblies are compiled into the executable, so they have no file.</summary>
        public virtual string Location => "";
        public virtual bool IsDynamic => false;
        /// <summary>.NET's answers for any assembly: code access security is gone (always true / None), the image runtime version is CoreCLR's.</summary>
        public virtual bool IsFullyTrusted => true;
        /// <summary>The global assembly cache is gone: .NET reports false for every assembly.</summary>
        public virtual bool GlobalAssemblyCache => false;
        public virtual string ImageRuntimeVersion => "v4.0.30319";
        public virtual bool ReflectionOnly => false;
        public virtual long HostContext => 0;
        public virtual System.Security.SecurityRuleSet SecurityRuleSet => System.Security.SecurityRuleSet.None;
        /// <summary>A csharpc program's assemblies are compiled in: none can be unloaded.</summary>
        public virtual bool IsCollectible => false;
        public virtual AssemblyName GetName() => AssemblyNameImpl.ForLoadedAssembly(_fullName);
        public override string ToString() => _fullName;
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(AssemblyImpl left, AssemblyImpl right) => ReferenceEquals(left, right);
        public static bool operator !=(AssemblyImpl left, AssemblyImpl right) => !ReferenceEquals(left, right);
    }

    /// <summary>System.Version (dotnet/runtime's semantics; parsing is not implemented yet).</summary>
    [Shadows("System.Version")]
    public sealed class VersionImpl : IComparable, IComparable<Version>, IEquatable<Version>
    {
        private readonly int _Major;
        private readonly int _Minor;
        private readonly int _Build;
        private readonly int _Revision;

        public VersionImpl(int major, int minor, int build, int revision)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(major, nameof(major));
            ArgumentOutOfRangeException.ThrowIfNegative(minor, nameof(minor));
            ArgumentOutOfRangeException.ThrowIfNegative(build, nameof(build));
            ArgumentOutOfRangeException.ThrowIfNegative(revision, nameof(revision));
            _Major = major; _Minor = minor; _Build = build; _Revision = revision;
        }

        public VersionImpl(int major, int minor, int build)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(major, nameof(major));
            ArgumentOutOfRangeException.ThrowIfNegative(minor, nameof(minor));
            ArgumentOutOfRangeException.ThrowIfNegative(build, nameof(build));
            _Major = major; _Minor = minor; _Build = build; _Revision = -1;
        }

        public VersionImpl(int major, int minor)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(major, nameof(major));
            ArgumentOutOfRangeException.ThrowIfNegative(minor, nameof(minor));
            _Major = major; _Minor = minor; _Build = -1; _Revision = -1;
        }

        public VersionImpl() { _Build = -1; _Revision = -1; }

        // .NET: Version.Parse(version) (its exceptions), then its four components.
        public VersionImpl(string version)
        {
            VersionImpl v = Of(Parse(version));
            _Major = v._Major; _Minor = v._Minor; _Build = v._Build; _Revision = v._Revision;
        }

        private static VersionImpl Of(Version v) => (VersionImpl)(object)v;

        public int Major => _Major;
        public int Minor => _Minor;
        public int Build => _Build;
        public int Revision => _Revision;
        public short MajorRevision => (short)(_Revision >> 16);
        public short MinorRevision => (short)(_Revision & 0xFFFF);

        private VersionImpl(VersionImpl version)
        {
            _Major = version._Major; _Minor = version._Minor; _Build = version._Build; _Revision = version._Revision;
        }

        public object Clone() => new VersionImpl(this);

        // Parse / TryParse: dotnet/runtime's ParseVersion and TryParseComponent (Version.cs), for char input.
        public static Version Parse(string input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return ParseVersion(input.AsSpan(), throwOnFailure: true)!;
        }

        public static Version Parse(ReadOnlySpan<char> input) => ParseVersion(input, throwOnFailure: true)!;

        public static bool TryParse(string input, out Version result)
        {
            if (input == null) { result = null; return false; }
            result = ParseVersion(input.AsSpan(), throwOnFailure: false);
            return result is not null;
        }

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Version result) =>
            TryParse(System.Text.Encoding.UTF8.GetString(utf8Text).AsSpan(), out result);

        public static bool TryParse(ReadOnlySpan<char> input, out Version result)
        {
            result = ParseVersion(input, throwOnFailure: false);
            return result is not null;
        }

        private static Version ParseVersion(ReadOnlySpan<char> input, bool throwOnFailure)
        {
            int majorEnd = input.IndexOf('.');
            if (majorEnd < 0)
            {
                if (throwOnFailure) throw new ArgumentException("Version string portion was too short or too long.", nameof(input));
                return null;
            }
            int buildEnd = -1;
            int minorEnd = input.Slice(majorEnd + 1).IndexOf('.');
            if (minorEnd >= 0)
            {
                minorEnd += majorEnd + 1;
                buildEnd = input.Slice(minorEnd + 1).IndexOf('.');
                if (buildEnd >= 0)
                {
                    buildEnd += minorEnd + 1;
                    if (input.Slice(buildEnd + 1).IndexOf('.') >= 0)
                    {
                        if (throwOnFailure) throw new ArgumentException("Version string portion was too short or too long.", nameof(input));
                        return null;
                    }
                }
            }
            if (!TryParseComponent(input.Slice(0, majorEnd), nameof(input), throwOnFailure, input, out int major)) return null;
            if (minorEnd != -1)
            {
                if (!TryParseComponent(input.Slice(majorEnd + 1, minorEnd - majorEnd - 1), nameof(input), throwOnFailure, input, out int minor)) return null;
                if (buildEnd != -1)
                {
                    return TryParseComponent(input.Slice(minorEnd + 1, buildEnd - minorEnd - 1), "build", throwOnFailure, input, out int build)
                        && TryParseComponent(input.Slice(buildEnd + 1), "revision", throwOnFailure, input, out int revision)
                        ? (Version)(object)new VersionImpl(major, minor, build, revision) : null;
                }
                return TryParseComponent(input.Slice(minorEnd + 1), "build", throwOnFailure, input, out int build3)
                    ? (Version)(object)new VersionImpl(major, minor, build3) : null;
            }
            return TryParseComponent(input.Slice(majorEnd + 1), nameof(input), throwOnFailure, input, out int minor2)
                ? (Version)(object)new VersionImpl(major, minor2) : null;
        }

        private static bool TryParseComponent(ReadOnlySpan<char> component, string componentName, bool throwOnFailure, ReadOnlySpan<char> originalInput, out int parsedComponent)
        {
            bool ok = int.TryParse(component, System.Globalization.NumberStyles.Integer, System.Globalization.NumberFormatInfo.InvariantInfo, out parsedComponent);
            if (ok && parsedComponent >= 0) return true;
            if (throwOnFailure)
            {
                if (ok) throw new ArgumentOutOfRangeException(componentName, parsedComponent, componentName + " ('" + parsedComponent + "') must be a non-negative value.");
                // Number.ParsingStatus.Overflow: valid integer syntax that doesn't fit an int.
                if (IsIntegerSyntax(component)) throw new OverflowException("Value was either too large or too small for an Int32.");
                throw new FormatException("The input string '" + originalInput.ToString() + "' was not in a correct format.");
            }
            parsedComponent = 0;
            return false;
        }

        // NumberStyles.Integer: optional white space, an optional sign, digits, optional white space.
        private static bool IsIntegerSyntax(ReadOnlySpan<char> s)
        {
            s = s.Trim();
            if (s.Length > 0 && (s[0] == '-' || s[0] == '+')) s = s.Slice(1);
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        public int CompareTo(object version)
        {
            if (version == null) return 1;
            if (version is Version v) return CompareTo(v);
            throw new ArgumentException("Object must be of type Version.", nameof(version));
        }

        public int CompareTo(Version value)
        {
            if (value is null) return 1;
            var other = Of(value);
            if (ReferenceEquals(other, this)) return 0;
            return _Major != other._Major ? (_Major > other._Major ? 1 : -1) :
                _Minor != other._Minor ? (_Minor > other._Minor ? 1 : -1) :
                _Build != other._Build ? (_Build > other._Build ? 1 : -1) :
                _Revision != other._Revision ? (_Revision > other._Revision ? 1 : -1) :
                0;
        }

        public override bool Equals(object obj) => Equals(obj as Version);

        public bool Equals(Version obj)
        {
            var other = Of(obj);
            return ReferenceEquals(other, this) || (other is not null && _Major == other._Major && _Minor == other._Minor
                && _Build == other._Build && _Revision == other._Revision);
        }

        public override int GetHashCode()
        {
            int accumulator = 0;
            accumulator |= (_Major & 0x0000000F) << 28;
            accumulator |= (_Minor & 0x000000FF) << 20;
            accumulator |= (_Build & 0x000000FF) << 12;
            accumulator |= (_Revision & 0x00000FFF);
            return accumulator;
        }

        private int DefaultFormatFieldCount => _Build == -1 ? 2 : _Revision == -1 ? 3 : 4;

        public override string ToString() => ToString(DefaultFormatFieldCount);

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten) =>
            NumberTryFormat.CopyUtf8(ToString(), utf8Destination, out bytesWritten);

        public string ToString(int fieldCount)
        {
            switch (fieldCount)
            {
                case 0: return "";
                case 1: return _Major.ToString();
                case 2: return _Major.ToString() + "." + _Minor.ToString();
                default:
                    if ((uint)fieldCount > 4)
                        throw new ArgumentException("Argument must be between 0 and 4.", nameof(fieldCount));
                    if (_Build == -1)
                        throw new ArgumentException("Argument must be between 0 and 2.", nameof(fieldCount));
                    if (fieldCount == 3)
                        return _Major.ToString() + "." + _Minor.ToString() + "." + _Build.ToString();
                    if (_Revision == -1)
                        throw new ArgumentException("Argument must be between 0 and 3.", nameof(fieldCount));
                    return _Major.ToString() + "." + _Minor.ToString() + "." + _Build.ToString() + "." + _Revision.ToString();
            }
        }

        public static bool operator ==(VersionImpl v1, VersionImpl v2) => v2 is null ? v1 is null : ReferenceEquals(v2, v1) || v2.Equals((Version)(object)v1);
        public static bool operator !=(VersionImpl v1, VersionImpl v2) => !(v1 == v2);

        public static bool operator <(VersionImpl v1, VersionImpl v2)
        {
            if (v1 is null) return v2 is not null;
            return v1.CompareTo((Version)(object)v2) < 0;
        }

        public static bool operator <=(VersionImpl v1, VersionImpl v2)
        {
            if (v1 is null) return true;
            return v1.CompareTo((Version)(object)v2) <= 0;
        }

        public static bool operator >(VersionImpl v1, VersionImpl v2) => v2 < v1;
        public static bool operator >=(VersionImpl v1, VersionImpl v2) => v2 <= v1;
    }
}
