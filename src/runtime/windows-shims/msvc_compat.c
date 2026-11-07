// The MSVC runtime symbols .NET's Windows shim archives need (Windows x64 only).
//
// The NativeAOT runtime pack for win-x64 ships System.IO.Compression.Native.Aot.lib, brotli*.lib,
// zlibstatic.lib and zstd_static.lib built with cl (/GS, /guard:cf, /MT). Linked into a MinGW (UCRT,
// lld) executable they still reference a handful of symbols that only MSVC's vcruntime defines;
// this file supplies them. It is not part of the runtime proper: Toolchain compiles it on demand and
// links it only when a program links one of the shims (NativeAotPack), so every other program is
// byte-for-byte what it was.
//
// Every definition is weak, so the C runtime of a MinGW distribution that has its own (mingw-w64's
// gs_support.c defines the stack cookie, for one) wins without a duplicate-symbol error. No headers:
// the file is compiled freestanding.

#if defined(_WIN64) && defined(__x86_64__)

typedef unsigned long long csrt_uptr;

// /GS: a function with a stack buffer stores __security_cookie ^ rsp in its frame and, before it
// returns, compares the copy against the cookie. MSVC's startup code randomizes the cookie; the
// default value is what the check compares against if nothing does, which still catches a smashed
// frame (the frame copy changes, the cookie does not).
__attribute__((weak)) csrt_uptr __security_cookie = 0x00002B992DDFA232ull;

// __fastfail(code): INT 0x29 with the code in ECX, which is how Windows (and the MSVC runtime) ends a
// process on a failed security check (exit code 0xC0000409, STATUS_STACK_BUFFER_OVERRUN).
static __attribute__((noreturn)) void csrt_fastfail(unsigned code)
{
    __asm__ volatile("int $0x29" : : "c"(code) : "memory");
    __builtin_unreachable();
}

__attribute__((weak)) void __security_check_cookie(csrt_uptr cookie)
{
    if (cookie != __security_cookie) csrt_fastfail(2 /* FAST_FAIL_STACK_COOKIE_CHECK_FAILURE */);
}

__attribute__((weak)) void __report_rangecheckfailure(void)
{
    csrt_fastfail(8 /* FAST_FAIL_RANGE_CHECK_FAILURE */);
}

// The exception handler /GS puts in the unwind data of functions with a stack buffer: it only checks
// the cookie during a throw. Nothing throws through the shims (they call back into no managed code),
// so it declines (ExceptionContinueSearch).
__attribute__((weak)) int __GSHandlerCheck(void *record, void *frame, void *context, void *dispatcher)
{
    (void)record; (void)frame; (void)context; (void)dispatcher;
    return 1;
}

// /guard:cf: indirect calls go through a pointer to the loader's validation routine with the target
// in RAX. Without a Control Flow Guard image there is nothing to validate: jump to the target.
__attribute__((naked)) static void csrt_guard_dispatch_icall(void)
{
    __asm__ volatile("jmp *%rax");
}
__attribute__((weak)) void (*__guard_dispatch_icall_fptr)(void) = csrt_guard_dispatch_icall;

// MSVC's stack probe for frames over a page (MinGW's compiler-rt names its own ___chkstk_ms): touches
// every page between RSP and RSP - RAX and returns with RAX and RSP unchanged, the caller then
// subtracts RAX.
__attribute__((naked, weak)) void __chkstk(void)
{
    __asm__ volatile(
        "push %rcx\n\t"
        "push %rax\n\t"
        "cmp $0x1000, %rax\n\t"
        "lea 24(%rsp), %rcx\n\t"
        "jb 3f\n"
        "2:\n\t"
        "sub $0x1000, %rcx\n\t"
        "test %rcx, (%rcx)\n\t"
        "sub $0x1000, %rax\n\t"
        "cmp $0x1000, %rax\n\t"
        "ja 2b\n"
        "3:\n\t"
        "sub %rax, %rcx\n\t"
        "test %rcx, (%rcx)\n\t"
        "pop %rax\n\t"
        "pop %rcx\n\t"
        "ret");
}

// The instruction-set level the vcruntime's startup stores for code compiled with /arch: 1 is SSE2,
// the x86-64 baseline (zstd and brotli check it before using a wider path of their own).
__attribute__((weak)) int __isa_available = 1;

// The compiler marks objects that use floating point with this symbol; a flag, never read.
__attribute__((weak)) int _fltused = 0x9875;

#endif
