// libFuzzer harness for csrt's input-handling code (number parsing, format strings, composite
// formatting, UTF-8 decoding, string operations, decimal/BigInteger/DateTime formatting).
// See tools/fuzz/run.sh. The first input byte selects the target; the rest is the input.
//
// The runtime raises C# exceptions (FormatException, ArgumentOutOfRangeException, ...) with the
// Itanium unwinder; a C++ catch(...) catches those foreign exceptions, which is the *correct*
// outcome for bad input. Only sanitizer reports, crashes and hangs are bugs.
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <string>

extern "C" {
#include "../../src/runtime/csrt.h"
csrt_String* csrt_format_i64(int64_t v, csrt_String* fmt, int32_t bits);
csrt_String* csrt_format_u64(uint64_t v, csrt_String* fmt, int32_t bits);
csrt_String* csrt_format_f64(double v, csrt_String* fmt, bool single);
csrt_String* csrt_string_format(csrt_String* fmt, csrt_Array* args);
bool csrt_int32_try_parse(csrt_String* s, int32_t* result);
bool csrt_int64_try_parse(csrt_String* s, int64_t* result);
bool csrt_double_try_parse(csrt_String* s, double* result);
int32_t csrt_int32_parse(csrt_String* s);
double csrt_double_parse(csrt_String* s);
csrt_String* csrt_string_replace(csrt_String* s, csrt_String* from, csrt_String* to);
csrt_Array* csrt_string_split_char(csrt_String* s, uint16_t sep, int32_t options);
csrt_String* csrt_string_pad(csrt_String* s, int32_t width, uint16_t c, bool left);
csrt_String* csrt_string_trim(csrt_String* s, int32_t mode);
csrt_String* csrt_string_substring(csrt_String* s, int32_t start, int32_t length);
csrt_String* csrt_timespan_format(int64_t ticks, csrt_String* fmt);
csrt_String* csrt_datetime_format(int64_t ticks, csrt_String* fmt);
csrt_String* csrt_string_align(csrt_String* s, int32_t width);
}

static csrt_String* str(const uint8_t* data, size_t size) {
    std::string text(reinterpret_cast<const char*>(data), size);  // NUL-terminated copy
    return csrt_string_from_utf8(text.c_str());
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
        switch (selector % 10) {
            case 0: {  // integer / double parsing (and UTF-8 decoding of the input)
                csrt_String* s = str(data, size);
                int32_t i; int64_t l; double d;
                csrt_int32_try_parse(s, &i); csrt_int64_try_parse(s, &l); csrt_double_try_parse(s, &d);
                csrt_int32_parse(s);
                break;
            }
            case 1: { int64_t v = take<int64_t>(data, size); csrt_format_i64(v, str(data, size), 64); break; }
            case 2: { uint64_t v = take<uint64_t>(data, size); csrt_format_u64(v, str(data, size), 32); break; }
            case 3: { double v = take<double>(data, size); csrt_format_f64(v, str(data, size), false); break; }
            case 4: {  // composite formatting with three boxed-int / string arguments
                csrt_Array* args = csrt_new_array(sizeof(void*), 3);
                void** items = reinterpret_cast<void**>(args->data);
                items[0] = csrt_string_from_utf8("zero"); items[1] = csrt_string_from_i32(42); items[2] = nullptr;
                csrt_string_format(str(data, size), args);
                break;
            }
            case 5: {  // replace / split / trim on fuzzed pieces
                size_t cut = size ? data[0] % (size + 1) : 0;
                csrt_String* a = str(data, cut);
                csrt_String* b = str(data + cut, size - cut);
                if (b->length) csrt_string_replace(a, b, a);
                csrt_string_split_char(a, b->length ? b->chars[0] : ',', (int32_t)(cut & 3));
                csrt_string_trim(b, (int32_t)(cut % 3) + 1);
                break;
            }
            case 6: {  // pad / substring / align with fuzzed (bounded) widths
                int32_t w = take<int32_t>(data, size), start = take<int32_t>(data, size), len = take<int32_t>(data, size);
                csrt_String* s = str(data, size);
                csrt_string_pad(s, w % 4096, '*', w & 1);
                csrt_string_substring(s, start, len);
                csrt_string_align(s, w % 4096);
                break;
            }
            case 7: { int64_t t = take<int64_t>(data, size); csrt_timespan_format(t, str(data, size)); break; }
            case 8: { uint64_t t = take<uint64_t>(data, size) % 3155378975999999999ULL; csrt_datetime_format((int64_t)t, str(data, size)); break; }
            case 9: { csrt_double_parse(str(data, size)); break; }
        }
    } catch (...) {
        // A C# exception: the runtime rejected the input the way .NET does.
    }
    return 0;
}
