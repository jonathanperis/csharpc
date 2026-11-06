// Marshal.SizeOf of a struct with a VARIANT_BOOL field: .NET on Windows has the built-in COM marshaller and gives its size;
// elsewhere the type "cannot be marshaled as an unmanaged structure" (ArgumentException). Platform-dependent, so it has a
// sample of its own (tests/platform-skips.txt).
using System;
using System.Runtime.InteropServices;

try { Console.WriteLine(Marshal.SizeOf<VariantFlag>()); }
catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

struct VariantFlag { [MarshalAs(UnmanagedType.VariantBool)] public bool Flag; }
