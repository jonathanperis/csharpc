// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/native/libs/Common/pal_compiler.h;
// see THIRD-PARTY-NOTICES.md. csharpc: reduced to what System.Globalization.Native uses (no minipal), and
// the target macros come from config.h.

#pragma once

#include "config.h"

#ifndef __cplusplus
#include <stdbool.h>
#endif

#ifndef __has_extension
#define __has_extension(...) 0
#endif

#ifdef static_assert
#define c_static_assert_msg(e, msg) static_assert((e), msg)
#define c_static_assert(e) c_static_assert_msg(e,"")
#elif __has_extension(c_static_assert)
#define c_static_assert_msg(e, msg) _Static_assert((e), msg)
#define c_static_assert(e) c_static_assert_msg(e, "")
#else
#define c_static_assert_msg(e, msg) typedef char __c_static_assert__[(e)?1:-1]
#define c_static_assert(e) c_static_assert_msg(e, "")
#endif

// csharpc: the shim is linked into the program (and internalized by runtime LTO), never exported.
#ifndef PALEXPORT
#define PALEXPORT
#endif

#ifndef PAL_CALLBACK_CALLTYPE
#define PAL_CALLBACK_CALLTYPE
#endif

#ifndef EXTERN_C
#define EXTERN_C extern
#endif

#ifndef TYPEOF
#define TYPEOF __typeof
#endif
