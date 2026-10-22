// libFuzzer harness for the input-handling code that is still C in csrt: UTF-8 decoding, UTF-16
// strings from native memory (P/Invoke), ordinal string primitives and Enum.Parse's name matching.
// Number/date formatting and parsing, decimal and BigInteger are C# (src/Csharpc/corelib) and are
// covered by the differential fuzzer in tools/fuzz/corelib (see tools/fuzz/run.sh).
// The first input byte selects the target; the rest is the input.
//
// The runtime raises C# exceptions (FormatException, ArgumentException, ...) with the Itanium
// unwinder; a C++ catch(...) catches those foreign exceptions, which is the *correct* outcome for
// bad input. Only sanitizer reports, crashes and hangs are bugs.
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

extern "C" {
#include "../../src/runtime/csrt.h"
int32_t csrt_string_compare_ordinal(csrt_String* a, csrt_String* b);
int32_t csrt_string_get_hash_code(csrt_String* s);
csrt_String* csrt_string_new_from_utf16(const uint16_t* chars, int32_t length);
csrt_String* csrt_pinvoke_string_from_native(void* p, bool unicode, bool release);
int32_t csrt_enum_parse(csrt_String* s, bool ignore_case, const uint64_t* values, csrt_String* const* names, int32_t count,
                        int32_t bits, bool is_signed, int64_t* result);

// Enum.Parse hands numeric input to the corelib's C# parser (an export of compiled programs). The
// harness has no corelib, so numeric pieces simply fail to parse here.
int32_t csrt_parse_i64_styled(csrt_String*, int32_t, int32_t, bool, int64_t* result) { *result = 0; return 0; }
}

static csrt_String* str(const uint8_t* data, size_t size) {
    return csrt_string_from_utf8_n(reinterpret_cast<const char*>(data), size);
}

template <typename T> static T take(const uint8_t*& data, size_t& size) {
    T value{};
    size_t n = size < sizeof(T) ? size : sizeof(T);
    std::memcpy(&value, data, n);
    data += n; size -= n;
    return value;
}

extern "C" int LLVMFuzzerInitialize(int*, char***) {
    csrt_init(0, nullptr);
    return 0;
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t* data, size_t size) {
    if (size < 1) return 0;
    uint8_t selector = *data++; size--;
    try {
        switch (selector % 4) {
            case 0: {  // UTF-8 decoding: length-delimited and NUL-terminated
                str(data, size);
                std::string text(reinterpret_cast<const char*>(data), size);
                csrt_string_from_utf8(text.c_str());
                break;
            }
            case 1: {  // UTF-16 from native memory: counted and NUL-terminated (LPWStr marshalling)
                std::vector<uint16_t> units(size / 2 + 1, 0);
                std::memcpy(units.data(), data, size & ~size_t{1});
                csrt_string_new_from_utf16(units.data(), static_cast<int32_t>(size / 2));
                csrt_pinvoke_string_from_native(units.data(), true, false);
                break;
            }
            case 2: {  // ordinal primitives on two fuzzed halves
                size_t cut = size ? data[0] % (size + 1) : 0;
                csrt_String* a = str(data, cut);
                csrt_String* b = str(data + cut, size - cut);
                csrt_string_compare_ordinal(a, b);
                csrt_string_get_hash_code(a);
                csrt_string_equals(a, b);
                csrt_string_concat(a, b);
                break;
            }
            case 3: {  // Enum.Parse name matching ([Flags]-style comma lists, case folding)
                static csrt_String* names[4];
                static const uint64_t values[4] = { 0, 1, 2, 4 };
                if (!names[0]) {
                    const char* n[4] = { "None", "Read", "Write", "Execute" };
                    for (int i = 0; i < 4; i++) names[i] = csrt_string_from_utf8(n[i]);
                }
                int64_t result;
                csrt_enum_parse(str(data, size), selector & 0x80, values, names, 4, 32, true, &result);
                break;
            }
        }
    } catch (...) {
        // A C# exception: the runtime rejected the input the way .NET does.
    }
    return 0;
}
