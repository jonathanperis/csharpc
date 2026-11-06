// System.Reflection.EventInfo over csharpc's compile-time metadata (csrt_EventRecord, kept for the types the
// program reflects over: [DynamicallyAccessedMembers(PublicEvents)] locations and typeof(X).GetEvent(...) on a
// known type, as NativeAOT keeps them; events are described only for programs that call Type.GetEvent/GetEvents).
// The accessors are the methods' own records, so AddEventHandler/RemoveEventHandler go through their invokers.
// The member lists follow RuntimeType's (declared events first, then the base types'; GetEvents drops a base
// event whose name a more derived type has).
#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.EventInfo")]
    public abstract class EventInfoImpl : MemberInfo
    {
        protected EventInfoImpl() { }

        public override MemberTypes MemberType => MemberTypes.Event;

        public abstract EventAttributes Attributes { get; }
        public bool IsSpecialName => (Attributes & EventAttributes.SpecialName) != 0;

        public MethodInfo[] GetOtherMethods() => GetOtherMethods(false);
        public virtual MethodInfo[] GetOtherMethods(bool nonPublic) => throw new NotImplementedException();

        public virtual MethodInfo AddMethod => GetAddMethod(true);
        public virtual MethodInfo RemoveMethod => GetRemoveMethod(true);
        public virtual MethodInfo RaiseMethod => GetRaiseMethod(true);

        public MethodInfo GetAddMethod() => GetAddMethod(false);
        public MethodInfo GetRemoveMethod() => GetRemoveMethod(false);
        public MethodInfo GetRaiseMethod() => GetRaiseMethod(false);

        public abstract MethodInfo GetAddMethod(bool nonPublic);
        public abstract MethodInfo GetRemoveMethod(bool nonPublic);
        public abstract MethodInfo GetRaiseMethod(bool nonPublic);

        public virtual bool IsMulticast
        {
            get
            {
                Type cl = EventHandlerType;
                Type mc = typeof(MulticastDelegate);
                return mc.IsAssignableFrom(cl);
            }
        }

        public virtual Type EventHandlerType
        {
            get
            {
                MethodInfo m = GetAddMethod(true);
                ParameterInfo[] p = m.GetParameters();
                Type del = typeof(Delegate);
                for (int i = 0; i < p.Length; i++)
                {
                    Type c = p[i].ParameterType;
                    if (c.IsSubclassOf(del)) return c;
                }
                return null;
            }
        }

        public virtual void AddEventHandler(object target, Delegate handler)
        {
            MethodInfo addMethod = GetAddMethod(false) ?? throw new InvalidOperationException("Cannot add the event handler since no public add method exists for the event.");
            addMethod.Invoke(target, new object[] { handler });
        }

        public virtual void RemoveEventHandler(object target, Delegate handler)
        {
            MethodInfo removeMethod = GetRemoveMethod(false) ?? throw new InvalidOperationException("Cannot remove the event handler since no public remove method exists for the event.");
            removeMethod.Invoke(target, new object[] { handler });
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(EventInfoImpl left, EventInfoImpl right) => right is null ? left is null : ReferenceEquals(left, right) || (left is not null && left.Equals((object)right));
        public static bool operator !=(EventInfoImpl left, EventInfoImpl right) => !(left == right);
    }

    [Shadows("System.Reflection.RuntimeEventInfo")]
    internal sealed unsafe class RuntimeEventInfoImpl : EventInfo
    {
        internal readonly byte* _record;
        private readonly Type _reflected;
        private string _name;

        internal RuntimeEventInfoImpl(byte* record, Type reflected) { _record = record; _reflected = reflected; }

        private static MethodInfo Accessor(byte* record) => record == null ? null : (MethodInfo)ReflectionRuntime.Member(record, false);

        public override string Name => _name ??= Utf8.FromNative(ReflectionRecords.EventName(_record));
        public override Type DeclaringType => ReflectionRuntime.TypeOf(ReflectionRecords.EventDeclaring(_record));
        public override Type ReflectedType => _reflected;
        public override EventAttributes Attributes => (EventAttributes)ReflectionRecords.EventAttributes(_record);

        public override MethodInfo GetAddMethod(bool nonPublic) => Include(Accessor(ReflectionRecords.EventAdd(_record)), nonPublic);
        public override MethodInfo GetRemoveMethod(bool nonPublic) => Include(Accessor(ReflectionRecords.EventRemove(_record)), nonPublic);
        public override MethodInfo GetRaiseMethod(bool nonPublic) => Include(Accessor(ReflectionRecords.EventRaise(_record)), nonPublic);

        private static MethodInfo Include(MethodInfo accessor, bool nonPublic) => accessor != null && (nonPublic || accessor.IsPublic) ? accessor : null;

        public override MethodInfo[] GetOtherMethods(bool nonPublic)
        {
            if ((ReflectionRecords.EventFlags(_record) & 8) != 0)
                throw new NotSupportedException("EventInfo.GetOtherMethods of the event '" + Name + "' (it has `.other` accessors) is not supported by csharpc yet.");
            return new MethodInfo[0];
        }

        private void RequireAttributes()
        {
            if ((ReflectionRecords.EventFlags(_record) & 4) != 0)
                throw new NotSupportedException("The custom attributes of the event '" + Name + "' are not supported by csharpc yet (csc synthesizes attributes csharpc can't reproduce exactly, or the event is declared in a reference assembly).");
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            RequireAttributes();
            return ReflectionRuntime.IsDefined(ReflectionRecords.EventAttributeRecords(_record), ReflectionRecords.EventAttributeCount(_record), attributeType);
        }

        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            RequireAttributes();
            return ReflectionRuntime.CreateAttributes(ReflectionRecords.EventAttributeRecords(_record), ReflectionRecords.EventAttributeCount(_record), attributeType);
        }

        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            RequireAttributes();
            return ReflectionRuntime.AttributeData(ReflectionRecords.EventAttributeRecords(_record), ReflectionRecords.EventAttributeCount(_record));
        }

        public override string ToString()
        {
            MethodInfo add = Accessor(ReflectionRecords.EventAdd(_record));
            ParameterInfo[] parameters;
            if (add == null || (parameters = add.GetParameters()).Length == 0)
                throw new InvalidOperationException("Cannot add the event handler since no public add method exists for the event.");
            return ReflectionRuntime.FormatTypeName(parameters[0].ParameterType) + " " + Name;
        }
    }

    internal static unsafe class EventReflection
    {
        private static readonly object s_lock = new object();
        private static Dictionary<nint, EventInfo> s_events;

        private static EventInfo Event(byte* record, Type reflected)
        {
            lock (s_lock)
            {
                s_events ??= new Dictionary<nint, EventInfo>();
                if (!s_events.TryGetValue((nint)record, out var info))
                    s_events[(nint)record] = info = new RuntimeEventInfoImpl(record, reflected);
                return info;
            }
        }

        // RuntimeType's filter: visibility and static/instance; inherited static events only with FlattenHierarchy.
        private static bool Matches(int flags, bool inherited, BindingFlags binding)
        {
            bool isPublic = (flags & 1) != 0, isStatic = (flags & 2) != 0;
            if (!((isPublic && (binding & BindingFlags.Public) != 0) || (!isPublic && (binding & BindingFlags.NonPublic) != 0))) return false;
            if (!(isStatic ? (binding & BindingFlags.Static) != 0 : (binding & BindingFlags.Instance) != 0)) return false;
            if (inherited && (binding & BindingFlags.DeclaredOnly) != 0) return false;
            if (inherited && isStatic && (binding & BindingFlags.FlattenHierarchy) == 0) return false;
            return true;
        }

        public static EventInfo[] GetEventsAll(Type type) => GetEvents(type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        public static EventInfo[] GetEventsFlags(Type type, BindingFlags flags) => GetEvents(type, flags);

        internal static EventInfo[] GetEvents(Type type, BindingFlags flags)
        {
            byte* info = ReflectionRuntime.Info(type, "events");
            ReflectionRuntime.Require(info, type, flags, 1024, 2048, "events");
            var list = new List<EventInfo>();
            var names = new HashSet<string>();
            byte* records = ReflectionRecords.InfoEvents(info);
            int count = ReflectionRecords.InfoEventCount(info);
            nint self = CoreIntrinsics.csrt_type_handle(type);
            for (int i = 0; i < count; i++)
            {
                byte* record = records + i * ReflectionRecords.EventSize;
                var candidate = Event(record, type);
                // A name more derived types already have hides the base type's event (the list is derived first).
                if (!names.Add(candidate.Name)) continue;
                if (Matches(ReflectionRecords.EventFlags(record), ReflectionRecords.EventDeclaring(record) != self, flags)) list.Add(candidate);
            }
            return list.ToArray();
        }

        public static EventInfo GetEventName(Type type, string name) => GetEvent(type, name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        public static EventInfo GetEventNameFlags(Type type, string name, BindingFlags flags) => GetEvent(type, name, flags);

        private static EventInfo GetEvent(Type type, string name, BindingFlags flags)
        {
            ArgumentNullException.ThrowIfNull(name);
            byte* info = ReflectionRuntime.Info(type, "events");
            ReflectionRuntime.Require(info, type, flags, 1024, 2048, "events");
            bool ignoreCase = (flags & BindingFlags.IgnoreCase) != 0;
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            // RuntimeType's PopulateEvents with a case-sensitive name keeps only the first event of that name (the most
            // derived one, so a private event of a derived type hides a public one of its base) and leaves the binding
            // flags to the caller; a case-insensitive lookup hides by exact name and can find two.
            var seen = ignoreCase ? new HashSet<string>() : null;
            EventInfo match = null;
            byte* records = ReflectionRecords.InfoEvents(info);
            int count = ReflectionRecords.InfoEventCount(info);
            nint self = CoreIntrinsics.csrt_type_handle(type);
            for (int i = 0; i < count; i++)
            {
                byte* record = records + i * ReflectionRecords.EventSize;
                var candidate = Event(record, type);
                if (!string.Equals(candidate.Name, name, comparison)) continue;
                bool matches = Matches(ReflectionRecords.EventFlags(record), ReflectionRecords.EventDeclaring(record) != self, flags);
                if (!ignoreCase) return matches ? candidate : null;
                if (!seen.Add(candidate.Name) || !matches) continue;
                if (match != null) throw new AmbiguousMatchException("Ambiguous match found for '" + match.DeclaringType + " " + match + "'.");
                match = candidate;
            }
            return match;
        }
    }
}
