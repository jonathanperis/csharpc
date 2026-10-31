// QUIC (System.Net.Quic) is not implemented by csharpc yet. Kestrel asks QuicListener.IsSupported on every
// start (UseKestrel -> UseQuic) and registers its HTTP/3 transport only when it is true. .NET answers false
// when it can't load MsQuic (libmsquic.so.2 / libmsquic.so; MsQuicApi's static constructor), which is the
// common case (no MsQuic in the runtime-deps images, nor on most machines): csharpc answers the same then.
// Where MsQuic is installed .NET would go on and use it, so csharpc stops with an explicit error instead of
// silently serving without HTTP/3.
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Net.Quic.QuicListener")]
    internal static class QuicListenerMembers
    {
        public static bool IsSupported => Quic.IsSupported;
    }

    [ShadowMembers("System.Net.Quic.QuicConnection")]
    internal static class QuicConnectionMembers
    {
        public static bool IsSupported => Quic.IsSupported;
    }

    internal static class Quic
    {
        private static int s_state; // 0 unknown, 1 no MsQuic

        internal static bool IsSupported
        {
            get
            {
                if (s_state == 0)
                {
#if CSHARPC_TARGET_LINUX
                    IntPtr handle;
                    if (NativeLibrary.TryLoad("libmsquic.so.2", out handle) || NativeLibrary.TryLoad("libmsquic.so", out handle))
                        throw new NotSupportedException("QUIC (System.Net.Quic over MsQuic, which is installed here) is not supported by csharpc yet.");
#elif CSHARPC_TARGET_WINDOWS
                    // Windows: .NET loads msquic.dll from the directory of System.Net.Quic.dll, which under NativeAOT
                    // is the app's (AppContext.BaseDirectory); never the system's.
                    if (System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "msquic.dll")))
                        throw new NotSupportedException("QUIC (System.Net.Quic over MsQuic, which is installed here) is not supported by csharpc yet.");
#endif
                    // macOS and WebAssembly: none of the names .NET probes (libmsquic.so.2, libmsquic.so) is a library there.
                    s_state = 1;
                }
                return false;
            }
        }
    }
}
