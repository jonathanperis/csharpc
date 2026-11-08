// A tiny native library for tests/projects.sh: built as a static archive (linked by csharpc through DirectPInvoke and
// NativeLibrary) and as a shared library (loaded by `dotnet run`, the reference output).
#include <errno.h>
#include <string.h>

static int counter;

int native_add(int a, int b) { return a + b; }

double native_scale(double x, int times) {
    while (times-- > 0) x *= 2;
    return x;
}

// State that lives in the native library: both builds must keep one counter for the whole run.
int native_counter_next(void) { return ++counter; }

int native_length(const char* text) { return (int)strlen(text); }

void native_fill(unsigned char* buffer, int length) {
    for (int i = 0; i < length; i++) buffer[i] = (unsigned char)(i * 3);
}

int native_fail(void) {
    errno = ENOENT;
    return -1;
}

typedef struct { int x; int y; } Point;

Point native_swap(Point p) {
    Point q = { p.y, p.x };
    return q;
}

int native_sum_with_callback(int (*callback)(int), int count) {
    int total = 0;
    for (int i = 1; i <= count; i++) total += callback(i);
    return total;
}
