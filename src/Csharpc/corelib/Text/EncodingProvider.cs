// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/Text/
// EncodingProvider.cs; see THIRD-PARTY-NOTICES.md. Encoding.RegisterProvider adds a provider that
// Encoding.GetEncoding asks before its own table (Roslyn registers CodePagesEncodingProvider, compiled from IL).
#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.EncodingProvider")]
    public abstract class EncodingProviderImpl
    {
        private static object s_lock = new object();
        private static EncodingProvider[] s_providers;

        public EncodingProviderImpl() { }

        public abstract Encoding GetEncoding(string name);
        public abstract Encoding GetEncoding(int codepage);

        // GetEncoding should return either valid encoding or null; it shouldn't throw any exception except on null name.
        public virtual Encoding GetEncoding(string name, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
        {
            Encoding enc = GetEncoding(name);
            if (enc != null)
            {
                enc = (Encoding)enc.Clone();
                enc.EncoderFallback = encoderFallback;
                enc.DecoderFallback = decoderFallback;
            }
            return enc;
        }

        public virtual Encoding GetEncoding(int codepage, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
        {
            Encoding enc = GetEncoding(codepage);
            if (enc != null)
            {
                enc = (Encoding)enc.Clone();
                enc.EncoderFallback = encoderFallback;
                enc.DecoderFallback = decoderFallback;
            }
            return enc;
        }

        public virtual IEnumerable<EncodingInfo> GetEncodings() => Array.Empty<EncodingInfo>();

        internal static void AddProvider(EncodingProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            lock (s_lock)
            {
                if (s_providers == null)
                {
                    s_providers = new EncodingProvider[] { provider };
                    return;
                }
                if (Array.IndexOf(s_providers, provider) >= 0) return;
                var providers = new EncodingProvider[s_providers.Length + 1];
                Array.Copy(s_providers, providers, s_providers.Length);
                providers[s_providers.Length] = provider;
                s_providers = providers;
            }
        }

        internal static Encoding GetEncodingFromProvider(int codepage)
        {
            EncodingProvider[] providers = s_providers;
            if (providers == null) return null;
            foreach (EncodingProvider provider in providers)
            {
                Encoding enc = provider.GetEncoding(codepage);
                if (enc != null) return enc;
            }
            return null;
        }

        internal static Encoding GetEncodingFromProvider(string encodingName)
        {
            EncodingProvider[] providers = s_providers;
            if (providers == null) return null;
            foreach (EncodingProvider provider in providers)
            {
                Encoding enc = provider.GetEncoding(encodingName);
                if (enc != null) return enc;
            }
            return null;
        }

        internal static Encoding GetEncodingFromProvider(int codepage, EncoderFallback enc, DecoderFallback dec)
        {
            EncodingProvider[] providers = s_providers;
            if (providers == null) return null;
            foreach (EncodingProvider provider in providers)
            {
                Encoding encoding = provider.GetEncoding(codepage, enc, dec);
                if (encoding != null) return encoding;
            }
            return null;
        }

        internal static Encoding GetEncodingFromProvider(string encodingName, EncoderFallback enc, DecoderFallback dec)
        {
            EncodingProvider[] providers = s_providers;
            if (providers == null) return null;
            foreach (EncodingProvider provider in providers)
            {
                Encoding encoding = provider.GetEncoding(encodingName, enc, dec);
                if (encoding != null) return encoding;
            }
            return null;
        }
    }
}
