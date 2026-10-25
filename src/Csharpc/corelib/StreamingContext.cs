// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/
// Runtime/Serialization/StreamingContext.cs; see THIRD-PARTY-NOTICES.md. The value type of the
// serialization signatures (GetObjectData, [OnDeserialized] callbacks) that classes still declare.
#nullable disable
using System;
using System.Runtime.Serialization;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.Serialization.StreamingContext")]
    public readonly struct StreamingContextImpl
    {
        private readonly object _additionalContext;
        private readonly StreamingContextStates _state;

        public StreamingContextImpl(StreamingContextStates state) : this(state, null) { }

        public StreamingContextImpl(StreamingContextStates state, object additional)
        {
            _state = state;
            _additionalContext = additional;
        }

        public override bool Equals(object obj) =>
            obj is StreamingContext other && ReferenceEquals(other.Context, _additionalContext) && other.State == _state;

        public override int GetHashCode() => (int)_state;

        public StreamingContextStates State => _state;
        public object Context => _additionalContext;
    }
}
