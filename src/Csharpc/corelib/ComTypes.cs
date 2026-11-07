// The plain structs of System.Runtime.InteropServices.ComTypes that COM interop declarations name (IStream's
// STATSTG, FILETIME): sequential layouts with .NET's field names and order, so a struct built or read by
// name and a P/Invoke that passes one see what .NET's would. COM calls themselves stay the CSLLVM0008 boundary.
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.ComTypes.FILETIME")]
    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIMEImpl
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [Shadows("System.Runtime.InteropServices.ComTypes.STATSTG")]
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STATSTGImpl
    {
        public string pwcsName;
        public int type;
        public long cbSize;
        public System.Runtime.InteropServices.ComTypes.FILETIME mtime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ctime;
        public System.Runtime.InteropServices.ComTypes.FILETIME atime;
        public int grfMode;
        public int grfLocksSupported;
        public Guid clsid;
        public int grfStateBits;
        public int reserved;
    }
}
