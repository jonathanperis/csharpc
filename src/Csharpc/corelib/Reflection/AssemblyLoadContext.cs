// System.Runtime.Loader.AssemblyLoadContext as NativeAOT has it (the shared AssemblyLoadContext.cs with
// AssemblyLoadContext.NativeAot.cs): contexts can be created, named, enumerated and unloaded, and every
// assembly belongs to the default one, but nothing can be loaded at run time — LoadFromAssemblyPath
// throws PlatformNotSupportedException. Unloading never frees a context: NativeAOT's runtime keeps the
// strong handle InitiateUnload creates, so the context stays alive.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.Loader.AssemblyLoadContext")]
    public class AssemblyLoadContextImpl
    {
        private static readonly object s_lock = new object();
        private static Dictionary<long, WeakReference<AssemblyLoadContext>> s_allContexts;
        // GCHandle.Normal of non-collectible contexts, and of unloaded ones (NativeAOT never releases it).
        private static List<AssemblyLoadContextImpl> s_strong;
        private static long s_nextId;
        // Created with the type, as the runtime creates it at startup: it is context #0, listed first by All.
        private static readonly AssemblyLoadContext s_default = CoreIntrinsics.ReinterpretAs<AssemblyLoadContext>(new AssemblyLoadContextImpl(false, "Default"));

        private readonly object _unloadLock;
        private readonly string _name;
        private readonly long _id;
        private readonly bool _isCollectible;
        private bool _unloading;
        private Action<AssemblyLoadContext> _unloadingHandlers;

        protected AssemblyLoadContextImpl() : this(false, null) { }
        protected AssemblyLoadContextImpl(bool isCollectible) : this(isCollectible, null) { }
        public AssemblyLoadContextImpl(string name, bool isCollectible = false) : this(isCollectible, name) { }

        private AssemblyLoadContextImpl(bool isCollectible, string name)
        {
            _isCollectible = isCollectible;
            _name = name;
            _unloadLock = new object();
            if (!isCollectible) GC.SuppressFinalize(this);
            lock (s_lock)
            {
                if (!isCollectible) (s_strong ??= new List<AssemblyLoadContextImpl>()).Add(this);
                _id = s_nextId++;
                (s_allContexts ??= new Dictionary<long, WeakReference<AssemblyLoadContext>>()).Add(_id, new WeakReference<AssemblyLoadContext>(Self, true));
            }
        }

        ~AssemblyLoadContextImpl()
        {
            if (_unloadLock != null) InitiateUnload();
        }

        private AssemblyLoadContext Self => CoreIntrinsics.ReinterpretAs<AssemblyLoadContext>(this);

        public static AssemblyLoadContext Default => s_default;

        public static AssemblyLoadContext CurrentContextualReflectionContext => null;

        public static IEnumerable<AssemblyLoadContext> All
        {
            get
            {
                var contexts = new List<AssemblyLoadContext>();
                lock (s_lock)
                {
                    foreach (var entry in s_allContexts)
                        if (entry.Value.TryGetTarget(out var context)) contexts.Add(context);
                }
                return contexts;
            }
        }

        public bool IsCollectible => _isCollectible;
        public string Name => _name;
        public override string ToString() => "\"" + Name + "\" " + GetType() + " #" + _id;

        public IEnumerable<Assembly> Assemblies
        {
            get
            {
                if (ReferenceEquals(this, Default))
                    throw new NotSupportedException("AssemblyLoadContext.Default.Assemblies is not supported by csharpc yet.");
                return new Assembly[0]; // every assembly is in the default context
            }
        }

        public event Func<AssemblyLoadContext, AssemblyName, Assembly> Resolving;
        public event Func<Assembly, string, IntPtr> ResolvingUnmanagedDll;
        public event Action<AssemblyLoadContext> Unloading
        {
            add { lock (s_lock) _unloadingHandlers += value; }
            remove { lock (s_lock) _unloadingHandlers -= value; }
        }

        public static AssemblyLoadContext GetLoadContext(Assembly assembly) => Default;

        protected virtual Assembly Load(AssemblyName assemblyName) => null;
        protected virtual IntPtr LoadUnmanagedDll(string unmanagedDllName) => IntPtr.Zero;

        public Assembly LoadFromAssemblyName(AssemblyName assemblyName) => Assembly.Load(assemblyName);

        public Assembly LoadFromAssemblyPath(string assemblyPath)
        {
            ArgumentNullException.ThrowIfNull(assemblyPath);
            if (!Path.IsPathFullyQualified(assemblyPath))
                throw new ArgumentException("Path \"" + assemblyPath + "\" is not an absolute path.", "assemblyPath");
            lock (_unloadLock)
            {
                VerifyIsAlive();
                throw new PlatformNotSupportedException();
            }
        }

        public Assembly LoadFromStream(Stream assembly) => LoadFromStream(assembly, null);
        public Assembly LoadFromStream(Stream assembly, Stream assemblySymbols)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            throw new NotSupportedException("AssemblyLoadContext.LoadFromStream is not supported by csharpc yet.");
        }

        public void SetProfileOptimizationRoot(string directoryPath) { }
        public void StartProfileOptimization(string profile) { }

        public void Unload()
        {
            if (!IsCollectible) throw new InvalidOperationException("Cannot unload non-collectible AssemblyLoadContext.");
            GC.SuppressFinalize(this);
            InitiateUnload();
        }

        private void InitiateUnload()
        {
            Action<AssemblyLoadContext> handlers;
            lock (s_lock)
            {
                handlers = _unloadingHandlers;
                _unloadingHandlers = null;
            }
            handlers?.Invoke(Self);
            bool wasAlive;
            lock (_unloadLock)
            {
                wasAlive = !_unloading;
                _unloading = true;
            }
            if (wasAlive)
                lock (s_lock)
                {
                    (s_strong ??= new List<AssemblyLoadContextImpl>()).Add(this);
                    s_allContexts.Remove(_id);
                }
        }

        private void VerifyIsAlive()
        {
            if (_unloading) throw new InvalidOperationException("AssemblyLoadContext is unloading or was already unloaded.");
        }
    }

    /// <summary>
    /// NativeAOT has no hostpolicy library to resolve a component's dependencies with: the constructor fails
    /// as it does there (the DllNotFoundException turned into InvalidOperationException).
    /// </summary>
    [Shadows("System.Runtime.Loader.AssemblyDependencyResolver")]
    public sealed class AssemblyDependencyResolverImpl
    {
        public AssemblyDependencyResolverImpl(string componentAssemblyPath)
        {
            ArgumentNullException.ThrowIfNull(componentAssemblyPath);
            throw new InvalidOperationException("Cannot load hostpolicy library. AssemblyDependencyResolver is currently only supported if the runtime is hosted through hostpolicy library.",
                new DllNotFoundException("Unable to load shared library 'libhostpolicy' or one of its dependencies."));
        }

        public string ResolveAssemblyToPath(AssemblyName assemblyName) => throw new InvalidOperationException();
        public string ResolveUnmanagedDllToPath(string unmanagedDllName) => throw new InvalidOperationException();
    }
}
