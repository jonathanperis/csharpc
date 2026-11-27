// The shim's object identifiers, ASN.1 strings and integers, big numbers, name stacks and extensions over libcrypto: pal_asn1.c,
// pal_bignum.c, pal_x509_name.c and pal_x509ext.c (dotnet/runtime, .NET 11 GA). See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int OBJ_ln2nid(byte* s);
        [DllImport(LibCrypto)] private static extern int OBJ_sn2nid(byte* s);
        [DllImport(LibCrypto)] private static extern int OBJ_txt2nid(byte* s);
        [DllImport(LibCrypto)] private static extern IntPtr OBJ_nid2obj(int n);
        [DllImport(LibCrypto)] private static extern IntPtr ASN1_OCTET_STRING_new();
        [DllImport(LibCrypto)] private static extern int ASN1_OCTET_STRING_set(IntPtr s, byte* data, int len);
        [DllImport(LibCrypto)] private static extern void ASN1_OCTET_STRING_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern int i2d_ASN1_INTEGER(IntPtr a, byte** pp);
        [DllImport(LibCrypto)] private static extern void BN_clear_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern int BN_bn2bin(IntPtr a, byte* to);
        [DllImport(LibCrypto)] private static extern int BN_num_bits(IntPtr a);
        [DllImport(LibCrypto)] private static extern IntPtr X509_EXTENSION_create_by_OBJ(IntPtr ex, IntPtr obj, int crit, IntPtr data);
        [DllImport(LibCrypto)] private static extern void X509_EXTENSION_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern int X509V3_EXT_print(IntPtr outBio, IntPtr ext, ulong flag, int indent);

        [RuntimeExport("CryptoNative_ObjTxt2Obj", Trigger + "CryptoNative_ObjTxt2Obj")]
        public static IntPtr ObjTxt2Obj(byte* s)
        {
            ERR_clear_error();
            return OBJ_txt2obj(s, 1);
        }

        // OBJ_obj2txt takes the caller's length as its bound.
        [RuntimeExport("CryptoNative_ObjObj2Txt", Trigger + "CryptoNative_ObjObj2Txt")]
        public static int ObjObj2Txt(byte* buf, int bufLen, IntPtr a)
        {
            ERR_clear_error();
            return OBJ_obj2txt(buf, bufLen, a, 1);
        }

        [RuntimeExport("CryptoNative_GetObjectDefinitionByName", Trigger + "CryptoNative_GetObjectDefinitionByName")]
        public static IntPtr GetObjectDefinitionByName(byte* friendlyName)
        {
            ERR_clear_error();
            int nid = OBJ_ln2nid(friendlyName);
            if (nid == 0) nid = OBJ_sn2nid(friendlyName);
            return nid == 0 ? IntPtr.Zero : OBJ_nid2obj(nid);
        }

        [RuntimeExport("CryptoNative_ObjTxt2Nid", Trigger + "CryptoNative_ObjTxt2Nid")]
        public static int ObjTxt2Nid(byte* sn)
        {
            ERR_clear_error();
            return OBJ_txt2nid(sn);
        }

        [RuntimeExport("CryptoNative_ObjNid2Obj", Trigger + "CryptoNative_ObjNid2Obj")]
        public static IntPtr ObjNid2Obj(int nid)
        {
            ERR_clear_error();
            return OBJ_nid2obj(nid);
        }

        [RuntimeExport("CryptoNative_Asn1ObjectFree", Trigger + "CryptoNative_Asn1ObjectFree")]
        public static void Asn1ObjectFree(IntPtr a) => ASN1_OBJECT_free(a);

        [RuntimeExport("CryptoNative_Asn1OctetStringNew", Trigger + "CryptoNative_Asn1OctetStringNew")]
        public static IntPtr Asn1OctetStringNew()
        {
            ERR_clear_error();
            return ASN1_OCTET_STRING_new();
        }

        [RuntimeExport("CryptoNative_Asn1OctetStringSet", Trigger + "CryptoNative_Asn1OctetStringSet")]
        public static int Asn1OctetStringSet(IntPtr s, byte* data, int len)
        {
            ERR_clear_error();
            if (len < 0) return 0;
            return ASN1_OCTET_STRING_set(s, data, len);
        }

        [RuntimeExport("CryptoNative_Asn1OctetStringFree", Trigger + "CryptoNative_Asn1OctetStringFree")]
        public static void Asn1OctetStringFree(IntPtr a) => ASN1_OCTET_STRING_free(a);

        [RuntimeExport("CryptoNative_GetAsn1IntegerDerSize", Trigger + "CryptoNative_GetAsn1IntegerDerSize")]
        public static int GetAsn1IntegerDerSize(IntPtr i)
        {
            ERR_clear_error();
            return i2d_ASN1_INTEGER(i, null);
        }

        [RuntimeExport("CryptoNative_EncodeAsn1Integer", Trigger + "CryptoNative_EncodeAsn1Integer")]
        public static int EncodeAsn1Integer(IntPtr i, byte* buf)
        {
            ERR_clear_error();
            return i2d_ASN1_INTEGER(i, &buf);
        }

        [RuntimeExport("CryptoNative_BigNumDestroy", Trigger + "CryptoNative_BigNumDestroy")]
        public static void BigNumDestroy(IntPtr a)
        {
            if (a != IntPtr.Zero) BN_clear_free(a);
        }

        // The framework sizes `to` with GetBigNumBytes first.
        [RuntimeExport("CryptoNative_BigNumToBinary", Trigger + "CryptoNative_BigNumToBinary")]
        public static int BigNumToBinary(IntPtr a, byte* to)
        {
            if (a == IntPtr.Zero || to == null) return 0;
            ERR_clear_error();
            return BN_bn2bin(a, to);
        }

        // BN_num_bytes is a macro: (BN_num_bits(a) + 7) / 8.
        [RuntimeExport("CryptoNative_GetBigNumBytes", Trigger + "CryptoNative_GetBigNumBytes")]
        public static int GetBigNumBytes(IntPtr a) => a == IntPtr.Zero ? 0 : (BN_num_bits(a) + 7) / 8;

        [RuntimeExport("CryptoNative_GetX509NameStackFieldCount", Trigger + "CryptoNative_GetX509NameStackFieldCount")]
        public static int GetX509NameStackFieldCount(IntPtr sk) => OPENSSL_sk_num(sk);

        [RuntimeExport("CryptoNative_GetX509NameStackField", Trigger + "CryptoNative_GetX509NameStackField")]
        public static IntPtr GetX509NameStackField(IntPtr sk, int loc) => OPENSSL_sk_value(sk, loc);

        [RuntimeExport("CryptoNative_X509ExtensionCreateByObj", Trigger + "CryptoNative_X509ExtensionCreateByObj")]
        public static IntPtr X509ExtensionCreateByObj(IntPtr obj, int isCritical, IntPtr data)
        {
            ERR_clear_error();
            return X509_EXTENSION_create_by_OBJ(IntPtr.Zero, obj, isCritical, data);
        }

        [RuntimeExport("CryptoNative_X509ExtensionDestroy", Trigger + "CryptoNative_X509ExtensionDestroy")]
        public static void X509ExtensionDestroy(IntPtr a)
        {
            if (a != IntPtr.Zero) X509_EXTENSION_free(a);
        }

        [RuntimeExport("CryptoNative_X509V3ExtPrint", Trigger + "CryptoNative_X509V3ExtPrint")]
        public static int X509V3ExtPrint(IntPtr outBio, IntPtr ext)
        {
            ERR_clear_error();
            return X509V3_EXT_print(outBio, ext, 0 /* X509V3_EXT_DEFAULT */, 0);
        }
    }
}
#endif
