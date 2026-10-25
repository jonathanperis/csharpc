// System.Reflection.AssemblyName: port of dotnet/runtime's AssemblyName.cs, AssemblyNameParser.cs,
// AssemblyNameFormatter.cs and AssemblyNameHelpers.StrongName.cs (release/11.0), MIT license.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: the culture is kept by name and its CultureInfo created on first use of the CultureInfo
// property (an AssemblyName built from a culture-neutral name, which is every name csharpc itself
// produces, never touches the culture model); a non-neutral culture name is still validated by the
// constructor, as .NET's eager `new CultureInfo(name)` does.
#nullable disable
using System;
using System.Configuration.Assemblies;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.AssemblyName")]
    public sealed class AssemblyNameImpl : ICloneable
    {
        private string _name;
        private byte[] _publicKey;
        private byte[] _publicKeyToken;
        private string _cultureName;      // null: no culture; "": neutral
        private CultureInfo _cultureInfo;
        private string _codeBase;
        private Version _version;
        private System.Configuration.Assemblies.AssemblyHashAlgorithm _hashAlgorithm;
        private AssemblyVersionCompatibility _versionCompatibility;
        private AssemblyNameFlags _flags;

        public AssemblyNameImpl(string assemblyName) : this()
        {
            ArgumentException.ThrowIfNullOrEmpty(assemblyName);
            if (assemblyName[0] == '\0') throw new ArgumentException("String cannot be of zero length.");
            var parts = AssemblyNameParser.Parse(assemblyName);
            _name = parts.Name;
            _version = parts.Version;
            _flags = parts.Flags;
            if ((parts.Flags & AssemblyNameFlags.PublicKey) != 0) _publicKey = parts.PublicKeyOrToken;
            else _publicKeyToken = parts.PublicKeyOrToken;
            if (parts.CultureName != null)
            {
                _cultureName = parts.CultureName;
                if (parts.CultureName.Length != 0) _cultureInfo = new CultureInfo(parts.CultureName);
            }
        }

        public AssemblyNameImpl() { _versionCompatibility = AssemblyVersionCompatibility.SameMachine; }

        /// <summary>A display name csharpc produced itself (Assembly.GetName()).</summary>
        internal static AssemblyName FromFullName(string fullName)
        {
            var result = new AssemblyNameImpl();
            var parts = AssemblyNameParser.Parse(fullName);
            result._name = parts.Name;
            result._version = parts.Version;
            result._flags = parts.Flags;
            if ((parts.Flags & AssemblyNameFlags.PublicKey) != 0) result._publicKey = parts.PublicKeyOrToken;
            else result._publicKeyToken = parts.PublicKeyOrToken;
            result._cultureName = parts.CultureName;
            return (AssemblyName)(object)result;
        }

        public string Name { get => _name; set => _name = value; }
        public Version Version { get => _version; set => _version = value; }

        public CultureInfo CultureInfo
        {
            get => _cultureInfo ??= _cultureName == null ? null : _cultureName.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(_cultureName);
            set { _cultureInfo = value; _cultureName = value?.Name; }
        }

        public string CultureName
        {
            get => _cultureInfo != null ? _cultureInfo.Name : _cultureName;
            set
            {
                _cultureInfo = value == null || value.Length == 0 ? null : new CultureInfo(value);
                _cultureName = value;
            }
        }

        public string CodeBase { get => _codeBase; set => _codeBase = value; }

        public ProcessorArchitecture ProcessorArchitecture
        {
            get
            {
                int x = (((int)_flags) & 0x70) >> 4;
                if (x > 5) x = 0;
                return (ProcessorArchitecture)x;
            }
            set
            {
                int x = ((int)value) & 0x07;
                if (x <= 5)
                {
                    _flags = (AssemblyNameFlags)((int)_flags & unchecked((int)0xFFFFFF0F));
                    _flags |= (AssemblyNameFlags)(x << 4);
                }
            }
        }

        public AssemblyContentType ContentType
        {
            get
            {
                int x = (((int)_flags) & 0x00000E00) >> 9;
                if (x > 1) x = 0;
                return (AssemblyContentType)x;
            }
            set
            {
                int x = ((int)value) & 0x07;
                if (x <= 1)
                {
                    _flags = (AssemblyNameFlags)((int)_flags & unchecked((int)0xFFFFF1FF));
                    _flags |= (AssemblyNameFlags)(x << 9);
                }
            }
        }

        public object Clone()
        {
            var name = new AssemblyNameImpl();
            name._name = _name;
            name._publicKey = (byte[])_publicKey?.Clone();
            name._publicKeyToken = (byte[])_publicKeyToken?.Clone();
            name._cultureName = _cultureName;
            name._cultureInfo = _cultureInfo;
            name._version = _version;
            name._flags = _flags;
            name._codeBase = _codeBase;
            name._hashAlgorithm = _hashAlgorithm;
            name._versionCompatibility = _versionCompatibility;
            return name;
        }

        public byte[] GetPublicKey() => _publicKey;

        public void SetPublicKey(byte[] publicKey)
        {
            _publicKey = publicKey;
            if (publicKey == null) _flags &= ~AssemblyNameFlags.PublicKey;
            else _flags |= AssemblyNameFlags.PublicKey;
        }

        public byte[] GetPublicKeyToken() => _publicKeyToken ??= ComputePublicKeyToken(_publicKey);

        public void SetPublicKeyToken(byte[] publicKeyToken) { _publicKeyToken = publicKeyToken; }

        public AssemblyNameFlags Flags
        {
            get => (AssemblyNameFlags)((uint)_flags & 0xFFFFF10F);
            set
            {
                _flags &= unchecked((AssemblyNameFlags)0x00000EF0);
                _flags |= (value & unchecked((AssemblyNameFlags)0xFFFFF10F));
            }
        }

        public System.Configuration.Assemblies.AssemblyHashAlgorithm HashAlgorithm { get => _hashAlgorithm; set => _hashAlgorithm = value; }
        public AssemblyVersionCompatibility VersionCompatibility { get => _versionCompatibility; set => _versionCompatibility = value; }

        public string FullName
        {
            get
            {
                if (string.IsNullOrEmpty(Name)) return string.Empty;
                // Not GetPublicKeyToken(): that latches the token into the name.
                byte[] pkt = _publicKeyToken ?? ComputePublicKeyToken(_publicKey);
                return ComputeDisplayName(Name, Version, CultureName, pkt, Flags, ContentType);
            }
        }

        public override string ToString()
        {
            string s = FullName;
            return s ?? base.ToString();
        }

        public static bool ReferenceMatchesDefinition(AssemblyName reference, AssemblyName definition)
        {
            if (ReferenceEquals(reference, definition)) return true;
            ArgumentNullException.ThrowIfNull(reference);
            ArgumentNullException.ThrowIfNull(definition);
            string refName = reference.Name ?? string.Empty;
            string defName = definition.Name ?? string.Empty;
            return refName.Equals(defName, StringComparison.OrdinalIgnoreCase);
        }

        // ---- AssemblyNameFormatter --------------------------------------------------------------

        private static string ComputeDisplayName(string name, Version version, string cultureName, byte[] pkt, AssemblyNameFlags flags, AssemblyContentType contentType)
        {
            const int PublicKeyTokenLength = 8;
            var sb = new StringBuilder();
            AppendQuoted(sb, name);
            if (version != null)
            {
                ushort major = (ushort)version.Major;
                if (major != ushort.MaxValue)
                {
                    sb.Append(", Version=").Append(major);
                    ushort minor = (ushort)version.Minor;
                    if (minor != ushort.MaxValue)
                    {
                        sb.Append('.').Append(minor);
                        ushort build = (ushort)version.Build;
                        if (build != ushort.MaxValue)
                        {
                            sb.Append('.').Append(build);
                            ushort revision = (ushort)version.Revision;
                            if (revision != ushort.MaxValue) sb.Append('.').Append(revision);
                        }
                    }
                }
            }
            if (cultureName != null)
            {
                if (cultureName.Length == 0) cultureName = "neutral";
                sb.Append(", Culture=");
                AppendQuoted(sb, cultureName);
            }
            if (pkt != null)
            {
                if (pkt.Length > PublicKeyTokenLength) throw new ArgumentException();
                sb.Append(", PublicKeyToken=");
                if (pkt.Length == 0) sb.Append("null");
                else
                    for (int i = 0; i < pkt.Length; i++) sb.Append("0123456789abcdef"[pkt[i] >> 4]).Append("0123456789abcdef"[pkt[i] & 15]);
            }
            if ((flags & AssemblyNameFlags.Retargetable) != 0) sb.Append(", Retargetable=Yes");
            if (contentType == AssemblyContentType.WindowsRuntime) sb.Append(", ContentType=WindowsRuntime");
            // By design (desktop compat) FullName and ToString() do not include ProcessorArchitecture.
            return sb.ToString();
        }

        private static void AppendQuoted(StringBuilder sb, string s)
        {
            // Double quotes when the name has surrounding white space or quotes (.NET's choice).
            bool needsQuoting = s.Length != s.Trim().Length || s.IndexOf('"') >= 0 || s.IndexOf('\'') >= 0;
            if (needsQuoting) sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                switch (s[i])
                {
                    case '\\': case ',': case '=': case '\'': case '"': sb.Append('\\'); break;
                    case '\t': sb.Append("\\t"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\n': sb.Append("\\n"); continue;
                }
                sb.Append(s[i]);
            }
            if (needsQuoting) sb.Append('"');
        }

        // ---- AssemblyNameHelpers.StrongName ----------------------------------------------------

        private static byte[] ComputePublicKeyToken(byte[] publicKey)
        {
            if (publicKey == null) return null;
            if (publicKey.Length == 0) return new byte[0];
            if (!IsValidPublicKey(publicKey)) throw new SecurityException("Invalid assembly public key.");
            byte[] hash = SHA1Impl.HashData(publicKey);
            byte[] token = new byte[8];
            for (int i = 0; i < token.Length; i++) token[i] = hash[hash.Length - 1 - i];
            return token;
        }

        private static uint ReadUInt32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);

        private static bool IsValidPublicKey(byte[] publicKey)
        {
            const uint SizeOfPublicKeyBlob = 12, AlgClassHash = 4 << 13, AlgSidSha1 = 4, AlgClassSignature = 1 << 13, PublicKeyBlob = 6;
            uint length = (uint)publicKey.Length;
            if (length < SizeOfPublicKeyBlob + 4) return false;
            uint sigAlgId = ReadUInt32(publicKey, 0), hashAlgId = ReadUInt32(publicKey, 4), cbPublicKey = ReadUInt32(publicKey, 8);
            if (cbPublicKey != length - SizeOfPublicKeyBlob) return false;
            // The ECMA key: 8 zero bytes, 4 (little-endian), 4 zero bytes.
            if (length == 16)
            {
                bool ecma = true;
                for (int i = 0; i < 16; i++) if (publicKey[i] != (i == 8 ? 4 : 0)) { ecma = false; break; }
                if (ecma) return true;
            }
            bool hashValid = (hashAlgId & (7 << 13)) == AlgClassHash && (hashAlgId & 511) >= AlgSidSha1;
            if (hashAlgId != 0 && !hashValid) return false;
            bool signatureValid = (sigAlgId & (7 << 13)) == AlgClassSignature;
            if (sigAlgId != 0 && !signatureValid) return false;
            return publicKey[SizeOfPublicKeyBlob] == PublicKeyBlob;
        }
    }

    /// <summary>dotnet/runtime's AssemblyNameParser: the display-name lexer and attribute grammar.</summary>
    internal struct AssemblyNameParser
    {
        internal sealed class Parts
        {
            public string Name;
            public Version Version;
            public string CultureName;
            public AssemblyNameFlags Flags;
            public byte[] PublicKeyOrToken;
        }

        private enum Token { Equals = 1, Comma = 2, String = 3, End = 4 }

        private const int SeenVersion = 1, SeenCulture = 2, SeenPublicKeyOrToken = 4, SeenProcessorArchitecture = 8, SeenRetargetable = 16, SeenContentType = 32;

        private readonly string _input;
        private int _index;

        private AssemblyNameParser(string input)
        {
            if (input.Length == 0) throw new ArgumentException("String cannot be of zero length.");
            _input = input;
            _index = 0;
        }

        public static Parts Parse(string name)
        {
            var parser = new AssemblyNameParser(name);
            var result = parser.TryParse();
            if (result != null) return result;
            throw new FileLoadException("The given assembly name was invalid.", name);
        }

        private static bool TryRecordNewSeen(ref int seen, int attribute)
        {
            if ((seen & attribute) != 0) return false;
            seen |= attribute;
            return true;
        }

        private Parts TryParse()
        {
            // Name must come first.
            if (!TryGetNextToken(out string name, out Token token) || token != Token.String || string.IsNullOrEmpty(name)) return null;
            Version version = null;
            string cultureName = null;
            byte[] pkt = null;
            AssemblyNameFlags flags = 0;
            int seen = 0;
            if (!TryGetNextToken(out _, out token)) return null;
            while (token != Token.End)
            {
                if (token != Token.Comma) return null;
                if (!TryGetNextToken(out string attributeName, out token) || token != Token.String) return null;
                if (!TryGetNextToken(out _, out token) || token != Token.Equals) return null;
                if (!TryGetNextToken(out string attributeValue, out token) || token != Token.String) return null;
                if (attributeName == string.Empty) return null;

                if (IsAttribute(attributeName, "Version"))
                {
                    if (!TryRecordNewSeen(ref seen, SeenVersion) || !TryParseVersion(attributeValue, ref version)) return null;
                }
                else if (IsAttribute(attributeName, "Culture"))
                {
                    if (!TryRecordNewSeen(ref seen, SeenCulture)) return null;
                    cultureName = attributeValue.Equals("Neutral", StringComparison.OrdinalIgnoreCase) ? "" : attributeValue;
                }
                else if (IsAttribute(attributeName, "PublicKeyToken"))
                {
                    if (!TryRecordNewSeen(ref seen, SeenPublicKeyOrToken) || !TryParsePKT(attributeValue, isToken: true, out pkt)) return null;
                }
                else if (IsAttribute(attributeName, "PublicKey"))
                {
                    if (!TryRecordNewSeen(ref seen, SeenPublicKeyOrToken) || !TryParsePKT(attributeValue, isToken: false, out pkt)) return null;
                    flags |= AssemblyNameFlags.PublicKey;
                }
                else if (IsAttribute(attributeName, "ProcessorArchitecture"))
                {
                    if (!TryRecordNewSeen(ref seen, SeenProcessorArchitecture)) return null;
                    ProcessorArchitecture arch = TryParseProcessorArchitecture(attributeValue);
                    if (arch == ProcessorArchitecture.None) return null;
                    flags |= (AssemblyNameFlags)(((int)arch) << 4);
                }
                else if (IsAttribute(attributeName, "Retargetable"))
                {
                    if (!TryRecordNewSeen(ref seen, SeenRetargetable)) return null;
                    if (attributeValue.Equals("Yes", StringComparison.OrdinalIgnoreCase)) flags |= AssemblyNameFlags.Retargetable;
                    else if (!attributeValue.Equals("No", StringComparison.OrdinalIgnoreCase)) return null;
                }
                else if (IsAttribute(attributeName, "ContentType"))
                {
                    if (!TryRecordNewSeen(ref seen, SeenContentType)) return null;
                    if (attributeValue.Equals("WindowsRuntime", StringComparison.OrdinalIgnoreCase))
                        flags |= (AssemblyNameFlags)(((int)AssemblyContentType.WindowsRuntime) << 9);
                    else return null;
                }
                // Desktop compat: unknown attribute names are ignored.

                if (!TryGetNextToken(out _, out token)) return null;
            }
            return new Parts { Name = name, Version = version, CultureName = cultureName, Flags = flags, PublicKeyOrToken = pkt };
        }

        private static bool IsAttribute(string candidate, string attributeKind) => candidate.Equals(attributeKind, StringComparison.OrdinalIgnoreCase);

        private static bool TryParseVersion(string attributeValue, ref Version version)
        {
            string[] parts = attributeValue.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;
            // ushort.MaxValue marks a missing component, so a component of 65535 counts as missing (as in .NET).
            int[] numbers = { ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue };
            for (int i = 0; i < parts.Length; i++)
            {
                // NumberStyles.None: decimal digits only, in ushort's range.
                string part = parts[i];
                if (part.Length == 0) return false;
                int value = 0;
                for (int k = 0; k < part.Length; k++)
                {
                    char c = part[k];
                    if (c < '0' || c > '9') return false;
                    value = value * 10 + (c - '0');
                    if (value > ushort.MaxValue) return false;
                }
                numbers[i] = value;
            }
            if (numbers[0] == ushort.MaxValue || numbers[1] == ushort.MaxValue) return false;
            version = numbers[2] == ushort.MaxValue ? new Version(numbers[0], numbers[1])
                : numbers[3] == ushort.MaxValue ? new Version(numbers[0], numbers[1], numbers[2])
                : new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        private static int Hex(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        private static bool TryParsePKT(string attributeValue, bool isToken, out byte[] result)
        {
            if (attributeValue.Equals("null", StringComparison.OrdinalIgnoreCase) || attributeValue == string.Empty)
            {
                result = new byte[0];
                return true;
            }
            result = null;
            if (attributeValue.Length % 2 != 0 || (isToken && attributeValue.Length != 8 * 2)) return false;
            byte[] pkt = new byte[attributeValue.Length / 2];
            for (int i = 0; i < pkt.Length; i++)
            {
                int hi = Hex(attributeValue[2 * i]), lo = Hex(attributeValue[2 * i + 1]);
                if (hi < 0 || lo < 0) return false;
                pkt[i] = (byte)(hi << 4 | lo);
            }
            result = pkt;
            return true;
        }

        private static ProcessorArchitecture TryParseProcessorArchitecture(string value)
        {
            if (value.Equals("msil", StringComparison.OrdinalIgnoreCase)) return ProcessorArchitecture.MSIL;
            if (value.Equals("x86", StringComparison.OrdinalIgnoreCase)) return ProcessorArchitecture.X86;
            if (value.Equals("ia64", StringComparison.OrdinalIgnoreCase)) return ProcessorArchitecture.IA64;
            if (value.Equals("amd64", StringComparison.OrdinalIgnoreCase)) return ProcessorArchitecture.Amd64;
            if (value.Equals("arm", StringComparison.OrdinalIgnoreCase)) return ProcessorArchitecture.Arm;
            return ProcessorArchitecture.None;
        }

        private static bool IsWhiteSpace(char ch) => ch == '\n' || ch == '\r' || ch == ' ' || ch == '\t';

        private bool TryGetNextChar(out char ch)
        {
            if (_index < _input.Length)
            {
                ch = _input[_index++];
                if (ch == '\0') return false;
            }
            else ch = '\0';
            return true;
        }

        /// <summary>The next token; a String token's text in <paramref name="tokenString"/>.</summary>
        private bool TryGetNextToken(out string tokenString, out Token token)
        {
            tokenString = string.Empty;
            char c;
            while (true)
            {
                if (!TryGetNextChar(out c)) { token = default; return false; }
                switch (c)
                {
                    case ',': token = Token.Comma; return true;
                    case '=': token = Token.Equals; return true;
                    case '\0': token = Token.End; return true;
                }
                if (!IsWhiteSpace(c)) break;
            }
            var sb = new StringBuilder();
            char quoteChar = '\0';
            if (c == '\'' || c == '"')
            {
                quoteChar = c;
                if (!TryGetNextChar(out c)) { token = default; return false; }
            }
            for (; ; )
            {
                if (c == 0)
                {
                    if (quoteChar != 0) { token = default; return false; } // unclosed quote
                    break;
                }
                if (quoteChar != 0 && c == quoteChar) break;
                if (quoteChar == 0 && (c == ',' || c == '=')) { _index--; break; }
                if (quoteChar == 0 && (c == '\'' || c == '"')) { token = default; return false; }
                if (c == '\\')
                {
                    if (!TryGetNextChar(out c)) { token = default; return false; }
                    switch (c)
                    {
                        case '\\': case ',': case '=': case '\'': case '"': sb.Append(c); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'n': sb.Append('\n'); break;
                        default: token = default; return false;
                    }
                }
                else sb.Append(c);
                if (!TryGetNextChar(out c)) { token = default; return false; }
            }
            int length = sb.Length;
            if (quoteChar == 0) while (length > 0 && IsWhiteSpace(sb[length - 1])) length--;
            tokenString = sb.ToString(0, length);
            token = Token.String;
            return true;
        }
    }
}
