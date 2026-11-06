/* The native asset of the RidPkg test package (runtimes/<rid>/native): tests/il.sh compiles it for the host. */
#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

EXPORT int rid_native_answer(void) { return 42; }
EXPORT int rid_native_add(int a, int b) { return a + b; }
