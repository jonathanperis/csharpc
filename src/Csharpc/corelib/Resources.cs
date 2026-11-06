// ResourceManager and AppContext over data compiled into the program (ModuleCodeGen.Resources.cs).
//
// Library exception messages come from string resources embedded in their assemblies (SR.* in the
// framework, Properties.Resources in packages); csharpc reads every IL assembly's .resources at compile
// time, so `new ResourceManager(typeof(X)).GetString(key)` returns what .NET returns for the neutral
// culture (csharpc is invariant-culture; satellite assemblies are not used).
//
// AppContext starts with runtimeconfig.json's configProperties — the RuntimeHostConfigurationOption
// items of the project, or a file-based app's defaults for single files — as strings, like the host
// passes them. Host-supplied properties (APP_CONTEXT_BASE_DIRECTORY, TRUSTED_PLATFORM_ASSEMBLIES, ...)
// don't exist in a native executable and read as null.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace CsharpcCoreLib
{
    internal static class CompiledBlob
    {
        /// <summary>Decodes [length high][length low][chars] fields.</summary>
        public static List<string> Fields(string blob)
        {
            var fields = new List<string>();
            var i = 0;
            while (i < blob.Length)
            {
                var length = (blob[i] << 16) | blob[i + 1];
                fields.Add(blob.Substring(i + 2, length));
                i += 2 + length;
            }
            return fields;
        }
    }

    internal static class ResourceTable
    {
        private static Dictionary<string, Dictionary<string, string>> s_sets;

        /// <summary>The string resources of `baseName` (e.g. "FxResources.System.Collections.Immutable.SR"), or null.</summary>
        public static Dictionary<string, string> Find(string baseName)
        {
            var sets = s_sets;
            if (sets == null)
            {
                sets = new Dictionary<string, Dictionary<string, string>>();
                var fields = CompiledBlob.Fields(CoreIntrinsics.ResourceStrings());
                for (var i = 0; i + 2 < fields.Count; i += 3)
                {
                    if (!sets.TryGetValue(fields[i], out var set)) sets[fields[i]] = set = new Dictionary<string, string>();
                    set[fields[i + 1]] = fields[i + 2];
                }
                sets = Interlocked.CompareExchange(ref s_sets, sets, null) ?? sets;
            }
            return sets.TryGetValue(baseName, out var found) ? found : null;
        }
    }

    [Shadows("System.Resources.ResourceManager")]
    public class ResourceManagerImpl
    {
        private readonly string _baseName;
        private Dictionary<string, string> _strings;

        public ResourceManagerImpl(Type resourceSource)
        {
            if (resourceSource == null) throw new ArgumentNullException("resourceSource");
            _baseName = resourceSource.FullName;
        }

        public ResourceManagerImpl(string baseName, Assembly assembly)
        {
            if (baseName == null) throw new ArgumentNullException("baseName");
            if (assembly == null) throw new ArgumentNullException("assembly");
            _baseName = baseName;
        }

        public virtual string BaseName => _baseName;

        public virtual bool IgnoreCase { get; set; }

        public virtual string GetString(string name) => GetString(name, null);

        public virtual string GetString(string name, CultureInfo culture)
        {
            string value = Lookup(name);
            if (value == null) return null;
            if (value.StartsWith(NonStringPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Resource was of type '" + NonStringTypeName(value) + "' instead of String - call GetObject instead.");
            return value;
        }

        public virtual object GetObject(string name) => GetObject(name, null);

        public virtual object GetObject(string name, CultureInfo culture)
        {
            string value = Lookup(name);
            if (value == null || !value.StartsWith(NonStringPrefix, StringComparison.Ordinal)) return value;
            string type = NonStringTypeName(value);
            string text = value.Substring(value.IndexOf('\u0001', NonStringPrefix.Length) + 1);
            string stored = StorageFormat(value);
            if (stored == null)
            {
                try { return Create(name, type, text); }
                catch (FormatException) { }
                catch (OverflowException) { }
            }
            else if (stored == ConverterString)
            {
                // A value DeserializingResourceReader creates with TypeDescriptor.GetConverter(type).ConvertFromInvariantString(text).
                // A converter's own exceptions (bad text) propagate, as they do from .NET; a null result is a NullReferenceException there.
                object created = ConvertPrimitive(type, text);
                if (created != null) return created;
                // The other types: the front end generated the conversions of the program's own resources
                // (ProgramHooks.ResourceObject, Frontend/ResourceConverters.cs).
                object[] converted = CoreIntrinsics.ResourceObjectHook(type, text);
                if (converted != null) return converted[0] ?? throw new NullReferenceException();
            }
            throw new NotSupportedException("csharpc: the resource '" + name + "' of '" + _baseName + "' is a " + type
                + (stored != null && stored != ConverterString ? " stored " + StoredHow(stored) : "")
                + ", which ResourceManager.GetObject does not create yet (only strings, numbers, bools, chars, byte arrays and types whose TypeConverter csharpc can create from the string in the .resx, from the types the project's .resx files name).");
        }

        // A preserialized value that is the type converter's string ("\u0002" + this after the type name).
        private const string ConverterString = "3";

        private static string StoredHow(string format)
        {
            switch (format)
            {
                case "1": return "as BinaryFormatter data";
                case "2": return "as the byte array of its TypeConverter";
                case "4": return "as a stream for its constructor";
                default: return "in an unknown format";
            }
        }

        /// <summary>The primitives' converters (TypeDescriptor.GetConverter: trimming, hex prefixes, culture-free), or null for any other type.</summary>
        private static object ConvertPrimitive(string type, string text)
        {
            switch (type)
            {
                case "System.Boolean": return TypeDescriptor.GetConverter(typeof(bool)).ConvertFromInvariantString(text);
                case "System.Char": return TypeDescriptor.GetConverter(typeof(char)).ConvertFromInvariantString(text);
                case "System.Byte": return TypeDescriptor.GetConverter(typeof(byte)).ConvertFromInvariantString(text);
                case "System.SByte": return TypeDescriptor.GetConverter(typeof(sbyte)).ConvertFromInvariantString(text);
                case "System.Int16": return TypeDescriptor.GetConverter(typeof(short)).ConvertFromInvariantString(text);
                case "System.UInt16": return TypeDescriptor.GetConverter(typeof(ushort)).ConvertFromInvariantString(text);
                case "System.Int32": return TypeDescriptor.GetConverter(typeof(int)).ConvertFromInvariantString(text);
                case "System.UInt32": return TypeDescriptor.GetConverter(typeof(uint)).ConvertFromInvariantString(text);
                case "System.Int64": return TypeDescriptor.GetConverter(typeof(long)).ConvertFromInvariantString(text);
                case "System.UInt64": return TypeDescriptor.GetConverter(typeof(ulong)).ConvertFromInvariantString(text);
                case "System.Single": return TypeDescriptor.GetConverter(typeof(float)).ConvertFromInvariantString(text);
                case "System.Double": return TypeDescriptor.GetConverter(typeof(double)).ConvertFromInvariantString(text);
                case "System.Decimal": return TypeDescriptor.GetConverter(typeof(decimal)).ConvertFromInvariantString(text);
                case "System.String": return text;
                case "System.TimeSpan": return TypeDescriptor.GetConverter(typeof(TimeSpan)).ConvertFromInvariantString(text);
                case "System.Guid": return TypeDescriptor.GetConverter(typeof(Guid)).ConvertFromInvariantString(text);
                case "System.Version": return TypeDescriptor.GetConverter(typeof(Version)).ConvertFromInvariantString(text);
            }
            return null;
        }

        private static object Create(string name, string type, string text)
        {
            var invariant = CultureInfo.InvariantCulture;
            switch (type)
            {
                case "null": return null;
                case "System.Boolean": return text == "True";
                case "System.Char": return (char)ushort.Parse(text, invariant);
                case "System.Byte": return byte.Parse(text, invariant);
                case "System.SByte": return sbyte.Parse(text, invariant);
                case "System.Int16": return short.Parse(text, invariant);
                case "System.UInt16": return ushort.Parse(text, invariant);
                case "System.Int32": return int.Parse(text, invariant);
                case "System.UInt32": return uint.Parse(text, invariant);
                case "System.Int64": return long.Parse(text, invariant);
                case "System.UInt64": return ulong.Parse(text, invariant);
                case "System.Single": return float.Parse(text, invariant);
                case "System.Double": return double.Parse(text, invariant);
                case "System.Decimal": return decimal.Parse(text, invariant);
                case "System.Byte[]": return Convert.FromBase64String(text);
                case "System.DateTime": return new DateTime(long.Parse(text, invariant));
                case "System.TimeSpan": return new TimeSpan(long.Parse(text, invariant));
            }
            throw new FormatException();
        }

        // "\uFFFF\u0001" + the type's full name + "\u0001" + the value (IlAssembly.StringResources writes it).
        private const string NonStringPrefix = "\uFFFF\u0001";

        private static string NonStringTypeName(string value)
        {
            int end = value.IndexOf('\u0001', NonStringPrefix.Length);
            int marker = value.IndexOf('\u0002', NonStringPrefix.Length, end - NonStringPrefix.Length);
            return value.Substring(NonStringPrefix.Length, (marker < 0 ? end : marker) - NonStringPrefix.Length);
        }

        // The SerializationFormat of a preserialized value ("\u0002<format>" after the type name), or null for a primitive stored by type code.
        private static string StorageFormat(string value)
        {
            int end = value.IndexOf('\u0001', NonStringPrefix.Length);
            int marker = value.IndexOf('\u0002', NonStringPrefix.Length, end - NonStringPrefix.Length);
            return marker < 0 ? null : value.Substring(marker + 1, end - marker - 1);
        }

        private string Lookup(string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            var strings = _strings ??= ResourceTable.Find(_baseName)
                ?? throw new NotSupportedException("csharpc: no embedded resources named '" + _baseName + ".resources' in the assemblies compiled from IL; this resource lookup is not supported by csharpc yet.");
            if (strings.TryGetValue(name, out var value)) return value;
            if (IgnoreCase)
                foreach (var entry in strings)
                    if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            return null;
        }

        public virtual void ReleaseAllResources() { }
    }

    [ShadowMembers("System.AppContext")]
    internal static unsafe class AppContextImpl
    {
        private static Dictionary<string, object> s_data;
        private static Dictionary<string, bool> s_switches;

        // Set once the program changes AppContext (SetSwitch/SetData): from then on, the library's own
        // switch lookups (TryGetConfigSwitch) go through the dictionaries. Reaching them only through this
        // pointer keeps them, and bool.TryParse, out of programs that never change AppContext.
        private static delegate*<string, out bool, bool> s_modifiedLookup;

        private static Dictionary<string, object> Data
        {
            get
            {
                var data = s_data;
                if (data != null) return data;
                data = new Dictionary<string, object>();
                var fields = CompiledBlob.Fields(CoreIntrinsics.RuntimeConfig());
                for (var i = 0; i + 1 < fields.Count; i += 2) data[fields[i]] = fields[i + 1];
                return Interlocked.CompareExchange(ref s_data, data, null) ?? data;
            }
        }

        private static Dictionary<string, bool> Switches
        {
            get
            {
                var switches = s_switches;
                if (switches != null) return switches;
                switches = new Dictionary<string, bool>();
                return Interlocked.CompareExchange(ref s_switches, switches, null) ?? switches;
            }
        }

        private static string s_baseDirectory;

        /// <summary>
        /// NativeAOT's answer: the directory of the executable, with a trailing separator (the JIT's is the
        /// directory of the app's .dll, which is the same directory for an apphost).
        /// </summary>
        public static string BaseDirectory
        {
            get
            {
                if (s_baseDirectory != null) return s_baseDirectory;
                string exe = Environment.ProcessPath;
                string dir = exe == null ? null : System.IO.Path.GetDirectoryName(exe);
                return s_baseDirectory = dir == null ? "" : dir.EndsWith(System.IO.Path.DirectorySeparatorChar) ? dir : dir + System.IO.Path.DirectorySeparatorChar;
            }
        }

        public static object GetData(string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            var data = Data;
            lock (data) return data.TryGetValue(name, out var value) ? value : null;
        }

        public static void SetData(string name, object data)
        {
            if (name == null) throw new ArgumentNullException("name");
            if (data is string text && bool.TryParse(text, out var on)) RejectResourceKeys(name, on);  // TryGetSwitch reads it
            var store = Data;
            lock (store) store[name] = data;
            s_modifiedLookup = &TryGetSwitch;
        }

        /// <summary>
        /// AppContext.TryGetSwitch for switches the library reads itself (GlobalizationMode). Until the program
        /// calls SetSwitch or SetData, the switches are the compiled runtimeconfig's configProperties, whose
        /// boolean values are exactly "true" / "false" (ProjectLoader normalizes them with bool.TryParse).
        /// </summary>
        internal static bool TryGetConfigSwitch(string switchName, out bool isEnabled)
        {
            var modified = s_modifiedLookup;
            if (modified != null) return modified(switchName, out isEnabled);
            string blob = CoreIntrinsics.RuntimeConfig();
            string value = null;
            int i = 0;
            while (i < blob.Length)
            {
                int keyLength = (blob[i] << 16) | blob[i + 1];
                int keyStart = i + 2;
                i = keyStart + keyLength;
                int valueLength = (blob[i] << 16) | blob[i + 1];
                // the last value of a key wins, as in the Data dictionary
                if (keyLength == switchName.Length && StringOps.EqualsOrdinal(blob.AsSpan(keyStart, keyLength), switchName.AsSpan()))
                    value = blob.Substring(i + 2, valueLength);
                i += 2 + valueLength;
            }
            isEnabled = value == "true";
            return value == "true" || value == "false";
        }

        public static bool TryGetSwitch(string switchName, out bool isEnabled)
        {
            ThrowIfNullOrEmpty(switchName, "switchName");
            var switches = Switches;
            lock (switches)
                if (switches.TryGetValue(switchName, out isEnabled)) return true;
            if (GetData(switchName) is string value && bool.TryParse(value, out isEnabled)) return true;
            isEnabled = false;
            return false;
        }

        public static void SetSwitch(string switchName, bool isEnabled)
        {
            ThrowIfNullOrEmpty(switchName, "switchName");
            RejectResourceKeys(switchName, isEnabled);
            var switches = Switches;
            lock (switches) switches[switchName] = isEnabled;
            s_modifiedLookup = &TryGetSwitch;
        }

        /// <summary>
        /// .NET's libraries read System.Resources.UseSystemResourceKeys when they first format a message, so a
        /// program can turn it on at run time; their messages are then resource keys. csharpc's corelib writes the
        /// full messages (and the compiler refuses the setting in runtimeconfig), so turning it on fails explicitly
        /// instead of every later message differing from .NET's.
        /// </summary>
        private static void RejectResourceKeys(string switchName, bool isEnabled)
        {
            if (isEnabled && switchName == "System.Resources.UseSystemResourceKeys")
                throw new PlatformNotSupportedException("System.Resources.UseSystemResourceKeys is not supported by csharpc yet: .NET then gives its exception messages as resource keys, csharpc's library writes the full messages.");
        }

        private static void ThrowIfNullOrEmpty(string value, string paramName)
        {
            if (value == null) throw new ArgumentNullException(paramName);
            if (value.Length == 0) throw new ArgumentException("The value cannot be an empty string.", paramName);
        }
    }
}
