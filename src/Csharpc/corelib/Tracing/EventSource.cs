// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: System.Diagnostics.Tracing.EventSource without a tracing backend. A csharpc executable has no
// EventPipe/ETW session and no EventListener (deriving from it is a CSLLVM0001 error), and that is
// exactly the state in which .NET's EventSource never writes anything: IsEnabled() is false and every
// WriteEvent/Write returns at once. What is observable without a listener follows dotnet/runtime
// release/11.0 (EventSource.cs, TraceLoggingEventSource.cs): the IsSupported feature switch (false for
// file-based apps, whose runtimeconfig turns EventSourceSupport off), Name and Guid from
// [EventSource] or the type name (Guid derived from the name with .NET's SHA-1 name hashing, ported
// below), Settings validation, traits, ConstructionException, ToString, Dispose.
#nullable disable
using System;
using System.Diagnostics.Tracing;

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

        // No session or listener ever enables an EventSource here.
        public bool IsEnabled() => false;
        public bool IsEnabled(EventLevel level, EventKeywords keywords) => false;
        public bool IsEnabled(EventLevel level, EventKeywords keywords, EventChannel channel) => false;

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

        // Writing: nothing is enabled, so .NET returns before looking at the arguments.
        protected void WriteEvent(int eventId) { }
        protected void WriteEvent(int eventId, int arg1) { }
        protected void WriteEvent(int eventId, int arg1, int arg2) { }
        protected void WriteEvent(int eventId, int arg1, int arg2, int arg3) { }
        protected void WriteEvent(int eventId, long arg1) { }
        protected void WriteEvent(int eventId, long arg1, long arg2) { }
        protected void WriteEvent(int eventId, long arg1, long arg2, long arg3) { }
        protected void WriteEvent(int eventId, string arg1) { }
        protected void WriteEvent(int eventId, string arg1, string arg2) { }
        protected void WriteEvent(int eventId, string arg1, string arg2, string arg3) { }
        protected void WriteEvent(int eventId, string arg1, int arg2) { }
        protected void WriteEvent(int eventId, string arg1, int arg2, int arg3) { }
        protected void WriteEvent(int eventId, string arg1, long arg2) { }
        protected void WriteEvent(int eventId, long arg1, string arg2) { }
        protected void WriteEvent(int eventId, int arg1, string arg2) { }
        protected void WriteEvent(int eventId, byte[] arg1) { }
        protected void WriteEvent(int eventId, long arg1, byte[] arg2) { }
        protected void WriteEvent(int eventId, params EventSourcePrimitive[] args) { }
        protected void WriteEvent(int eventId, params object[] args) { }
        protected unsafe void WriteEventCore(int eventId, int eventDataCount, EventData* data) { }
        protected unsafe void WriteEventWithRelatedActivityIdCore(int eventId, Guid* relatedActivityId, int eventDataCount, EventData* data) { }
        protected void WriteEventWithRelatedActivityId(int eventId, Guid relatedActivityId, params object[] args) { }

        public void Write(string eventName) { }
        public void Write(string eventName, EventSourceOptions options) { }
        public void Write<T>(string eventName, T data) { }
        public void Write<T>(string eventName, EventSourceOptions options, T data) { }
        public void Write<T>(string eventName, ref EventSourceOptions options, ref T data) { }
        public void Write<T>(string eventName, ref EventSourceOptions options, ref Guid activityId, ref Guid relatedActivityId, ref T data) { }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!IsSupported) return;
            m_eventSourceDisposed = true;
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
        public EventLevel Level { get; set; }
        public EventOpcode Opcode { get; set; }
        public EventKeywords Keywords { get; set; }
        public EventTags Tags { get; set; }
        public EventActivityOptions ActivityOptions { get; set; }
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
