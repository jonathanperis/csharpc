// csrt on Windows: C library functions the UCRT lacks but LLVM-generated code may call.
#include "csrt.h"

#ifdef _WIN32
#include <math.h>

// C23 roundeven: LLVM lowers llvm.roundeven (Math.Round's default MidpointRounding.ToEven) to this
// libcall when the CPU has no SSE4.1 round instruction. RuntimeLto keeps these external.
double roundeven(double x) {
    double r = round(x);  // halfway cases away from zero
    if (fabs(x - trunc(x)) == 0.5) r = 2.0 * round(x / 2.0);
    return r;
}

float roundevenf(float x) {
    float r = roundf(x);
    if (fabsf(x - truncf(x)) == 0.5f) r = 2.0f * roundf(x / 2.0f);
    return r;
}
#else
typedef int csrt_win32_unused;  // ISO C: a translation unit must not be empty
#endif
