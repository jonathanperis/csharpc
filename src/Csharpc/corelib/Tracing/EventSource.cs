// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: System.Diagnostics.Tracing.EventSource without an EventPipe/ETW backend: only in-process
// EventListeners (EventListener.cs) can enable it. With no listener enabled it is the state in which .NET's
// EventSource writes nothing: IsEnabled() is false and every WriteEvent/Write returns before looking at its
// arguments. A listener's EnableEvents builds the event table from the [Event] methods (the compiler records
// them: ModuleCodeGen.EventSource.cs), checks it as .NET checks a manifest, calls OnEventCommand, and from then
// on WriteEvent dispatches to the listeners whose level and keyword filter lets the event through. What is
// observable without a listener follows dotnet/runtime release/11.0 (EventSource.cs, TraceLoggingEventSource.cs):
// the IsSupported feature switch (false for file-based apps, whose runtimeconfig turns EventSourceSupport off),
// Name and Guid from [EventSource] or the type name (Guid derived from the name with .NET's SHA-1 name hashing,
// ported below), Settings validation, traits, ConstructionException, ToString, Dispose.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.Tracing.EventSource")]
    public class EventSourceImpl : IDisposable
    {
        private string m_name;
        private Guid m_guid;
        private EventSourceSettings m_config;
        private string[] m_traits;
        private Exception m_constructionException;
        private bool m_eventSourceDisposed;
        private EventHandler<EventCommandEventArgs> m_eventCommandExecuted;

        // The listener side (EventListener.cs): the events built at the first EnableEvents, and the state of enabling.
        private bool m_manifestBuilt;
        private string m_manifestError;
        private Dictionary<int, EventDescriptorData> m_eventData;
        private volatile bool m_eventSourceEnabled;
        private EventLevel m_level;
        private EventKeywords m_matchAnyKeyword;
        [ThreadStatic] private static bool t_reportingMessage;

        // 0: not read yet, 1: supported, 2: turned off (System.Diagnostics.Tracing.EventSource.IsSupported).
        private static int s_isSupported;

        internal static bool IsSupported
        {
            get
            {
                if (s_isSupported == 0)
                    s_isSupported = AppContext.TryGetSwitch("System.Diagnostics.Tracing.EventSource.IsSupported", out bool supported) && !supported ? 2 : 1;
                return s_isSupported == 1;
            }
        }

        protected EventSourceImpl() : this(EventSourceSettings.EtwManifestEventFormat) { }

        protected EventSourceImpl(bool throwOnEventWriteErrors)
            : this(EventSourceSettings.EtwManifestEventFormat | (throwOnEventWriteErrors ? EventSourceSettings.ThrowOnEventWriteErrors : 0)) { }

        protected EventSourceImpl(EventSourceSettings settings) : this(settings, null) { }

        protected EventSourceImpl(EventSourceSettings settings, params string[] traits)
        {
            if (IsSupported)
            {
                m_config = ValidateSettings(settings);
                Type myType = GetType();
                Initialize(GetGuid(myType), GetName(myType), traits);
            }
        }

        public EventSourceImpl(string eventSourceName, Guid eventSourceGuid)
            : this(eventSourceName, eventSourceGuid, EventSourceSettings.EtwManifestEventFormat) { }

        public EventSourceImpl(string eventSourceName, Guid eventSourceGuid, EventSourceSettings settings, string[] traits = null)
        {
            if (IsSupported)
            {
                m_config = ValidateSettings(settings);
                Initialize(eventSourceGuid, eventSourceName, traits);
            }
        }

        public EventSourceImpl(string eventSourceName) : this(eventSourceName, EventSourceSettings.EtwSelfDescribingEventFormat) { }

        public EventSourceImpl(string eventSourceName, EventSourceSettings config) : this(eventSourceName, config, null) { }

        public EventSourceImpl(string eventSourceName, EventSourceSettings config, params string[] traits)
            : this(eventSourceName, GenerateGuidFromName((eventSourceName ?? throw new ArgumentNullException("eventSourceName")).ToUpperInvariant()), config, traits) { }

        private void Initialize(Guid eventSourceGuid, string eventSourceName, string[] traits)
        {
            try
            {
                m_traits = traits;
                if (m_traits != null && m_traits.Length % 2 != 0)
                    throw new ArgumentException("There must be an even number of trait strings (they are key-value pairs).", "traits");
                if (eventSourceGuid == Guid.Empty) throw new ArgumentException("The Guid of an EventSource must be non zero.");
                if (eventSourceName == null) throw new ArgumentException("The name of an EventSource must not be null.");
                m_name = eventSourceName;
                m_guid = eventSourceGuid;
                EventListenerImpl.AddEventSource(this); // listeners hear of it (and may enable it) before the derived constructor runs
            }
            catch (Exception e)
            {
                if (m_constructionException == null) m_constructionException = e;
            }
        }

        private static EventSourceSettings ValidateSettings(EventSourceSettings settings)
        {
            const EventSourceSettings evtFormatMask = EventSourceSettings.EtwManifestEventFormat | EventSourceSettings.EtwSelfDescribingEventFormat;
            if ((settings & evtFormatMask) == evtFormatMask)
                throw new ArgumentException("Can't specify both etw event format flags.", "settings");
            // If you did not explicitly ask for manifest, you get self-describing.
            if ((settings & evtFormatMask) == 0) settings |= EventSourceSettings.EtwSelfDescribingEventFormat;
            return settings;
        }

        public string Name => m_name;
        public Guid Guid => m_guid;
        public EventSourceSettings Settings => m_config;
        public Exception ConstructionException => m_constructionException;

        public bool IsEnabled() => m_eventSourceEnabled;
        public bool IsEnabled(EventLevel level, EventKeywords keywords) =>
            m_eventSourceEnabled && PassesFilter(m_level, m_matchAnyKeyword, level, (long)keywords);
        public bool IsEnabled(EventLevel level, EventKeywords keywords, EventChannel channel) => IsEnabled(level, keywords);

        /// <summary>EventSource.IsEnabledCommon: a LogAlways level enables everything, a zero event or filter keyword set matches any.</summary>
        internal static bool PassesFilter(EventLevel currentLevel, EventKeywords currentMatchAnyKeyword, EventLevel eventLevel, long eventKeywords)
        {
            if (currentLevel != 0 && currentLevel < eventLevel) return false;
            if (currentMatchAnyKeyword != 0 && eventKeywords != 0 && ((long)currentMatchAnyKeyword & eventKeywords) == 0) return false;
            return true;
        }

        /// <summary>EventSource.GetSources: the live sources, oldest first (here only the program's own).</summary>
        public static IEnumerable<EventSource> GetSources()
        {
            var sources = new List<EventSource>();
            foreach (EventSourceImpl source in EventListenerImpl.LiveSources())
                if (!source.m_eventSourceDisposed) sources.Add((EventSource)(object)source);
            return sources;
        }

        public string GetTrait(string key)
        {
            if (m_traits != null)
                for (int i = 0; i < m_traits.Length - 1; i += 2)
                    if (m_traits[i] == key) return m_traits[i + 1];
            return null;
        }

        public override string ToString() => IsSupported ? "EventSource(" + Name + ", " + Guid.ToString() + ")" : GetType().ToString(); // object.ToString()

        public static string GetName(Type eventSourceType)
        {
            if (eventSourceType == null) throw new ArgumentNullException("eventSourceType");
            return CoreIntrinsics.EventSourceAttribute(eventSourceType, 0) ?? eventSourceType.Name;
        }

        public static Guid GetGuid(Type eventSourceType)
        {
            if (eventSourceType == null) throw new ArgumentNullException("eventSourceType");
            string guid = CoreIntrinsics.EventSourceAttribute(eventSourceType, 1);
            if (guid != null && Guid.TryParse(guid, out Guid g)) return g;
            string name = CoreIntrinsics.EventSourceAttribute(eventSourceType, 0) ?? eventSourceType.Name;
            if (name == null) throw new ArgumentException("The name of the type is invalid.", "eventSourceType");
            return GenerateGuidFromName(name.ToUpperInvariant());
        }

        [ThreadStatic] private static Guid t_activityId;

        public static Guid CurrentThreadActivityId => IsSupported ? t_activityId : default(Guid);

        public static void SetCurrentThreadActivityId(Guid activityId)
        {
            if (IsSupported) t_activityId = activityId;
        }

        public static void SetCurrentThreadActivityId(Guid activityId, out Guid oldActivityThatWillContinue)
        {
            oldActivityThatWillContinue = CurrentThreadActivityId;
            SetCurrentThreadActivityId(activityId);
        }

        public event EventHandler<EventCommandEventArgs> EventCommandExecuted
        {
            add { m_eventCommandExecuted += value; }
            remove { m_eventCommandExecuted -= value; }
        }

        protected virtual void OnEventCommand(EventCommandEventArgs command) { }

        // Writing: with no listener enabled .NET returns before looking at the arguments.
        protected void WriteEvent(int eventId) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[0]); }
        protected void WriteEvent(int eventId, int arg1) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1 }); }
        protected void WriteEvent(int eventId, int arg1, int arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, int arg1, int arg2, int arg3) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2, arg3 }); }
        protected void WriteEvent(int eventId, long arg1) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1 }); }
        protected void WriteEvent(int eventId, long arg1, long arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, long arg1, long arg2, long arg3) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2, arg3 }); }
        protected void WriteEvent(int eventId, string arg1) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1 }); }
        protected void WriteEvent(int eventId, string arg1, string arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, string arg1, string arg2, string arg3) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2, arg3 }); }
        protected void WriteEvent(int eventId, string arg1, int arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, string arg1, int arg2, int arg3) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2, arg3 }); }
        protected void WriteEvent(int eventId, string arg1, long arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, long arg1, string arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, int arg1, string arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, byte[] arg1) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1 }); }
        protected void WriteEvent(int eventId, long arg1, byte[] arg2) { if (m_eventSourceEnabled) WriteEventTyped(eventId, new object[] { arg1, arg2 }); }
        protected void WriteEvent(int eventId, params EventSourcePrimitive[] args)
        {
            if (!m_eventSourceEnabled) return;
            object[] values = new object[args.Length];
            for (int i = 0; i < values.Length; i++) values[i] = args[i].Value;
            WriteEventVarargs(eventId, default, values);
        }
        protected void WriteEvent(int eventId, params object[] args) { if (m_eventSourceEnabled) WriteEventVarargs(eventId, default, args); }
        protected unsafe void WriteEventCore(int eventId, int eventDataCount, EventData* data)
        {
            if (m_eventSourceEnabled) WriteEventVarargs(eventId, default, DecodeEventData(eventId, eventDataCount, data, 0));
        }
        protected unsafe void WriteEventWithRelatedActivityIdCore(int eventId, Guid* relatedActivityId, int eventDataCount, EventData* data)
        {
            if (m_eventSourceEnabled) WriteEventVarargs(eventId, relatedActivityId == null ? default : *relatedActivityId, DecodeEventData(eventId, eventDataCount, data, 1), false, true);
        }
        protected void WriteEventWithRelatedActivityId(int eventId, Guid relatedActivityId, params object[] args)
        {
            if (m_eventSourceEnabled) WriteEventVarargs(eventId, relatedActivityId, args, false, true);
        }

        // Write(...): the self-describing events. Without a payload they are dispatched; a payload is read from the
        // properties of an anonymous or [EventData] type by reflection in .NET, which csharpc can't do yet.
        public void Write(string eventName) => WriteSelfDescribing(eventName, default(EventSourceOptions), false);
        public void Write(string eventName, EventSourceOptions options) => WriteSelfDescribing(eventName, options, false);
        public void Write<T>(string eventName, T data) => WriteSelfDescribing(eventName, default(EventSourceOptions), true);
        public void Write<T>(string eventName, EventSourceOptions options, T data) => WriteSelfDescribing(eventName, options, true);
        public void Write<T>(string eventName, ref EventSourceOptions options, ref T data) => WriteSelfDescribing(eventName, options, true);
        public void Write<T>(string eventName, ref EventSourceOptions options, ref Guid activityId, ref Guid relatedActivityId, ref T data) =>
            WriteSelfDescribing(eventName, options, true);

        private void WriteSelfDescribing(string eventName, EventSourceOptions options, bool hasPayload)
        {
            if (!m_eventSourceEnabled || m_eventSourceDisposed) return;
            if (eventName == null) throw new ArgumentNullException("eventName");
            ref EventSourceOptionsImpl impl = ref Unsafe.As<EventSourceOptions, EventSourceOptionsImpl>(ref options);
            EventLevel level = impl.LevelWasSet ? impl.Level : EventLevel.Verbose;
            long keywords = (long)impl.Keywords;
            EventWrittenEventArgsImpl args = null;
            foreach (EventListenerImpl listener in EventListenerImpl.Listeners())
            {
                ListenerEnablement enablement = listener.EnablementOf(this);
                if (enablement == null || !PassesFilter(enablement.Level, enablement.Keywords, level, keywords)) continue;
                if (hasPayload)
                    throw new NotSupportedException("EventSource.Write<T> with a payload is not supported by csharpc yet: .NET reads the payload from the properties of the data type by reflection.");
                args ??= new EventWrittenEventArgsImpl((EventSource)(object)this)
                {
                    EventId = -1, EventName = eventName, Level = level, Keywords = (EventKeywords)keywords, Opcode = impl.Opcode, Tags = impl.Tags,
                    ActivityId = CurrentThreadActivityId, Payload = new ReadOnlyCollection<object>(new object[0]),
                    PayloadNames = new ReadOnlyCollection<string>(new string[0]),
                };
                DeliverTo(listener, args);
            }
        }

        // ---- the listener side ------------------------------------------------------------------------------------

        internal bool HasEvent(int eventId) => m_eventData != null && m_eventData.ContainsKey(eventId);

        /// <summary>The event table: built on the first EnableEvents; an error is what .NET reports as a manifest error.</summary>
        private bool EnsureEventData()
        {
            lock (EventListenerImpl.s_lock)
            {
                if (m_manifestBuilt) return m_manifestError == null;
                m_manifestBuilt = true;
                try
                {
                    List<EventDescriptorData> events = EventManifest.Parse(CoreIntrinsics.EventSourceAttribute(GetType(), 2), out HashSet<long> keywords, out HashSet<long> opcodes);
                    EventManifest.Validate(events, keywords, opcodes);
                    m_eventData = new Dictionary<int, EventDescriptorData>();
                    foreach (EventDescriptorData e in events) m_eventData[e.Id] = e;
                }
                catch (ArgumentException e) { m_manifestError = e.Message; }
                return m_manifestError == null;
            }
        }

        /// <summary>EventListener.EnableEvents / DisableEvents / Dispose for this source (EventSource.SendCommand with a listener).</summary>
        internal void SendListenerCommand(EventListenerImpl listener, bool enable, EventLevel level, EventKeywords matchAnyKeyword, IDictionary<string, string> arguments)
        {
            if (!IsSupported || m_name == null || m_eventSourceDisposed) return;
            ListenerEnablement enablement = null;
            if (enable)
            {
                if (listener.IsDisposed) throw new ObjectDisposedException("EventListener");
                if (!EnsureEventData())
                {
                    ReportOutOfBandMessage("ERROR: Exception in Command Processing for EventSource " + m_name + ": " + m_manifestError, listener);
                    return;
                }
                enablement = new ListenerEnablement { Level = level, Keywords = matchAnyKeyword };
                listener.SetEnablement(this, enablement);
            }
            else listener.RemoveEnablement(this); // (a command is sent even to a listener that had not enabled it)
            RecomputeEnabled(enablement);
            var commandArgs = new EventCommandEventArgsImpl(enable ? EventCommand.Enable : EventCommand.Disable,
                arguments ?? new Dictionary<string, string>(), this, listener, enablement);
            EventCommandEventArgs exposed = (EventCommandEventArgs)(object)commandArgs;
            OnEventCommand(exposed);
            m_eventCommandExecuted?.Invoke(this, exposed);
        }

        // IsEnabled(level, keywords) answers for the widest filter enabled since the source was last off: a listener that
        // enables it widens the level and keywords (LogAlways, no keywords: everything), one that leaves narrows nothing
        // until none is left (as .NET's m_level / m_matchAnyKeyword).
        private void RecomputeEnabled(ListenerEnablement enabled)
        {
            bool any = false;
            foreach (EventListenerImpl listener in EventListenerImpl.Listeners())
                if (listener.EnablementOf(this) != null) { any = true; break; }
            if (any && enabled != null)
            {
                if (!m_eventSourceEnabled) { m_level = enabled.Level; m_matchAnyKeyword = enabled.Keywords; }
                else
                {
                    if (enabled.Level == EventLevel.LogAlways || m_level == EventLevel.LogAlways) m_level = EventLevel.LogAlways;
                    else if (enabled.Level > m_level) m_level = enabled.Level;
                    m_matchAnyKeyword = enabled.Keywords == EventKeywords.None || m_matchAnyKeyword == EventKeywords.None
                        ? EventKeywords.None : m_matchAnyKeyword | enabled.Keywords;
                }
            }
            m_eventSourceEnabled = any;
        }

        private void WriteEventTyped(int eventId, object[] args) => WriteEventVarargs(eventId, default, args, true);

        private void WriteEventVarargs(int eventId, Guid relatedActivityId, object[] args, bool typed = false, bool withRelated = false)
        {
            if (m_eventSourceDisposed) return;
            EventDescriptorData data;
            lock (EventListenerImpl.s_lock)
            {
                if (m_eventData == null || !m_eventData.TryGetValue(eventId, out data)) return;
            }
            // WriteEventWithRelatedActivityId: the event's first parameter is the related activity id, not payload.
            int skip = withRelated ? 1 : 0;
            if (withRelated && (data.ParameterNames.Length == 0 || data.ParameterKinds[0] != "G" || data.ParameterNames[0] != "relatedActivityId"))
            {
                ReportOutOfBandMessage("EventSourceException while processing event \"" + data.Name + "\": System.ArgumentException:EventSource expects the first parameter of the Event method to be of type Guid and to be named \"relatedActivityId\" when calling WriteEventWithRelatedActivityId.", null);
                return;
            }
            int parameters = data.ParameterNames.Length - skip;
            if (args.Length != parameters)
                ReportOutOfBandMessage("Event " + EventManifest.Num(eventId) + " was called with " + EventManifest.Num(args.Length)
                    + " argument(s), but it is defined with " + EventManifest.Num(parameters) + " parameter(s).", null);
            int count = Math.Min(args.Length, parameters);
            object[] payload = new object[count];
            Array.Copy(args, payload, count);
            // The fixed-arity overloads write raw values, which .NET reads back as the event's declared parameter types
            // (a long argument of an int parameter is an Int32 payload); params object[] keeps the boxed values.
            if (typed)
                for (int i = 0; i < count; i++) payload[i] = AsParameter(payload[i], data.ParameterKinds[i + skip]);
            string[] names = new string[count];
            Array.Copy(data.ParameterNames, skip, names, 0, count);
            EventWrittenEventArgsImpl eventArgs = null;
            foreach (EventListenerImpl listener in EventListenerImpl.Listeners())
            {
                ListenerEnablement enablement = listener.EnablementOf(this);
                if (enablement == null) continue;
                bool enabled;
                lock (EventListenerImpl.s_lock)
                    enabled = enablement.Overrides != null && enablement.Overrides.TryGetValue(eventId, out bool overridden)
                        ? overridden : PassesFilter(enablement.Level, enablement.Keywords, data.Level, data.UserKeywords);
                if (!enabled) continue;
                eventArgs ??= new EventWrittenEventArgsImpl((EventSource)(object)this)
                {
                    EventId = eventId, EventName = data.Name, Level = data.Level, Keywords = (EventKeywords)(data.UserKeywords | EventDescriptorData.SessionMask),
                    Opcode = data.Opcode, Task = data.Task, Version = data.Version, Channel = data.Channel, Tags = data.Tags, Message = data.Message,
                    ActivityId = CurrentThreadActivityId, RelatedActivityId = relatedActivityId,
                    Payload = new ReadOnlyCollection<object>(payload), PayloadNames = new ReadOnlyCollection<string>(names),
                };
                DeliverTo(listener, eventArgs);
            }
        }

        private static object AsParameter(object value, string kind)
        {
            if (!(value is int || value is long)) return value;
            long x = value is int i ? i : (long)value;
            switch (kind)
            {
                case "i": return unchecked((int)x);
                case "ui": return unchecked((uint)x);
                case "l": return x;
                case "ul": return unchecked((ulong)x);
                case "s": return unchecked((short)x);
                case "us": return unchecked((ushort)x);
                case "B": return unchecked((byte)x);
                case "sb": return unchecked((sbyte)x);
                case "b": return unchecked((int)x) != 0;
                case "c": return unchecked((char)x);
                default: return value;
            }
        }

        private void DeliverTo(EventListenerImpl listener, EventWrittenEventArgsImpl args)
        {
            try { listener.OnEventWritten((EventWrittenEventArgs)(object)args); }
            catch (Exception e)
            {
                if ((m_config & EventSourceSettings.ThrowOnEventWriteErrors) != 0) throw;
                ReportOutOfBandMessage("ERROR: Exception during EventSource.OnEventWritten: " + e.Message, null);
            }
        }

        /// <summary>EventSource.ReportOutOfBandMessage: the text reaches the listeners as an "EventSourceMessage" event.</summary>
        private void ReportOutOfBandMessage(string message, EventListenerImpl alsoTo)
        {
            if (t_reportingMessage) return; // a listener that throws on the message itself
            t_reportingMessage = true;
            try
            {
                EventWrittenEventArgsImpl args = null;
                foreach (EventListenerImpl listener in EventListenerImpl.Listeners())
                {
                    if (!ReferenceEquals(listener, alsoTo) && listener.EnablementOf(this) == null) continue;
                    args ??= new EventWrittenEventArgsImpl((EventSource)(object)this)
                    {
                        EventId = 0, EventName = "EventSourceMessage", Level = EventLevel.LogAlways, Message = message, ActivityId = CurrentThreadActivityId,
                        Payload = new ReadOnlyCollection<object>(new object[] { message }), PayloadNames = new ReadOnlyCollection<string>(new[] { "message" }),
                    };
                    try { listener.OnEventWritten((EventWrittenEventArgs)(object)args); } catch (Exception) { }
                }
            }
            finally { t_reportingMessage = false; }
        }

        /// <summary>WriteEventCore's descriptors, read as the event's parameters (ETW's wire format: strings are null-terminated UTF-16, bool is 4 bytes).</summary>
        private unsafe object[] DecodeEventData(int eventId, int eventDataCount, EventData* data, int skip)
        {
            EventDescriptorData d;
            lock (EventListenerImpl.s_lock)
            {
                if (m_eventData == null || !m_eventData.TryGetValue(eventId, out d)) return new object[0];
            }
            object[] values = new object[Math.Max(0, Math.Min(eventDataCount, d.ParameterKinds.Length - skip))];
            for (int i = 0; i < values.Length; i++)
            {
                byte* pointer = (byte*)(void*)data[i].m_Ptr;
                int size = data[i].m_Size;
                values[i] = DecodeValue(d.ParameterKinds[i + skip], pointer, size);
            }
            return values;
        }

        private static unsafe object DecodeValue(string kind, byte* p, int size)
        {
            if (kind.Length > 1 && kind[0] == 'E') kind = kind.Substring(1); // enums are written as their underlying type
            switch (kind)
            {
                case "S":
                {
                    int chars = size / 2;
                    if (chars > 0 && ((char*)p)[chars - 1] == '\0') chars--;
                    return new string((char*)p, 0, chars);
                }
                case "i": return *(int*)p;
                case "ui": return *(uint*)p;
                case "l": return *(long*)p;
                case "ul": return *(ulong*)p;
                case "s": return *(short*)p;
                case "us": return *(ushort*)p;
                case "B": return *p;
                case "sb": return *(sbyte*)p;
                case "c": return *(char*)p;
                case "f": return *(float*)p;
                case "d": return *(double*)p;
                case "b": return *(int*)p != 0;
                case "G": return *(Guid*)p;
                case "p": return (IntPtr)(*(long*)p);
                case "up": return (UIntPtr)(*(ulong*)p);
                default: throw new NotSupportedException("WriteEventCore of a '" + kind + "' parameter is not supported by csharpc yet.");
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!IsSupported) return;
            // Listeners are not told: the source just stops writing.
            if (disposing) { m_eventSourceDisposed = true; m_eventSourceEnabled = false; }
        }

        /// <summary>The GUID .NET derives from a name (EventSource.GenerateGuidFromName: SHA-1, RFC 4122 version 5).</summary>
        private static Guid GenerateGuidFromName(string name)
        {
            byte[] source = new byte[16 + name.Length * 2];
            byte[] namespaceBytes = { 0x48, 0x2C, 0x2D, 0xB2, 0xC3, 0x90, 0x47, 0xC8, 0x87, 0xF8, 0x1A, 0x15, 0xBF, 0xC1, 0x30, 0xFB };
            System.Array.Copy(namespaceBytes, source, 16);
            for (int i = 0; i < name.Length; i++)
            {
                source[16 + 2 * i] = (byte)(name[i] >> 8); // Encoding.BigEndianUnicode
                source[17 + 2 * i] = (byte)name[i];
            }
            byte[] bytes = Sha1ForNonSecretPurposes.HashData(source);
            bytes[7] = unchecked((byte)((bytes[7] & 0x0F) | 0x50)); // Set high 4 bits of octet 7 to 5, as per RFC 4122
            byte[] guid = new byte[16];
            System.Array.Copy(bytes, guid, 16);
            return new Guid(guid);
        }

        [Shadows("System.Diagnostics.Tracing.EventSource+EventData")]
        protected internal struct EventData
        {
            internal ulong m_Ptr;
            internal int m_Size;
            internal int m_Reserved;

            public unsafe IntPtr DataPointer
            {
                get => (IntPtr)(void*)m_Ptr;
                set => m_Ptr = unchecked((ulong)(void*)value);
            }

            public int Size
            {
                get => m_Size;
                set => m_Size = value;
            }

            internal int Reserved
            {
                get => m_Reserved;
                set => m_Reserved = value;
            }
        }

        [Shadows("System.Diagnostics.Tracing.EventSource+EventSourcePrimitive")]
        public readonly struct EventSourcePrimitive
        {
            internal readonly object Value;

            private EventSourcePrimitive(object value) { Value = value; }

            public static implicit operator EventSourcePrimitive(bool value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(byte value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(short value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(int value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(long value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(sbyte value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(ushort value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(uint value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(ulong value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(float value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(double value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(decimal value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(string value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(byte[] value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(Guid value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(DateTime value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(char value) => new EventSourcePrimitive(value);
            public static implicit operator EventSourcePrimitive(Enum value) => new EventSourcePrimitive(value);
        }
    }

    [Shadows("System.Diagnostics.Tracing.EventSourceOptions")]
    public struct EventSourceOptionsImpl
    {
        private EventKeywords _keywords;
        private EventTags _tags;
        private EventActivityOptions _activityOptions;
        private byte _level;
        private byte _opcode;
        private byte _valuesSet;

        private const byte KeywordsSet = 0x1, TagsSet = 0x2, LevelSet = 0x4, OpcodeSet = 0x8, ActivityOptionsSet = 0x10;

        // An option nobody set means the default of Write: Verbose, opcode Info, no keywords or tags.
        internal bool LevelWasSet => (_valuesSet & LevelSet) != 0;

        public EventLevel Level
        {
            get => (EventLevel)_level;
            set { _level = checked((byte)value); _valuesSet |= LevelSet; }
        }

        public EventOpcode Opcode
        {
            get => (EventOpcode)_opcode;
            set { _opcode = checked((byte)value); _valuesSet |= OpcodeSet; }
        }

        public EventKeywords Keywords
        {
            get => _keywords;
            set { _keywords = value; _valuesSet |= KeywordsSet; }
        }

        public EventTags Tags
        {
            get => _tags;
            set { _tags = value; _valuesSet |= TagsSet; }
        }

        public EventActivityOptions ActivityOptions
        {
            get => _activityOptions;
            set { _activityOptions = value; _valuesSet |= ActivityOptionsSet; }
        }
    }

    /// <summary>dotnet/runtime src/libraries/Common/src/System/Sha1ForNonSecretPurposes.cs (HashData).</summary>
    internal static class Sha1ForNonSecretPurposes
    {
        public static byte[] HashData(byte[] source)
        {
            uint[] w = new uint[85];
            w[80] = 0x67452301;
            w[81] = 0xEFCDAB89;
            w[82] = 0x98BADCFE;
            w[83] = 0x10325476;
            w[84] = 0xC3D2E1F0;
            int full = source.Length / 64 * 64;
            for (int offset = 0; offset < full; offset += 64) Block(w, source, offset);
            byte[] tail = new byte[128];
            int pos = 0;
            for (int i = full; i < source.Length; i++) tail[pos++] = source[i];
            tail[pos++] = 0x80;
            while ((pos & 63) != 56) tail[pos++] = 0;
            ulong bits = (ulong)source.Length * 8;
            for (int i = 7; i >= 0; i--) tail[pos++] = (byte)(bits >> (8 * i));
            for (int offset = 0; offset < pos; offset += 64) Block(w, tail, offset);
            byte[] destination = new byte[20];
            for (int i = 0; i < 5; i++)
            {
                uint v = w[80 + i];
                destination[4 * i] = (byte)(v >> 24);
                destination[4 * i + 1] = (byte)(v >> 16);
                destination[4 * i + 2] = (byte)(v >> 8);
                destination[4 * i + 3] = (byte)v;
            }
            return destination;
        }

        private static uint Rol(uint value, int count) => (value << count) | (value >> (32 - count));

        private static void Block(uint[] w, byte[] data, int offset)
        {
            for (int i = 0; i < 16; i++)
            {
                int p = offset + 4 * i;
                w[i] = (uint)data[p] << 24 | (uint)data[p + 1] << 16 | (uint)data[p + 2] << 8 | data[p + 3];
            }
            for (int i = 16; i < 80; i++) w[i] = Rol(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
            uint a = w[80], b = w[81], c = w[82], d = w[83], e = w[84];
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint temp = Rol(a, 5) + f + e + k + w[i];
                e = d; d = c; c = Rol(b, 30); b = a; a = temp;
            }
            w[80] += a;
            w[81] += b;
            w[82] += c;
            w[83] += d;
            w[84] += e;
        }
    }
}
