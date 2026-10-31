// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: EventCounters (DiagnosticCounter, PollingCounter, IncrementingPollingCounter) after dotnet/runtime
// release/11.0. .NET publishes a counter to its EventSource's CounterGroup, which only polls once an
// EventListener or a tracing session enables the "EventCounterIntervalSec" command; csharpc has neither
// (EventSource.cs), so a counter is never polled and the group is not needed: what remains observable is the
// argument checks, the properties, AddMetadata and ToString — as in .NET without a listener.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.Tracing.DiagnosticCounter")]
    public abstract class DiagnosticCounterImpl : IDisposable
    {
        private Dictionary<string, string> _metadata;

        internal DiagnosticCounterImpl(string Name, EventSource EventSource)
        {
            ArgumentNullException.ThrowIfNull(Name);
            ArgumentNullException.ThrowIfNull(EventSource);
            this.Name = Name;
            this.EventSource = EventSource;
        }

        // .NET removes the counter from its group (none here: it was never polled).
        public void Dispose() { }

        public void AddMetadata(string key, string value)
        {
            lock (this)
            {
                _metadata ??= new Dictionary<string, string>();
                _metadata.Add(key, value);
            }
        }

        public string DisplayName { get; set; } = "";
        public string DisplayUnits { get; set; } = "";
        public string Name { get; }
        public EventSource EventSource { get; }
    }

    [Shadows("System.Diagnostics.Tracing.PollingCounter")]
    public class PollingCounterImpl : DiagnosticCounterImpl
    {
        private readonly Func<double> _metricProvider;
        private double _lastVal;

        public PollingCounterImpl(string name, EventSource eventSource, Func<double> metricProvider) : base(name, eventSource)
        {
            ArgumentNullException.ThrowIfNull(metricProvider);
            _metricProvider = metricProvider;
        }

        public override string ToString() => "PollingCounter '" + Name + "' Count 1 Mean " + _lastVal.ToString("n3");
    }

    [Shadows("System.Diagnostics.Tracing.IncrementingPollingCounter")]
    public class IncrementingPollingCounterImpl : DiagnosticCounterImpl
    {
        private readonly Func<double> _totalValueProvider;
        private double _increment;

        public IncrementingPollingCounterImpl(string name, EventSource eventSource, Func<double> totalValueProvider) : base(name, eventSource)
        {
            ArgumentNullException.ThrowIfNull(totalValueProvider);
            _totalValueProvider = totalValueProvider;
        }

        public TimeSpan DisplayRateTimeScale { get; set; }

        public override string ToString() => "IncrementingPollingCounter '" + Name + "' Increment " + _increment.ToString();
    }
}
