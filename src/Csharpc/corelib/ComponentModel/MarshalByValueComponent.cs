// System.ComponentModel.MarshalByValueComponent: dotnet/runtime's implementation (System.ComponentModel.TypeConverter),
// the base class of System.Data's DataTable, DataColumn, DataSet and DataView. csharpc serves it from the corelib
// because the rest of TypeConverter.dll stays out of the closed world (TypeConverter.cs).
#nullable enable
using System;
using System.ComponentModel;

namespace CsharpcCoreLib
{
    [Shadows("System.ComponentModel.MarshalByValueComponent")]
    public class MarshalByValueComponentImpl : IComponent, IDisposable, IServiceProvider
    {
        private ISite? _site;
        private EventHandlerList? _events;
        // .NET keeps Disposed in Events under a private key no subclass can name, so a field behaves the same
        // and keeps EventHandlerList (System.ComponentModel.Primitives) out of programs that don't use Events.
        private EventHandler? _disposed;

        public MarshalByValueComponentImpl() { }

        // .NET's finalizer calls Dispose(false), which does nothing here or in the derived System.Data types.

        public event EventHandler? Disposed
        {
            add { _disposed += value; }
            remove { _disposed -= value; }
        }

        protected EventHandlerList Events => _events ??= new EventHandlerList();

        public virtual ISite? Site
        {
            get => _site;
            set => _site = value;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (this)
                {
                    _site?.Container?.Remove(this);
                    _disposed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public virtual IContainer? Container => _site?.Container;

        public virtual object? GetService(Type service) => _site?.GetService(service);

        public virtual bool DesignMode => _site?.DesignMode ?? false;

        public override string? ToString()
        {
            ISite? s = _site;
            return s != null ? s.Name + " [" + GetType().FullName + "]" : GetType().FullName;
        }
    }
}
