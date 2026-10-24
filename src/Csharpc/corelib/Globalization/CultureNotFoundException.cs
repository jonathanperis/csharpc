// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Private.CoreLib/src/System/Globalization;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md); changes of substance
// are marked `csharpc:`.
#nullable enable annotations

using System.Globalization;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System.Globalization;
    using System;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    [Shadows("System.Globalization.CultureNotFoundException")]
    public class CultureNotFoundException : ArgumentException
    {
        private readonly string? _invalidCultureName; // unrecognized culture name
        private readonly int? _invalidCultureId;     // unrecognized culture Lcid

        public CultureNotFoundException()
            : base(DefaultMessage)
        {
        }

        public CultureNotFoundException(string? message)
            : base(message ?? DefaultMessage)
        {
        }

        public CultureNotFoundException(string? paramName, string? message)
            : base(message ?? DefaultMessage, paramName)
        {
        }

        public CultureNotFoundException(string? message, Exception? innerException)
            : base(message ?? DefaultMessage, innerException)
        {
        }

        public CultureNotFoundException(string? paramName, string? invalidCultureName, string? message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string? message, string? invalidCultureName, Exception? innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string? message, int invalidCultureId, Exception? innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureId = invalidCultureId;
        }

        public CultureNotFoundException(string? paramName, int invalidCultureId, string? message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureId = invalidCultureId;
        }

        // csharpc: the serialization support (ISerializable) is omitted.

        // csharpc: the serialization support (ISerializable) is omitted.

        public virtual int? InvalidCultureId => _invalidCultureId;

        public virtual string? InvalidCultureName => _invalidCultureName;

        private static string DefaultMessage => SR.Argument_CultureNotSupported;

        private string? FormattedInvalidCultureId =>
            InvalidCultureId != null ?
                // csharpc: string.Format(CultureInfo.InvariantCulture, "{0} (0x{0:x4})", id) without composite
                // formatting (which would link every boxed type's formatter into programs that create cultures)
                string.Concat(CsharpcCoreLib.Number.Int64ToDecStrInvariant((int)InvalidCultureId), " (0x", SR.Hex((int)InvalidCultureId, lower: true, 4), ")") :
                InvalidCultureName;

        public override string Message
        {
            get
            {
                string s = base.Message;
                if (_invalidCultureId != null || _invalidCultureName != null)
                {
                    string valueMessage = SR.Format(SR.Argument_CultureInvalidIdentifier, FormattedInvalidCultureId);
                    if (s == null)
                    {
                        return valueMessage;
                    }

                    return s + global::System.Environment.NewLine + valueMessage;
                }
                return s;
            }
        }
    }
}
