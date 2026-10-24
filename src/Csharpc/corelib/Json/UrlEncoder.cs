// System.Text.Encodings.Web.UrlEncoder (ASP.NET Core's QueryString, routing and WebUtilities), after
// dotnet/runtime's UrlEncoder.cs and DefaultUrlEncoder.cs (release/11.0): the inbox escaping of
// Encoders.cs with RFC 3987's isegment-nz-nc as the allowed set and %XX escapes of the UTF-8 bytes.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Encodings.Web.UrlEncoder")]
    public abstract class UrlEncoderImpl : TextEncoderImpl
    {
        private static UrlEncoderImpl s_default;

        public static UrlEncoderImpl Default => s_default ??= new DefaultUrlEncoder(new TextEncoderSettingsImpl(UnicodeRangesImpl.BasicLatin));

        public static UrlEncoderImpl Create(TextEncoderSettingsImpl settings) => new DefaultUrlEncoder(settings);

        public static UrlEncoderImpl Create(params UnicodeRangeImpl[] allowedRanges) => new DefaultUrlEncoder(new TextEncoderSettingsImpl(allowedRanges));
    }

    // The base escaping forbids the HTML-sensitive characters (& ' + among them); a URL component also
    // escapes these, and the specials U+FFF0..U+FFFF.
    internal sealed class DefaultUrlEncoder : UrlEncoderImpl
    {
        private const string ExtraCharactersToEscape = " #%/:=?[\\]^`{|}";

        internal DefaultUrlEncoder(TextEncoderSettingsImpl settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _allowed = settings._bitmap.Clone();
            _allowed.ForbidUndefinedCharacters();
            _allowed.ForbidHtmlCharacters();
            for (int i = 0; i < ExtraCharactersToEscape.Length; i++) _allowed.ForbidChar(ExtraCharactersToEscape[i]);
            for (int c = 0xFFF0; c <= 0xFFFF; c++) _allowed.ForbidChar(c);
            _percentEncoding = true;
        }

        public override int MaxOutputCharactersPerInputCharacter => 9;
    }
}
