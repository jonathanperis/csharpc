// Small library base classes that user code derives from: EventArgs (the event pattern), Attribute
// (custom attributes; csharpc has no reflection, so they only need to be constructible), and the
// INotifyPropertyChanged argument types.
#nullable disable
using System;
using System.ComponentModel;

namespace CsharpcCoreLib
{
    [Shadows("System.EventArgs")]
    public class EventArgsImpl
    {
        private static EventArgs s_empty;

        public EventArgsImpl() { }

        /// <summary>EventArgs.Empty (a static readonly field in .NET; read through BclValueTypes' field table).</summary>
        internal static EventArgs GetEmpty() => s_empty ??= new EventArgs();
    }

    [Shadows("System.Attribute")]
    public abstract class AttributeImpl
    {
        protected AttributeImpl() { }

        /// <summary>Attribute.TypeId: default is the attribute's runtime type (.NET).</summary>
        public virtual object TypeId => GetType();

        /// <summary>Attribute.IsDefaultAttribute: default false; ConfigurationIgnoreAttribute overrides.</summary>
        public virtual bool IsDefaultAttribute() => false;

        public virtual bool Match(object obj) => Equals(obj);
    }

    [Shadows("System.ComponentModel.PropertyChangedEventArgs")]
    public class PropertyChangedEventArgsImpl : EventArgs
    {
        private readonly string _propertyName;
        public PropertyChangedEventArgsImpl(string propertyName) { _propertyName = propertyName; }
        public virtual string PropertyName => _propertyName;
    }

    [Shadows("System.ComponentModel.PropertyChangingEventArgs")]
    public class PropertyChangingEventArgsImpl : EventArgs
    {
        private readonly string _propertyName;
        public PropertyChangingEventArgsImpl(string propertyName) { _propertyName = propertyName; }
        public virtual string PropertyName => _propertyName;
    }

    [Shadows("System.ComponentModel.CancelEventArgs")]
    public class CancelEventArgsImpl : EventArgs
    {
        public CancelEventArgsImpl() { }
        public CancelEventArgsImpl(bool cancel) { Cancel = cancel; }
        public bool Cancel { get; set; }
    }
}
