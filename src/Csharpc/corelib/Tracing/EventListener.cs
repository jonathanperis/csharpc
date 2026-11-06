// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: the listener side of System.Diagnostics.Tracing: EventListener (derivable), EventWrittenEventArgs,
// EventCommandEventArgs and EventSourceCreatedEventArgs, over EventSourceImpl (EventSource.cs). In-process
// listeners receive the events a program's own EventSources write, with what dotnet/runtime release/11.0 gives
// them: the [Event] metadata (id, name, level, keywords with the session bits, opcode, task - generated from the id
// when an event has neither a task nor an opcode -, version, channel, tags, message, payload names and values),
// OnEventSourceCreated for the sources that exist when a listener is created (from the listener's base
// constructor, before the derived constructor runs, as in .NET) and for later ones, EnableEvents / DisableEvents /
// Dispose with the OnEventCommand callbacks, per-listener level and keyword filters, the "EventSourceMessage"
// events .NET reports a failed manifest or a wrong argument count with. The [Event] methods are read at compile time
// (ModuleCodeGen.EventSource.cs), not by reflection. Not modeled: the EventSources .NET's own runtime and libraries
// create (NativeRuntimeEventSource, System.Runtime, TplEventSource ...: a listener here sees only the program's own),
// EventSource.Write<T> payloads and EventSource.SendCommand / GenerateManifest (explicit NotSupportedException /
// CSLLVM0002), EventPipe and ETW sessions.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Threading;

namespace CsharpcCoreLib
{
    /// <summary>One [Event] method of an EventSource, as the compiler recorded it (ModuleCodeGen.EventsBlob).</summary>
    internal sealed class EventDescriptorData
    {
        // ETW's reserved keyword bits: .NET ORs them into every manifest event's keywords (the sessions it can be enabled for).
        internal const long SessionMask = 0xF000_0000_0000L;

        internal int Id;
        internal string Name;
        internal EventLevel Level;
        internal long UserKeywords;
        internal EventOpcode Opcode;
        internal EventTask Task;
        internal byte Version;
        internal EventChannel Channel;
        internal EventTags Tags;
        internal EventActivityOptions Activity;
        internal string Message;
        internal string[] ParameterNames;
        internal string[] ParameterKinds;
    }

    internal sealed class ListenerEnablement
    {
        internal EventLevel Level;
        internal EventKeywords Keywords;
        internal Dictionary<int, bool> Overrides; // EnableEvent / DisableEvent from OnEventCommand
    }

    [Shadows("System.Diagnostics.Tracing.EventListener")]
    public abstract class EventListenerImpl : IDisposable
    {
        internal static readonly object s_lock = new object();
        internal static readonly List<EventListenerImpl> s_listeners = new List<EventListenerImpl>();
        internal static readonly List<WeakReference<EventSourceImpl>> s_sources = new List<WeakReference<EventSourceImpl>>();

        // The sources this listener enabled (guarded by s_lock), in the order it enabled them.
        private readonly Dictionary<EventSourceImpl, ListenerEnablement> m_enabled = new Dictionary<EventSourceImpl, ListenerEnablement>();
        private bool m_disposed;
        private EventHandler<EventSourceCreatedEventArgs> m_eventSourceCreated;
        private EventHandler<EventWrittenEventArgs> m_eventWritten;

        protected EventListenerImpl()
        {
            lock (s_lock) s_listeners.Add(this);
            // The sources that exist now, oldest first. This runs in the base constructor: a derived class's
            // constructor body has not run yet (its field initializers have), exactly as in .NET.
            foreach (EventSourceImpl source in LiveSources())
                CreatedCallback(source);
        }

        internal static List<EventSourceImpl> LiveSources()
        {
            var result = new List<EventSourceImpl>();
            lock (s_lock)
                foreach (WeakReference<EventSourceImpl> reference in s_sources)
                    if (reference.TryGetTarget(out EventSourceImpl source)) result.Add(source);
            return result;
        }

        /// <summary>The live listeners, newest first (.NET keeps them as a linked list that a new one is put in front of).</summary>
        internal static EventListenerImpl[] Listeners()
        {
            lock (s_lock)
            {
                EventListenerImpl[] listeners = s_listeners.ToArray();
                Array.Reverse(listeners);
                return listeners;
            }
        }

        /// <summary>A source finished constructing: every listener hears of it (EventSource.Initialize).</summary>
        internal static void AddEventSource(EventSourceImpl source)
        {
            lock (s_lock) s_sources.Add(new WeakReference<EventSourceImpl>(source));
            foreach (EventListenerImpl listener in Listeners()) listener.CreatedCallback(source);
        }

        private void CreatedCallback(EventSourceImpl source) => OnEventSourceCreated((EventSource)(object)source);

        protected internal virtual void OnEventSourceCreated(EventSource eventSource)
        {
            EventHandler<EventSourceCreatedEventArgs> handler = m_eventSourceCreated;
            if (handler != null)
                handler(this, (EventSourceCreatedEventArgs)(object)new EventSourceCreatedEventArgsImpl { EventSource = eventSource });
        }

        protected internal virtual void OnEventWritten(EventWrittenEventArgs eventData)
        {
            EventHandler<EventWrittenEventArgs> handler = m_eventWritten;
            if (handler != null) handler(this, eventData);
        }

        public event EventHandler<EventSourceCreatedEventArgs> EventSourceCreated
        {
            // A new handler is called at once for every source that exists, as .NET's add accessor does.
            add
            {
                foreach (EventSourceImpl source in LiveSources())
                    value(this, (EventSourceCreatedEventArgs)(object)new EventSourceCreatedEventArgsImpl { EventSource = (EventSource)(object)source });
                m_eventSourceCreated += value;
            }
            remove { m_eventSourceCreated -= value; }
        }

        public event EventHandler<EventWrittenEventArgs> EventWritten
        {
            add { m_eventWritten += value; }
            remove { m_eventWritten -= value; }
        }

        public void EnableEvents(EventSource eventSource, EventLevel level) =>
            EnableEvents(eventSource, level, EventKeywords.None, null);

        public void EnableEvents(EventSource eventSource, EventLevel level, EventKeywords matchAnyKeyword) =>
            EnableEvents(eventSource, level, matchAnyKeyword, null);

        public void EnableEvents(EventSource eventSource, EventLevel level, EventKeywords matchAnyKeyword, IDictionary<string, string> arguments)
        {
            if (eventSource == null) throw new ArgumentNullException("eventSource");
            ((EventSourceImpl)(object)eventSource).SendListenerCommand(this, true, level, matchAnyKeyword, arguments);
        }

        public void DisableEvents(EventSource eventSource)
        {
            if (eventSource == null) throw new ArgumentNullException("eventSource");
            ((EventSourceImpl)(object)eventSource).SendListenerCommand(this, false, EventLevel.LogAlways, EventKeywords.None, null);
        }

        /// <summary>EventListener.EventSourceIndex: the position of the source among the live sources (-1: not one of them).</summary>
        protected static int EventSourceIndex(EventSource eventSource)
        {
            int index = 0;
            foreach (EventSourceImpl source in LiveSources())
            {
                if (ReferenceEquals(source, eventSource)) return index;
                index++;
            }
            return -1;
        }

        public virtual void Dispose()
        {
            List<EventSourceImpl> enabled;
            lock (s_lock)
            {
                if (m_disposed) return;
                m_disposed = true;
                s_listeners.Remove(this);
                enabled = new List<EventSourceImpl>(m_enabled.Keys);
            }
            foreach (EventSourceImpl source in enabled) source.SendListenerCommand(this, false, EventLevel.LogAlways, EventKeywords.None, null);
        }

        // ---- what EventSourceImpl reads and changes -------------------------------------------------------------

        internal ListenerEnablement EnablementOf(EventSourceImpl source)
        {
            lock (s_lock) return m_enabled.TryGetValue(source, out ListenerEnablement enablement) ? enablement : null;
        }

        internal void SetEnablement(EventSourceImpl source, ListenerEnablement enablement)
        {
            lock (s_lock) m_enabled[source] = enablement;
        }

        internal bool RemoveEnablement(EventSourceImpl source)
        {
            lock (s_lock) return m_enabled.Remove(source);
        }

        internal bool IsDisposed
        {
            get { lock (s_lock) return m_disposed; }
        }
    }

    [Shadows("System.Diagnostics.Tracing.EventSourceCreatedEventArgs")]
    public class EventSourceCreatedEventArgsImpl : EventArgs
    {
        public EventSource EventSource { get; internal set; }
    }

    [Shadows("System.Diagnostics.Tracing.EventWrittenEventArgs")]
    public class EventWrittenEventArgsImpl : EventArgs
    {
        internal EventWrittenEventArgsImpl(EventSource source)
        {
            EventSource = source;
            TimeStamp = DateTime.UtcNow;
            OSThreadId = Environment.CurrentManagedThreadId;
        }

        public string EventName { get; internal set; }
        public int EventId { get; internal set; }
        public Guid ActivityId { get; internal set; }
        public EventChannel Channel { get; internal set; }
        public EventKeywords Keywords { get; internal set; }
        public EventLevel Level { get; internal set; }
        public string Message { get; internal set; }
        public EventOpcode Opcode { get; internal set; }
        public long OSThreadId { get; internal set; }
        public ReadOnlyCollection<object> Payload { get; internal set; }
        public ReadOnlyCollection<string> PayloadNames { get; internal set; }
        public Guid RelatedActivityId { get; internal set; }
        public EventSource EventSource { get; internal set; }
        public EventTags Tags { get; internal set; }
        public EventTask Task { get; internal set; }
        public DateTime TimeStamp { get; internal set; }
        public byte Version { get; internal set; }
    }

    [Shadows("System.Diagnostics.Tracing.EventCommandEventArgs")]
    public class EventCommandEventArgsImpl : EventArgs
    {
        private readonly EventSourceImpl m_source;
        private readonly EventListenerImpl m_listener;
        private readonly ListenerEnablement m_enablement;

        internal EventCommandEventArgsImpl(EventCommand command, IDictionary<string, string> arguments, EventSourceImpl source, EventListenerImpl listener, ListenerEnablement enablement)
        {
            Command = command;
            Arguments = arguments;
            m_source = source;
            m_listener = listener;
            m_enablement = enablement;
        }

        public EventCommand Command { get; internal set; }
        public IDictionary<string, string> Arguments { get; internal set; }

        public bool EnableEvent(int eventId) => SetEvent(eventId, true);
        public bool DisableEvent(int eventId) => SetEvent(eventId, false);

        private bool SetEvent(int eventId, bool enable)
        {
            if (Command != EventCommand.Enable && Command != EventCommand.Update) throw new InvalidOperationException();
            if (!m_source.HasEvent(eventId)) return false;
            if (m_enablement != null)
                lock (EventListenerImpl.s_lock) (m_enablement.Overrides ??= new Dictionary<int, bool>())[eventId] = enable;
            return true;
        }
    }

    /// <summary>Parses what ModuleCodeGen.EventsBlob wrote and checks it the way .NET checks a manifest.</summary>
    internal static class EventManifest
    {
        private const char RecordSeparator = '\u001e', FieldSeparator = '\u001f';

        // Numbers are formatted and parsed by hand: the culture-aware ones would link the number and culture
        // machinery into every program that merely has an EventSource.
        internal static string Num(long value)
        {
            if (value == 0) return "0";
            bool negative = value < 0;
            ulong magnitude = negative ? (ulong)(-(value + 1)) + 1 : (ulong)value;
            char[] buffer = new char[20];
            int index = buffer.Length;
            while (magnitude != 0) { buffer[--index] = (char)('0' + (int)(magnitude % 10)); magnitude /= 10; }
            if (negative) buffer[--index] = '-';
            return new string(buffer, index, buffer.Length - index);
        }

        internal static string Hex(long value)
        {
            if (value == 0) return "0";
            ulong bits = (ulong)value;
            char[] buffer = new char[16];
            int index = buffer.Length;
            while (bits != 0) { int digit = (int)(bits & 0xF); buffer[--index] = (char)(digit < 10 ? '0' + digit : 'a' + digit - 10); bits >>= 4; }
            return new string(buffer, index, buffer.Length - index);
        }

        private static long ParseLong(string text)
        {
            bool negative = text.Length > 0 && text[0] == '-';
            long value = 0;
            for (int i = negative ? 1 : 0; i < text.Length; i++) value = value * 10 + (text[i] - '0');
            return negative ? -value : value;
        }

        internal static List<EventDescriptorData> Parse(string blob, out HashSet<long> keywords, out HashSet<long> opcodes)
        {
            keywords = new HashSet<long>();
            opcodes = new HashSet<long> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 240 }; // EventOpcode's own values
            var events = new List<EventDescriptorData>();
            if (blob == null) return events;
            foreach (string record in blob.Split(RecordSeparator))
            {
                string[] fields = record.Split(FieldSeparator);
                if (fields[0] == "k") { for (int i = 1; i < fields.Length; i++) if (fields[i].Length > 0) keywords.Add(ParseLong(fields[i])); }
                else if (fields[0] == "o") { for (int i = 1; i < fields.Length; i++) if (fields[i].Length > 0) opcodes.Add(ParseLong(fields[i])); }
                else if (fields[0] == "e")
                {
                    int count = (int)ParseLong(fields[13]);
                    var data = new EventDescriptorData
                    {
                        Id = (int)ParseLong(fields[1]),
                        Name = fields[2],
                        Level = (EventLevel)(int)ParseLong(fields[3]),
                        UserKeywords = ParseLong(fields[4]),
                        Opcode = (EventOpcode)(int)ParseLong(fields[5]),
                        Task = (EventTask)(int)ParseLong(fields[6]),
                        Version = (byte)ParseLong(fields[7]),
                        Channel = (EventChannel)(int)ParseLong(fields[8]),
                        Tags = (EventTags)(int)ParseLong(fields[9]),
                        Activity = (EventActivityOptions)(int)ParseLong(fields[10]),
                        Message = fields[11] == "1" ? fields[12] : null,
                        ParameterNames = new string[count],
                        ParameterKinds = new string[count],
                    };
                    for (int i = 0; i < count; i++)
                    {
                        data.ParameterKinds[i] = fields[14 + 2 * i];
                        data.ParameterNames[i] = fields[15 + 2 * i];
                    }
                    events.Add(data);
                }
            }
            return events;
        }

        /// <summary>
        /// The checks EventSource makes while it builds its manifest (the messages are .NET's; an error makes the
        /// source report "ERROR: Exception in Command Processing" and stay disabled). Throws ArgumentException.
        /// </summary>
        internal static void Validate(List<EventDescriptorData> events, HashSet<long> keywords, HashSet<long> opcodes)
        {
            long definedMask = 0;
            foreach (long keyword in keywords) definedMask |= keyword;
            var seen = new HashSet<int>();
            foreach (EventDescriptorData e in events)
            {
                if (e.Id <= 0) throw new ArgumentException("Event IDs must be positive integers.");
                if (!seen.Add(e.Id)) throw new ArgumentException("Event " + e.Name + " has ID " + Num(e.Id) + " which is already in use.");
                long undefined = e.UserKeywords & ~definedMask;
                if (undefined != 0) throw new ArgumentException("Use of undefined keyword value 0x" + Hex(undefined) + " for event " + e.Name + ".");
                if (!opcodes.Contains((long)e.Opcode)) throw new ArgumentException("Use of undefined opcode value " + Num((int)e.Opcode) + " for event " + e.Name + ".");
                foreach (string kind in e.ParameterKinds)
                    if (kind.Length > 0 && kind[0] == '!') throw new ArgumentException("Unsupported type " + kind.Substring(1) + " in event source.");
                // No task and no opcode: .NET gives the event a task of its own, counting down from 0xFFFE.
                if (e.Task == EventTask.None && e.Opcode == EventOpcode.Info) e.Task = (EventTask)(0xFFFE - e.Id);
            }
        }
    }
}
