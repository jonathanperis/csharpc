// libSystem.Net.Security.Native's C API (pal_gssapi.c, dotnet/runtime .NET 11 GA), which the framework's System.Net.Security IL P/Invokes on
// Linux for Negotiate, Kerberos and NTLM (NegotiateAuthentication, NegotiateStream, HttpClient's Negotiate/NTLM handler), served by csharpc in C#
// over MIT krb5's libgssapi_krb5 instead of the pack's shim: the same contract as corelib/Crypto/CryptoNative.cs, bound by name through
// [RuntimeExport] (ModuleCodeGen.PInvoke).
//
// As the portable shim (GSS_SHIM), the GSS library is loaded at run time: NetSecurityNative_EnsureGssInitialized loads libgssapi_krb5.so.2 and
// answers -1 when it is absent, and the framework reports that the way .NET does; a library that lacks one of the functions ends the process
// with the shim's message (it aborts there too). The buffers GSS returns move to the framework as they are (PAL_GssBuffer) and go back through
// NetSecurityNative_ReleaseGssBuffer. Security: every input buffer is passed with the length the caller gave, negative lengths of channel
// bindings are refused before they become a size_t (GSS_S_BAD_BINDINGS, as the shim), and nothing here parses a token (GSS does).
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class NetSecurityNative
    {
        private const string Trigger = "libSystem.Net.Security.Native!";
        private const string GssLibName = "libgssapi_krb5.so.2";

        // gss_buffer_desc, gss_OID_desc, gss_OID_set_desc and gss_channel_bindings_struct (gssapi.h).
        [StructLayout(LayoutKind.Sequential)]
        private struct GssBufferDesc
        {
            public nuint Length;
            public void* Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GssOidDesc
        {
            public uint Length;
            public void* Elements;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GssOidSetDesc
        {
            public nuint Count;
            public GssOidDesc* Elements;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GssChannelBindings
        {
            public uint InitiatorAddrType;
            public GssBufferDesc InitiatorAddress;
            public uint AcceptorAddrType;
            public GssBufferDesc AcceptorAddress;
            public GssBufferDesc ApplicationData;
        }

        // PAL_GssBuffer (pal_gssapi.h).
        [StructLayout(LayoutKind.Sequential)]
        public struct PalGssBuffer
        {
            public ulong Length;
            public byte* Data;
        }

        private const int GSS_C_INITIATE = 1, GSS_C_ACCEPT = 2, GSS_C_GSS_CODE = 1, GSS_C_MECH_CODE = 2;
        private const uint GSS_C_INDEFINITE = 0xFFFFFFFF, GSS_C_QOP_DEFAULT = 0, GSS_S_COMPLETE = 0, GSS_S_BAD_NAME = 2u << 16,
            GSS_S_BAD_BINDINGS = 4u << 16, GSS_C_IDENTIFY_FLAG = 0x2000;
        private const uint PAL_GSS_NTLM = 1, PAL_GSS_KERBEROS = 2;

        // MIT krb5 has no GSS_SPNEGO_MECHANISM: the shim's own SPNEGO (RFC 4178), NTLM and krb5 principal-name OIDs.
        private static readonly byte* s_spnegoOid = Bytes(new byte[] { 0x2b, 0x06, 0x01, 0x05, 0x05, 0x02 });
        private static readonly byte* s_ntlmOid = Bytes(new byte[] { 0x2b, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x02, 0x02, 0x0a });
        private static readonly byte* s_krb5PrincipalNameOid = Bytes(new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x12, 0x01, 0x02, 0x02, 0x01 });
        private static GssOidDesc* s_spnegoOidDesc, s_ntlmOidDesc, s_krb5PrincipalNameOidDesc;

        private static byte* Bytes(byte[] value)
        {
            var p = (byte*)NativeMemory.Alloc((nuint)value.Length);
            for (int i = 0; i < value.Length; i++) p[i] = value[i];
            return p;
        }

        private static GssOidDesc* Oid(byte* elements, uint length)
        {
            var d = (GssOidDesc*)NativeMemory.Alloc((nuint)sizeof(GssOidDesc));
            d->Length = length;
            d->Elements = elements;
            return d;
        }

        // ---- the GSS functions, looked up once (the shim's FOR_ALL_GSS_FUNCTIONS) -------------------------------------------------
        private static IntPtr s_gssLib;
        private static delegate* unmanaged<uint*, void**, void*, GssBufferDesc*, GssChannelBindings*, void**, GssOidDesc**, GssBufferDesc*, uint*, uint*, void**, uint> gss_accept_sec_context;
        private static delegate* unmanaged<uint*, void*, uint, GssOidSetDesc*, int, void**, GssOidSetDesc**, uint*, uint> gss_acquire_cred;
        private static delegate* unmanaged<uint*, void*, GssBufferDesc*, uint, GssOidSetDesc*, int, void**, GssOidSetDesc**, uint*, uint> gss_acquire_cred_with_password;
        private static delegate* unmanaged<uint*, void**, GssBufferDesc*, uint> gss_delete_sec_context;
        private static delegate* unmanaged<uint*, void*, GssBufferDesc*, GssOidDesc**, uint> gss_display_name;
        private static delegate* unmanaged<uint*, uint, int, GssOidDesc*, uint*, GssBufferDesc*, uint> gss_display_status;
        private static delegate* unmanaged<uint*, GssBufferDesc*, GssOidDesc*, void**, uint> gss_import_name;
        private static delegate* unmanaged<uint*, GssOidSetDesc**, uint> gss_indicate_mechs;
        private static delegate* unmanaged<uint*, void*, void**, void*, GssOidDesc*, uint, uint, GssChannelBindings*, GssBufferDesc*, GssOidDesc**, GssBufferDesc*, uint*, uint*, uint> gss_init_sec_context;
        private static delegate* unmanaged<uint*, void*, void**, void**, uint*, GssOidDesc**, uint*, int*, int*, uint> gss_inquire_context;
        private static delegate* unmanaged<GssOidDesc*, GssOidDesc*, int> gss_oid_equal;
        private static delegate* unmanaged<uint*, GssBufferDesc*, uint> gss_release_buffer;
        private static delegate* unmanaged<uint*, void**, uint> gss_release_cred;
        private static delegate* unmanaged<uint*, void**, uint> gss_release_name;
        private static delegate* unmanaged<uint*, GssOidSetDesc**, uint> gss_release_oid_set;
        private static delegate* unmanaged<uint*, void*, GssBufferDesc*, GssBufferDesc*, int*, uint*, uint> gss_unwrap;
        private static delegate* unmanaged<uint*, void*, int, uint, GssBufferDesc*, int*, GssBufferDesc*, uint> gss_wrap;
        private static delegate* unmanaged<uint*, void*, uint, GssBufferDesc*, GssBufferDesc*, uint> gss_get_mic;
        private static delegate* unmanaged<uint*, void*, GssBufferDesc*, GssBufferDesc*, uint*, uint> gss_verify_mic;
        // Data symbols: each holds a gss_OID.
        private static GssOidDesc* GSS_C_NT_USER_NAME, GSS_C_NT_HOSTBASED_SERVICE, gss_mech_krb5;

        [DllImport("libc")] private static extern void abort();
        [DllImport("libc")] private static extern byte* dlerror();

        private static readonly object s_initLock = new object();

        private static void* Symbol(string name)
        {
            if (NativeLibrary.TryGetExport(s_gssLib, name, out IntPtr address)) return (void*)address;
            // The shim: fprintf(stderr, "Cannot get symbol X from lib \nError: ...") and abort().
            byte* error = dlerror();
            Console.Error.Write("Cannot get symbol " + name + " from " + GssLibName + " \nError: "
                + (error == null ? "(null)" : Marshal.PtrToStringUTF8((IntPtr)error)) + "\n");
            Console.Error.Flush();
            abort();
            return null;
        }

        [RuntimeExport("NetSecurityNative_EnsureGssInitialized", Trigger + "NetSecurityNative_EnsureGssInitialized")]
        public static int EnsureGssInitialized()
        {
            lock (s_initLock)
            {
                if (s_gssLib != IntPtr.Zero) return 0;
                if (!NativeLibrary.TryLoad(GssLibName, out IntPtr lib)) return -1;
                s_gssLib = lib;
                gss_accept_sec_context = (delegate* unmanaged<uint*, void**, void*, GssBufferDesc*, GssChannelBindings*, void**, GssOidDesc**, GssBufferDesc*, uint*, uint*, void**, uint>)Symbol("gss_accept_sec_context");
                gss_acquire_cred = (delegate* unmanaged<uint*, void*, uint, GssOidSetDesc*, int, void**, GssOidSetDesc**, uint*, uint>)Symbol("gss_acquire_cred");
                gss_acquire_cred_with_password = (delegate* unmanaged<uint*, void*, GssBufferDesc*, uint, GssOidSetDesc*, int, void**, GssOidSetDesc**, uint*, uint>)Symbol("gss_acquire_cred_with_password");
                gss_delete_sec_context = (delegate* unmanaged<uint*, void**, GssBufferDesc*, uint>)Symbol("gss_delete_sec_context");
                gss_display_name = (delegate* unmanaged<uint*, void*, GssBufferDesc*, GssOidDesc**, uint>)Symbol("gss_display_name");
                gss_display_status = (delegate* unmanaged<uint*, uint, int, GssOidDesc*, uint*, GssBufferDesc*, uint>)Symbol("gss_display_status");
                gss_import_name = (delegate* unmanaged<uint*, GssBufferDesc*, GssOidDesc*, void**, uint>)Symbol("gss_import_name");
                gss_indicate_mechs = (delegate* unmanaged<uint*, GssOidSetDesc**, uint>)Symbol("gss_indicate_mechs");
                gss_init_sec_context = (delegate* unmanaged<uint*, void*, void**, void*, GssOidDesc*, uint, uint, GssChannelBindings*, GssBufferDesc*, GssOidDesc**, GssBufferDesc*, uint*, uint*, uint>)Symbol("gss_init_sec_context");
                gss_inquire_context = (delegate* unmanaged<uint*, void*, void**, void**, uint*, GssOidDesc**, uint*, int*, int*, uint>)Symbol("gss_inquire_context");
                gss_mech_krb5 = *(GssOidDesc**)Symbol("gss_mech_krb5");
                gss_oid_equal = (delegate* unmanaged<GssOidDesc*, GssOidDesc*, int>)Symbol("gss_oid_equal");
                gss_release_buffer = (delegate* unmanaged<uint*, GssBufferDesc*, uint>)Symbol("gss_release_buffer");
                gss_release_cred = (delegate* unmanaged<uint*, void**, uint>)Symbol("gss_release_cred");
                gss_release_name = (delegate* unmanaged<uint*, void**, uint>)Symbol("gss_release_name");
                gss_release_oid_set = (delegate* unmanaged<uint*, GssOidSetDesc**, uint>)Symbol("gss_release_oid_set");
                gss_unwrap = (delegate* unmanaged<uint*, void*, GssBufferDesc*, GssBufferDesc*, int*, uint*, uint>)Symbol("gss_unwrap");
                gss_wrap = (delegate* unmanaged<uint*, void*, int, uint, GssBufferDesc*, int*, GssBufferDesc*, uint>)Symbol("gss_wrap");
                gss_get_mic = (delegate* unmanaged<uint*, void*, uint, GssBufferDesc*, GssBufferDesc*, uint>)Symbol("gss_get_mic");
                gss_verify_mic = (delegate* unmanaged<uint*, void*, GssBufferDesc*, GssBufferDesc*, uint*, uint>)Symbol("gss_verify_mic");
                GSS_C_NT_USER_NAME = *(GssOidDesc**)Symbol("GSS_C_NT_USER_NAME");
                GSS_C_NT_HOSTBASED_SERVICE = *(GssOidDesc**)Symbol("GSS_C_NT_HOSTBASED_SERVICE");
                s_spnegoOidDesc = Oid(s_spnegoOid, 6);
                s_ntlmOidDesc = Oid(s_ntlmOid, 10);
                s_krb5PrincipalNameOidDesc = Oid(s_krb5PrincipalNameOid, 10);
                return 0;
            }
        }

        // Ownership of GSS's buffer moves to the framework, which hands it back to ReleaseGssBuffer.
        private static void MoveBuffer(GssBufferDesc* gssBuffer, PalGssBuffer* target)
        {
            target->Length = gssBuffer->Length;
            target->Data = (byte*)gssBuffer->Value;
        }

        private static GssOidDesc* MechFor(uint packageType) =>
            packageType == PAL_GSS_NTLM ? s_ntlmOidDesc : packageType == PAL_GSS_KERBEROS ? gss_mech_krb5 : s_spnegoOidDesc;

        [RuntimeExport("NetSecurityNative_ReleaseGssBuffer", Trigger + "NetSecurityNative_ReleaseGssBuffer")]
        public static void ReleaseGssBuffer(void* buffer, ulong length)
        {
            uint minorStatus;
            var gssBuffer = new GssBufferDesc { Length = (nuint)length, Value = buffer };
            gss_release_buffer(&minorStatus, &gssBuffer);
        }

        private static uint DisplayStatus(uint* minorStatus, uint statusValue, int statusType, PalGssBuffer* outBuffer)
        {
            uint messageContext = 0;
            var gssBuffer = new GssBufferDesc();
            uint majorStatus = gss_display_status(minorStatus, statusValue, statusType, null, &messageContext, &gssBuffer);
            MoveBuffer(&gssBuffer, outBuffer);
            return majorStatus;
        }

        [RuntimeExport("NetSecurityNative_DisplayMinorStatus", Trigger + "NetSecurityNative_DisplayMinorStatus")]
        public static uint DisplayMinorStatus(uint* minorStatus, uint statusValue, PalGssBuffer* outBuffer) =>
            DisplayStatus(minorStatus, statusValue, GSS_C_MECH_CODE, outBuffer);

        [RuntimeExport("NetSecurityNative_DisplayMajorStatus", Trigger + "NetSecurityNative_DisplayMajorStatus")]
        public static uint DisplayMajorStatus(uint* minorStatus, uint statusValue, PalGssBuffer* outBuffer) =>
            DisplayStatus(minorStatus, statusValue, GSS_C_GSS_CODE, outBuffer);

        [RuntimeExport("NetSecurityNative_ImportUserName", Trigger + "NetSecurityNative_ImportUserName")]
        public static uint ImportUserName(uint* minorStatus, byte* inputName, uint inputNameLen, void** outputName)
        {
            var inputNameBuffer = new GssBufferDesc { Length = inputNameLen, Value = inputName };
            return gss_import_name(minorStatus, &inputNameBuffer, GSS_C_NT_USER_NAME, outputName);
        }

        [RuntimeExport("NetSecurityNative_ImportPrincipalName", Trigger + "NetSecurityNative_ImportPrincipalName")]
        public static uint ImportPrincipalName(uint* minorStatus, byte* inputName, uint inputNameLen, void** outputName)
        {
            // SERVICE/HOST becomes GSS_C_NT_HOSTBASED_SERVICE's SERVICE@HOST; SERVICE/HOST@REALM is a full Kerberos principal, imported as is.
            long slash = -1, at = -1;
            for (uint i = 0; i < inputNameLen; i++)
            {
                if (inputName[i] == (byte)'/' && slash < 0) slash = i;
                if (inputName[i] == (byte)'@' && at < 0) at = i;
            }
            if (slash >= 0 && at >= 0)
            {
                var principal = new GssBufferDesc { Length = inputNameLen, Value = inputName };
                return gss_import_name(minorStatus, &principal, s_krb5PrincipalNameOidDesc, outputName);
            }
            byte* inputNameCopy = null;
            if (slash >= 0)
            {
                inputNameCopy = (byte*)NativeMemory.Alloc(inputNameLen);
                if (inputNameCopy == null)
                {
                    *minorStatus = 0;
                    return GSS_S_BAD_NAME;
                }
                Buffer.MemoryCopy(inputName, inputNameCopy, inputNameLen, inputNameLen);
                inputNameCopy[slash] = (byte)'@';
                inputName = inputNameCopy;
            }
            var inputNameBuffer = new GssBufferDesc { Length = inputNameLen, Value = inputName };
            uint result = gss_import_name(minorStatus, &inputNameBuffer, GSS_C_NT_HOSTBASED_SERVICE, outputName);
            if (inputNameCopy != null) NativeMemory.Free(inputNameCopy);
            return result;
        }

        [RuntimeExport("NetSecurityNative_ReleaseName", Trigger + "NetSecurityNative_ReleaseName")]
        public static uint ReleaseName(uint* minorStatus, void** inputName) => gss_release_name(minorStatus, inputName);

        [RuntimeExport("NetSecurityNative_AcquireAcceptorCred", Trigger + "NetSecurityNative_AcquireAcceptorCred")]
        public static uint AcquireAcceptorCred(uint* minorStatus, void** outputCredHandle) =>
            gss_acquire_cred(minorStatus, null, GSS_C_INDEFINITE, null, GSS_C_ACCEPT, outputCredHandle, null, null);

        [RuntimeExport("NetSecurityNative_InitiateCredSpNego", Trigger + "NetSecurityNative_InitiateCredSpNego")]
        public static uint InitiateCredSpNego(uint* minorStatus, void* desiredName, void** outputCredHandle)
        {
            var set = new GssOidSetDesc { Count = 1, Elements = s_spnegoOidDesc };
            return gss_acquire_cred(minorStatus, desiredName, 0, &set, GSS_C_INITIATE, outputCredHandle, null, null);
        }

        [RuntimeExport("NetSecurityNative_InitiateCredWithPassword", Trigger + "NetSecurityNative_InitiateCredWithPassword")]
        public static uint InitiateCredWithPassword(uint* minorStatus, int packageType, void* desiredName, byte* password, uint passwdLen, void** outputCredHandle)
        {
            var set = new GssOidSetDesc { Count = 1, Elements = MechFor((uint)packageType) };
            var passwordBuffer = new GssBufferDesc { Length = passwdLen, Value = password };
            return gss_acquire_cred_with_password(minorStatus, desiredName, &passwordBuffer, 0, &set, GSS_C_INITIATE, outputCredHandle, null, null);
        }

        [RuntimeExport("NetSecurityNative_ReleaseCred", Trigger + "NetSecurityNative_ReleaseCred")]
        public static uint ReleaseCred(uint* minorStatus, void** credHandle) => gss_release_cred(minorStatus, credHandle);

        [RuntimeExport("NetSecurityNative_InitSecContext", Trigger + "NetSecurityNative_InitSecContext")]
        public static uint InitSecContext(uint* minorStatus, void* claimantCredHandle, void** contextHandle, uint packageType, void* targetName,
            uint reqFlags, byte* inputBytes, uint inputLength, PalGssBuffer* outBuffer, uint* retFlags, int* isNtlmUsed) =>
            InitSecContextEx(minorStatus, claimantCredHandle, contextHandle, packageType, null, 0, targetName, reqFlags, inputBytes, inputLength,
                outBuffer, retFlags, isNtlmUsed);

        [RuntimeExport("NetSecurityNative_InitSecContextEx", Trigger + "NetSecurityNative_InitSecContextEx")]
        public static uint InitSecContextEx(uint* minorStatus, void* claimantCredHandle, void** contextHandle, uint packageType, void* cbt, int cbtSize,
            void* targetName, uint reqFlags, byte* inputBytes, uint inputLength, PalGssBuffer* outBuffer, uint* retFlags, int* isNtlmUsed)
        {
            // A negative size would become a huge size_t that GSS reads past.
            if (cbtSize < 0)
            {
                *minorStatus = 0;
                outBuffer->Length = 0;
                outBuffer->Data = null;
                *retFlags = 0;
                *isNtlmUsed = 0;
                return GSS_S_BAD_BINDINGS;
            }
            GssOidDesc* krbMech = gss_mech_krb5;
            GssOidDesc* desiredMech = MechFor(packageType);
            var inputToken = new GssBufferDesc { Length = inputLength, Value = inputBytes };
            var gssBuffer = new GssBufferDesc();
            GssOidDesc* outmech = null;
            var gssCbt = new GssChannelBindings();
            gssCbt.ApplicationData.Length = (nuint)cbtSize;
            gssCbt.ApplicationData.Value = cbt;
            uint majorStatus = gss_init_sec_context(minorStatus, claimantCredHandle, contextHandle, targetName, desiredMech, reqFlags, 0,
                cbt != null ? &gssCbt : null, &inputToken, &outmech, &gssBuffer, retFlags, null);
            *isNtlmUsed = packageType == PAL_GSS_NTLM || majorStatus != GSS_S_COMPLETE || gss_oid_equal(outmech, krbMech) == 0 ? 1 : 0;
            MoveBuffer(&gssBuffer, outBuffer);
            return majorStatus;
        }

        [RuntimeExport("NetSecurityNative_AcceptSecContext", Trigger + "NetSecurityNative_AcceptSecContext")]
        public static uint AcceptSecContext(uint* minorStatus, void* acceptorCredHandle, void** contextHandle, void* cbt, int cbtSize, byte* inputBytes,
            uint inputLength, PalGssBuffer* outBuffer, uint* retFlags, int* isNtlmUsed)
        {
            if (cbtSize < 0)
            {
                *minorStatus = 0;
                outBuffer->Length = 0;
                outBuffer->Data = null;
                *retFlags = 0;
                *isNtlmUsed = 0;
                return GSS_S_BAD_BINDINGS;
            }
            var inputToken = new GssBufferDesc { Length = inputLength, Value = inputBytes };
            var gssBuffer = new GssBufferDesc();
            var gssCbt = new GssChannelBindings();
            gssCbt.ApplicationData.Length = (nuint)cbtSize;
            gssCbt.ApplicationData.Value = cbt;
            GssOidDesc* mechType = null;
            uint majorStatus = gss_accept_sec_context(minorStatus, contextHandle, acceptorCredHandle, &inputToken, cbt != null ? &gssCbt : null, null,
                &mechType, &gssBuffer, retFlags, null, null);
            *isNtlmUsed = gss_oid_equal(mechType, s_ntlmOidDesc) != 0 ? 1 : 0;
            // gss-ntlmssp supports neither impersonation nor delegation but does not say so: GSS_C_IDENTIFY_FLAG, as on Windows.
            if (*isNtlmUsed == 1) *retFlags |= GSS_C_IDENTIFY_FLAG;
            MoveBuffer(&gssBuffer, outBuffer);
            return majorStatus;
        }

        [RuntimeExport("NetSecurityNative_DeleteSecContext", Trigger + "NetSecurityNative_DeleteSecContext")]
        public static uint DeleteSecContext(uint* minorStatus, void** contextHandle) => gss_delete_sec_context(minorStatus, contextHandle, null);

        [RuntimeExport("NetSecurityNative_GetUser", Trigger + "NetSecurityNative_GetUser")]
        public static uint GetUser(uint* minorStatus, void* contextHandle, PalGssBuffer* outBuffer)
        {
            void* srcName = null;
            uint majorStatus = gss_inquire_context(minorStatus, contextHandle, &srcName, null, null, null, null, null, null);
            if (majorStatus == GSS_S_COMPLETE)
            {
                var gssBuffer = new GssBufferDesc();
                majorStatus = gss_display_name(minorStatus, srcName, &gssBuffer, null);
                if (majorStatus == GSS_S_COMPLETE) MoveBuffer(&gssBuffer, outBuffer);
            }
            if (srcName != null) majorStatus = gss_release_name(minorStatus, &srcName);
            return majorStatus;
        }

        [RuntimeExport("NetSecurityNative_Wrap", Trigger + "NetSecurityNative_Wrap")]
        public static uint Wrap(uint* minorStatus, void* contextHandle, int* isEncrypt, byte* inputBytes, int count, PalGssBuffer* outBuffer)
        {
            int confState = 0;
            var inputMessageBuffer = new GssBufferDesc { Length = (nuint)(count < 0 ? 0 : count), Value = inputBytes };
            var gssBuffer = new GssBufferDesc();
            uint majorStatus = gss_wrap(minorStatus, contextHandle, *isEncrypt, GSS_C_QOP_DEFAULT, &inputMessageBuffer, &confState, &gssBuffer);
            MoveBuffer(&gssBuffer, outBuffer);
            *isEncrypt = confState;
            return majorStatus;
        }

        [RuntimeExport("NetSecurityNative_Unwrap", Trigger + "NetSecurityNative_Unwrap")]
        public static uint Unwrap(uint* minorStatus, void* contextHandle, int* isEncrypt, byte* inputBytes, int count, PalGssBuffer* outBuffer)
        {
            int confState = 0;
            var inputMessageBuffer = new GssBufferDesc { Length = (nuint)(count < 0 ? 0 : count), Value = inputBytes };
            var gssBuffer = new GssBufferDesc();
            uint majorStatus = gss_unwrap(minorStatus, contextHandle, &inputMessageBuffer, &gssBuffer, &confState, null);
            MoveBuffer(&gssBuffer, outBuffer);
            *isEncrypt = confState;
            return majorStatus;
        }

        [RuntimeExport("NetSecurityNative_GetMic", Trigger + "NetSecurityNative_GetMic")]
        public static uint GetMic(uint* minorStatus, void* contextHandle, byte* inputBytes, int inputLength, PalGssBuffer* outBuffer)
        {
            var inputMessageBuffer = new GssBufferDesc { Length = (nuint)(inputLength < 0 ? 0 : inputLength), Value = inputBytes };
            var gssBuffer = new GssBufferDesc();
            uint majorStatus = gss_get_mic(minorStatus, contextHandle, GSS_C_QOP_DEFAULT, &inputMessageBuffer, &gssBuffer);
            MoveBuffer(&gssBuffer, outBuffer);
            return majorStatus;
        }

        [RuntimeExport("NetSecurityNative_VerifyMic", Trigger + "NetSecurityNative_VerifyMic")]
        public static uint VerifyMic(uint* minorStatus, void* contextHandle, byte* inputBytes, int inputLength, byte* tokenBytes, int tokenLength)
        {
            var inputMessageBuffer = new GssBufferDesc { Length = (nuint)(inputLength < 0 ? 0 : inputLength), Value = inputBytes };
            var tokenBuffer = new GssBufferDesc { Length = (nuint)(tokenLength < 0 ? 0 : tokenLength), Value = tokenBytes };
            return gss_verify_mic(minorStatus, contextHandle, &inputMessageBuffer, &tokenBuffer, null);
        }

        [RuntimeExport("NetSecurityNative_IsNtlmInstalled", Trigger + "NetSecurityNative_IsNtlmInstalled")]
        public static uint IsNtlmInstalled()
        {
            uint minorStatus;
            GssOidSetDesc* mechSet = null;
            uint foundNtlm = 0;
            if (gss_indicate_mechs(&minorStatus, &mechSet) == GSS_S_COMPLETE)
            {
                for (nuint i = 0; i < mechSet->Count; i++)
                {
                    GssOidDesc oid = mechSet->Elements[i];
                    if (oid.Length == s_ntlmOidDesc->Length && new ReadOnlySpan<byte>(oid.Elements, (int)oid.Length).SequenceEqual(new ReadOnlySpan<byte>(s_ntlmOid, 10)))
                    {
                        foundNtlm = 1;
                        break;
                    }
                }
                gss_release_oid_set(&minorStatus, &mechSet);
            }
            return foundNtlm;
        }
    }
}
#endif
