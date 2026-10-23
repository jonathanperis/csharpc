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
    internal static class AppContextImpl
    {
        private static Dictionary<string, object> s_data;
        private static Dictionary<string, bool> s_switches;

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

        public static object GetData(string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            var data = Data;
            lock (data) return data.TryGetValue(name, out var value) ? value : null;
        }

        public static void SetData(string name, object data)
        {
            if (name == null) throw new ArgumentNullException("name");
            var store = Data;
            lock (store) store[name] = data;
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
            var switches = Switches;
            lock (switches) switches[switchName] = isEnabled;
        }

        private static void ThrowIfNullOrEmpty(string value, string paramName)
        {
            if (value == null) throw new ArgumentNullException(paramName);
            if (value.Length == 0) throw new ArgumentException("The value cannot be an empty string.", paramName);
        }
    }
}
