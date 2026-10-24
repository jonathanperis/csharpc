// csharpc: the configuration that System.Globalization.Native's CMake build generates (config.h.in), plus
// the TARGET_* macros its sources test, derived from the compiler's predefined macros (the runtime is
// compiled for the program's target). ICU's headers are not needed: like .NET's Android build, the shim
// declares the ICU types and functions it uses itself (pal_icushim_internal_android.h) and binds them at
// run time.
#pragma once

// The ICU declarations come from pal_icushim_internal_android.h on every target.
#define CSHARPC_HEADERLESS_ICU 1

// What src/native/minipal/types.h and utils.h provide to the shim.
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#ifndef ARRAY_SIZE
#define ARRAY_SIZE(a) (sizeof(a) / sizeof((a)[0]))
#endif

#if defined(_WIN32)
#  ifndef TARGET_WINDOWS
#    define TARGET_WINDOWS 1
#  endif
#else
#  ifndef TARGET_UNIX
#    define TARGET_UNIX 1
#  endif
#  if defined(__APPLE__) && !defined(TARGET_OSX)
#    define TARGET_OSX 1
#  endif
#endif

// Both hold for every ICU version .NET supports (UDAT_STANDALONE_SHORTER_WEEKDAYS exists since ICU 51).
#define HAVE_UDAT_STANDALONE_SHORTER_WEEKDAYS 1
#define HAVE_UCOL_CLONE 0

// The ICU major version .NET's Linux shim is built against: the probe for libicuuc.so.N runs from
// U_ICU_VERSION_MAJOR_NUM + 30 down to 60 (pal_icushim.c).
#ifndef U_ICU_VERSION_MAJOR_NUM
#define U_ICU_VERSION_MAJOR_NUM 72
#endif

#if defined(TARGET_OSX)
#define OSX_ICU_LIBRARY_PATH "/usr/lib/libicucore.dylib"
#endif
