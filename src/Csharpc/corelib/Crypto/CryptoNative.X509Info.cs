// The shim's certificate accessors, name lookups, host checks and stacks over libcrypto: the X.509 part of openssl.c (dotnet/runtime,
// .NET 11 GA), function by function. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int X509_digest(IntPtr data, IntPtr type, byte* md, uint* len);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get0_notBefore(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get0_notAfter(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr X509_CRL_get0_nextUpdate(IntPtr crl);
        [DllImport(LibCrypto)] private static extern long X509_get_version(IntPtr x);
        [DllImport(LibCrypto)] private static extern int X509_PUBKEY_get0_param(IntPtr* ppkalg, byte** pk, int* ppklen, IntPtr* pa, IntPtr pub);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get0_tbs_sigalg(IntPtr x);
        [DllImport(LibCrypto)] private static extern int i2d_ASN1_TYPE(IntPtr a, byte** pp);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get0_pubkey_bitstr(IntPtr x);
        [DllImport(LibCrypto)] private static extern int ASN1_STRING_length(IntPtr x);
        [DllImport(LibCrypto)] private static extern byte* ASN1_STRING_get0_data(IntPtr x);
        [DllImport(LibCrypto)] private static extern int X509_NAME_get0_der(IntPtr nm, byte** pder, nuint* pderlen);
        [DllImport(LibCrypto)] private static extern int X509_NAME_entry_count(IntPtr name);
        [DllImport(LibCrypto)] private static extern IntPtr X509_NAME_get_entry(IntPtr name, int loc);
        [DllImport(LibCrypto)] private static extern int X509_NAME_get_index_by_NID(IntPtr name, int nid, int lastpos);
        [DllImport(LibCrypto)] private static extern IntPtr X509_NAME_ENTRY_get_object(IntPtr ne);
        [DllImport(LibCrypto)] private static extern IntPtr X509_NAME_ENTRY_get_data(IntPtr ne);
        [DllImport(LibCrypto)] private static extern int OBJ_obj2nid(IntPtr o);
        [DllImport(LibCrypto)] private static extern int OBJ_obj2txt(byte* buf, int bufLen, IntPtr a, int noName);
        [DllImport(LibCrypto)] private static extern IntPtr OBJ_txt2obj(byte* s, int noName);
        [DllImport(LibCrypto)] private static extern byte* OBJ_nid2ln(int n);
        [DllImport(LibCrypto)] private static extern void ASN1_OBJECT_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern int a2d_ASN1_OBJECT(byte* outBuf, int olen, byte* buf, int num);
        [DllImport(LibCrypto)] internal static extern IntPtr BIO_new(IntPtr type);
        [DllImport(LibCrypto)] internal static extern IntPtr BIO_s_mem();
        [DllImport(LibCrypto)] private static extern int ASN1_STRING_print_ex(IntPtr outBio, IntPtr str, ulong flags);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_ext_d2i(IntPtr x, int nid, int* crit, int* idx);
        [DllImport(LibCrypto)] private static extern void GENERAL_NAMES_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern int X509_check_host(IntPtr x, byte* chk, nuint chklen, uint flags, byte** peername);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_X509_bio(IntPtr bp, IntPtr x509);
        [DllImport(LibCrypto)] internal static extern long BIO_ctrl(IntPtr bp, int cmd, long larg, void* parg);
        [DllImport(LibCrypto)] private static extern void OPENSSL_sk_pop_free(IntPtr st, IntPtr func);
        [DllImport("libc")] private static extern int strncasecmp(byte* a, byte* b, nuint n);

        private const int NAME_TYPE_SIMPLE = 0, NAME_TYPE_EMAIL = 1, NAME_TYPE_UPN = 2, NAME_TYPE_DNS = 3, NAME_TYPE_DNSALT = 4, NAME_TYPE_URL = 5;
        private const int NID_commonName = 13, NID_organizationName = 17, NID_organizationalUnitName = 18, NID_pkcs9_emailAddress = 48,
            NID_subject_alt_name = 85, NID_issuer_alt_name = 86;
        private const int GEN_OTHERNAME = 0, GEN_EMAIL = 1, GEN_DNS = 2, GEN_URI = 6, GEN_IPADD = 7, V_ASN1_UTF8STRING = 12;
        private const ulong ASN1_STRFLGS_UTF8_CONVERT = 0x10;

        [RuntimeExport("CryptoNative_GetX509Thumbprint", Trigger + "CryptoNative_GetX509Thumbprint")]
        public static int GetX509Thumbprint(IntPtr x509, byte* pBuf, int cBuf)
        {
            if (x509 == IntPtr.Zero) return 0;
            if (cBuf < 20) return -20; // SHA_DIGEST_LENGTH
            ERR_clear_error();
            return X509_digest(x509, EVP_sha1(), pBuf, null) == 0 ? 0 : 1;
        }

        [RuntimeExport("CryptoNative_GetX509NotBefore", Trigger + "CryptoNative_GetX509NotBefore")]
        public static IntPtr GetX509NotBefore(IntPtr x509) => x509 != IntPtr.Zero ? X509_get0_notBefore(x509) : IntPtr.Zero;

        [RuntimeExport("CryptoNative_GetX509NotAfter", Trigger + "CryptoNative_GetX509NotAfter")]
        public static IntPtr GetX509NotAfter(IntPtr x509) => x509 != IntPtr.Zero ? X509_get0_notAfter(x509) : IntPtr.Zero;

        [RuntimeExport("CryptoNative_GetX509CrlNextUpdate", Trigger + "CryptoNative_GetX509CrlNextUpdate")]
        public static IntPtr GetX509CrlNextUpdate(IntPtr crl) => crl != IntPtr.Zero ? X509_CRL_get0_nextUpdate(crl) : IntPtr.Zero;

        [RuntimeExport("CryptoNative_GetX509Version", Trigger + "CryptoNative_GetX509Version")]
        public static int GetX509Version(IntPtr x509) => x509 != IntPtr.Zero ? (int)X509_get_version(x509) : -1;

        [RuntimeExport("CryptoNative_GetX509PublicKeyAlgorithm", Trigger + "CryptoNative_GetX509PublicKeyAlgorithm")]
        public static IntPtr GetX509PublicKeyAlgorithm(IntPtr x509)
        {
            if (x509 == IntPtr.Zero) return IntPtr.Zero;
            IntPtr pubkey = X509_get_X509_PUBKEY(x509);
            IntPtr algOid;
            if (pubkey != IntPtr.Zero && X509_PUBKEY_get0_param(&algOid, null, null, null, pubkey) != 0) return algOid;
            return IntPtr.Zero;
        }

        [RuntimeExport("CryptoNative_GetX509SignatureAlgorithm", Trigger + "CryptoNative_GetX509SignatureAlgorithm")]
        public static IntPtr GetX509SignatureAlgorithm(IntPtr x509)
        {
            if (x509 == IntPtr.Zero) return IntPtr.Zero;
            IntPtr sigAlg = X509_get0_tbs_sigalg(x509);
            // X509_ALGOR: { ASN1_OBJECT* algorithm; ASN1_TYPE* parameter; }.
            return sigAlg != IntPtr.Zero ? *(IntPtr*)sigAlg : IntPtr.Zero;
        }

        [RuntimeExport("CryptoNative_GetX509PublicKeyParameterBytes", Trigger + "CryptoNative_GetX509PublicKeyParameterBytes")]
        public static int GetX509PublicKeyParameterBytes(IntPtr x509, byte* pBuf, int cBuf)
        {
            ERR_clear_error();
            if (x509 == IntPtr.Zero) return 0;
            IntPtr pubkey = X509_get_X509_PUBKEY(x509);
            if (pubkey == IntPtr.Zero) return 0;
            IntPtr alg;
            if (X509_PUBKEY_get0_param(null, null, null, &alg, pubkey) == 0 || alg == IntPtr.Zero) return 0;
            IntPtr parameter = ((IntPtr*)alg)[1];
            if (parameter == IntPtr.Zero) return 2;
            int len = i2d_ASN1_TYPE(parameter, null);
            if (cBuf < len || pBuf == null) return -len;
            byte* p = pBuf;
            return i2d_ASN1_TYPE(parameter, &p) > 0 ? 1 : 0;
        }

        [RuntimeExport("CryptoNative_GetX509PublicKeyBytes", Trigger + "CryptoNative_GetX509PublicKeyBytes")]
        public static IntPtr GetX509PublicKeyBytes(IntPtr x509) => x509 != IntPtr.Zero ? X509_get0_pubkey_bitstr(x509) : IntPtr.Zero;

        [RuntimeExport("CryptoNative_GetAsn1StringBytes", Trigger + "CryptoNative_GetAsn1StringBytes")]
        public static int GetAsn1StringBytes(IntPtr asn1, byte* pBuf, int cBuf)
        {
            if (asn1 == IntPtr.Zero || cBuf < 0) return 0;
            int length = ASN1_STRING_length(asn1);
            if (length < 0) return 0;
            if (pBuf == null || cBuf < length) return -length;
            Buffer.MemoryCopy(ASN1_STRING_get0_data(asn1), pBuf, cBuf, length);
            return 1;
        }

        [RuntimeExport("CryptoNative_GetX509NameRawBytes", Trigger + "CryptoNative_GetX509NameRawBytes")]
        public static int GetX509NameRawBytes(IntPtr x509Name, byte* pBuf, int cBuf)
        {
            ERR_clear_error();
            byte* nameBuf;
            nuint nameBufLen;
            if (x509Name == IntPtr.Zero || cBuf < 0 || X509_NAME_get0_der(x509Name, &nameBuf, &nameBufLen) == 0) return 0;
            if (nameBufLen > int.MaxValue) return 0;
            int length = (int)nameBufLen;
            if (pBuf == null || cBuf < length) return -length;
            Buffer.MemoryCopy(nameBuf, pBuf, cBuf, length);
            return 1;
        }

        private static IntPtr PrintToMemoryBio(IntPtr str, ulong flags)
        {
            IntPtr b = BIO_new(BIO_s_mem());
            ASN1_STRING_print_ex(b, str, flags);
            return b;
        }

        // X509Certificate2.GetNameInfo, entirely: the first matching name, printed into a memory BIO the framework reads.
        [RuntimeExport("CryptoNative_GetX509NameInfo", Trigger + "CryptoNative_GetX509NameInfo")]
        public static IntPtr GetX509NameInfo(IntPtr x509, int nameType, int forIssuer)
        {
            ERR_clear_error();
            if (x509 == IntPtr.Zero || nameType < NAME_TYPE_SIMPLE || nameType > NAME_TYPE_URL) return IntPtr.Zero;
            if (nameType == NAME_TYPE_SIMPLE)
            {
                IntPtr name = forIssuer != 0 ? X509_get_issuer_name(x509) : X509_get_subject_name(x509);
                if (name != IntPtr.Zero)
                {
                    IntPtr cn = IntPtr.Zero, ou = IntPtr.Zero, o = IntPtr.Zero, e = IntPtr.Zero, firstRdn = IntPtr.Zero;
                    // The last RDN of each kind, as Windows' CertGetNameString answers.
                    for (int i = X509_NAME_entry_count(name) - 1; i >= 0; --i)
                    {
                        IntPtr entry = X509_NAME_get_entry(name, i);
                        if (entry == IntPtr.Zero) continue;
                        IntPtr oid = X509_NAME_ENTRY_get_object(entry), str = X509_NAME_ENTRY_get_data(entry);
                        if (oid == IntPtr.Zero || str == IntPtr.Zero) continue;
                        int nid = OBJ_obj2nid(oid);
                        if (nid == NID_commonName) { cn = str; break; }
                        else if (nid == NID_organizationalUnitName) ou = str;
                        else if (nid == NID_organizationName) o = str;
                        else if (nid == NID_pkcs9_emailAddress) e = str;
                        else if (firstRdn == IntPtr.Zero) firstRdn = str;
                    }
                    IntPtr answer = cn;
                    if (answer == IntPtr.Zero && firstRdn != IntPtr.Zero)
                    {
                        answer = ou;
                        if (answer == IntPtr.Zero) answer = o;
                        if (answer == IntPtr.Zero) answer = e;
                        if (answer == IntPtr.Zero) answer = firstRdn;
                    }
                    if (answer != IntPtr.Zero) return PrintToMemoryBio(answer, ASN1_STRFLGS_UTF8_CONVERT);
                }
            }
            int expectedType = nameType switch
            {
                NAME_TYPE_DNS or NAME_TYPE_DNSALT => GEN_DNS,
                NAME_TYPE_SIMPLE or NAME_TYPE_EMAIL => GEN_EMAIL,
                NAME_TYPE_UPN => GEN_OTHERNAME,
                _ => GEN_URI,
            };
            IntPtr altNames = X509_get_ext_d2i(x509, forIssuer != 0 ? NID_issuer_alt_name : NID_subject_alt_name, null, null);
            if (altNames != IntPtr.Zero)
            {
                int count = OPENSSL_sk_num(altNames);
                for (int i = 0; i < count; ++i)
                {
                    IntPtr altName = OPENSSL_sk_value(altNames, i);
                    // GENERAL_NAME: { int type; union { ... } d; }, d at offset 8.
                    if (altName == IntPtr.Zero || *(int*)altName != expectedType) continue;
                    IntPtr d = *(IntPtr*)((byte*)altName + 8);
                    IntPtr str = IntPtr.Zero;
                    if (nameType == NAME_TYPE_UPN)
                    {
                        // OTHERNAME: { ASN1_OBJECT* type_id; ASN1_TYPE* value; }; ASN1_TYPE: { int type; union value; }.
                        if (d != IntPtr.Zero)
                        {
                            byte* localOid = stackalloc byte[26];
                            int cchLocalOid = 1 + OBJ_obj2txt(localOid, 26, *(IntPtr*)d, 1);
                            ReadOnlySpan<byte> upn = "1.3.6.1.4.1.311.20.2.3\0"u8;
                            if (cchLocalOid == upn.Length && new ReadOnlySpan<byte>(localOid, upn.Length).SequenceEqual(upn))
                            {
                                IntPtr value = ((IntPtr*)d)[1];
                                if (value != IntPtr.Zero && *(int*)value == V_ASN1_UTF8STRING) str = *(IntPtr*)((byte*)value + 8);
                            }
                        }
                    }
                    else
                    {
                        str = d;
                    }
                    if (str != IntPtr.Zero)
                    {
                        IntPtr b = PrintToMemoryBio(str, ASN1_STRFLGS_UTF8_CONVERT);
                        GENERAL_NAMES_free(altNames);
                        return b;
                    }
                }
                GENERAL_NAMES_free(altNames);
            }
            if (nameType == NAME_TYPE_EMAIL || nameType == NAME_TYPE_DNS)
            {
                IntPtr name = forIssuer != 0 ? X509_get_issuer_name(x509) : X509_get_subject_name(x509);
                int expectedNid = nameType == NAME_TYPE_EMAIL ? NID_pkcs9_emailAddress : NID_commonName;
                if (name != IntPtr.Zero)
                    for (int i = X509_NAME_entry_count(name) - 1; i >= 0; --i)
                    {
                        IntPtr entry = X509_NAME_get_entry(name, i);
                        if (entry == IntPtr.Zero) continue;
                        IntPtr oid = X509_NAME_ENTRY_get_object(entry), str = X509_NAME_ENTRY_get_data(entry);
                        if (oid == IntPtr.Zero || str == IntPtr.Zero) continue;
                        if (OBJ_obj2nid(oid) == expectedNid) return PrintToMemoryBio(str, 0);
                    }
            }
            return IntPtr.Zero;
        }

        [RuntimeExport("CryptoNative_CheckX509Hostname", Trigger + "CryptoNative_CheckX509Hostname")]
        public static int CheckX509Hostname(IntPtr x509, byte* hostname, int cchHostname)
        {
            if (x509 == IntPtr.Zero) return -3;
            if (cchHostname > 0 && hostname == null) return -4;
            if (cchHostname < 0) return -5;
            ERR_clear_error();
            // A leading dot would make OpenSSL match any subdomain: never a match for a host name, as there.
            if (cchHostname > 0 && hostname[0] == (byte)'.') return 0;
            return X509_check_host(x509, hostname, (nuint)cchHostname, 0x4 /* X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS */, null);
        }

        [RuntimeExport("CryptoNative_CheckX509IpAddress", Trigger + "CryptoNative_CheckX509IpAddress")]
        public static int CheckX509IpAddress(IntPtr x509, byte* addressBytes, int addressBytesLen, byte* hostname, int cchHostname)
        {
            if (x509 == IntPtr.Zero) return -2;
            if (cchHostname > 0 && hostname == null) return -3;
            if (cchHostname < 0) return -4;
            if (addressBytesLen < 0) return -5;
            if (addressBytes == null) return -6;
            ERR_clear_error();
            int success = 0;
            IntPtr san = X509_get_ext_d2i(x509, NID_subject_alt_name, null, null);
            if (san != IntPtr.Zero)
            {
                int count = OPENSSL_sk_num(san);
                for (int i = 0; i < count; ++i)
                {
                    IntPtr entry = OPENSSL_sk_value(san, i);
                    if (*(int*)entry != GEN_IPADD) continue;
                    IntPtr ipAddr = *(IntPtr*)((byte*)entry + 8);
                    if (ipAddr == IntPtr.Zero || ASN1_STRING_length(ipAddr) != addressBytesLen) continue;
                    byte* data = ASN1_STRING_get0_data(ipAddr);
                    if (data == null) continue;
                    if (new ReadOnlySpan<byte>(addressBytes, addressBytesLen).SequenceEqual(new ReadOnlySpan<byte>(data, addressBytesLen)))
                    {
                        success = 1;
                        break;
                    }
                }
                GENERAL_NAMES_free(san);
            }
            if (success == 0)
            {
                // The subject's common names, compared case-insensitively with the address's text, as the shim does.
                IntPtr subject = X509_get_subject_name(x509);
                if (subject != IntPtr.Zero)
                {
                    int i = -1;
                    while ((i = X509_NAME_get_index_by_NID(subject, NID_commonName, i)) >= 0)
                    {
                        IntPtr cn = X509_NAME_ENTRY_get_data(X509_NAME_get_entry(subject, i));
                        byte* data = ASN1_STRING_get0_data(cn);
                        if (ASN1_STRING_length(cn) == cchHostname && data != null && strncasecmp(data, hostname, (nuint)cchHostname) == 0)
                        {
                            success = 1;
                            break;
                        }
                    }
                }
            }
            return success;
        }

        [RuntimeExport("CryptoNative_GetX509StackFieldCount", Trigger + "CryptoNative_GetX509StackFieldCount")]
        public static int GetX509StackFieldCount(IntPtr stack) => OPENSSL_sk_num(stack);

        [RuntimeExport("CryptoNative_GetX509StackField", Trigger + "CryptoNative_GetX509StackField")]
        public static IntPtr GetX509StackField(IntPtr stack, int loc) => OPENSSL_sk_value(stack, loc);

        [RuntimeExport("CryptoNative_RecursiveFreeX509Stack", Trigger + "CryptoNative_RecursiveFreeX509Stack")]
        public static void RecursiveFreeX509Stack(IntPtr stack)
        {
            if (stack == IntPtr.Zero) return;
            IntPtr cur;
            while ((cur = OPENSSL_sk_pop(stack)) != IntPtr.Zero) X509_free(cur);
            OPENSSL_sk_free(stack);
        }

        [RuntimeExport("CryptoNative_ReadX509AsDerFromBio", Trigger + "CryptoNative_ReadX509AsDerFromBio")]
        public static IntPtr ReadX509AsDerFromBio(IntPtr bio)
        {
            ERR_clear_error();
            return d2i_X509_bio(bio, IntPtr.Zero);
        }

        [RuntimeExport("CryptoNative_BioTell", Trigger + "CryptoNative_BioTell")]
        public static int BioTell(IntPtr bio) => bio == IntPtr.Zero ? -1 : (int)BIO_ctrl(bio, 133 /* BIO_C_FILE_TELL */, 0, null);

        [RuntimeExport("CryptoNative_BioSeek", Trigger + "CryptoNative_BioSeek")]
        public static int BioSeek(IntPtr bio, int ofs) => bio == IntPtr.Zero ? -1 : (int)BIO_ctrl(bio, 128 /* BIO_C_FILE_SEEK */, ofs, null);

        [RuntimeExport("CryptoNative_NewX509Stack", Trigger + "CryptoNative_NewX509Stack")]
        public static IntPtr NewX509Stack()
        {
            ERR_clear_error();
            return OPENSSL_sk_new_null();
        }

        [RuntimeExport("CryptoNative_PushX509StackField", Trigger + "CryptoNative_PushX509StackField")]
        public static int PushX509StackField(IntPtr stack, IntPtr x509)
        {
            ERR_clear_error();
            return stack == IntPtr.Zero ? 0 : OPENSSL_sk_push(stack, x509);
        }

        [RuntimeExport("CryptoNative_LookupFriendlyNameByOid", Trigger + "CryptoNative_LookupFriendlyNameByOid")]
        public static int LookupFriendlyNameByOid(byte* oidValue, byte** friendlyName)
        {
            ERR_clear_error();
            if (oidValue == null || friendlyName == null) return -2;
            // A syntactically invalid OID is "not found", not an error.
            if (a2d_ASN1_OBJECT(null, 0, oidValue, -1) <= 0) return 0;
            IntPtr oid = OBJ_txt2obj(oidValue, 1);
            if (oid == IntPtr.Zero) return -1;
            int nid = OBJ_obj2nid(oid);
            ASN1_OBJECT_free(oid);
            if (nid == 0) return 0;
            byte* ln = OBJ_nid2ln(nid);
            if (ln == null) return 0;
            *friendlyName = ln;
            return 1;
        }
    }
}
#endif
