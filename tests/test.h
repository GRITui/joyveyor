// Minimal zero-dependency test harness for joyveyor.
// Each test file defines main(), calls JV_CHECK/JV_CHECK_EQ, then JV_REPORT().
#pragma once
#include <cstdio>

namespace jvtest {
inline int& failures() {
    static int f = 0;
    return f;
}
inline int& checks() {
    static int c = 0;
    return c;
}
inline void check(bool cond, const char* expr, const char* file, int line) {
    ++checks();
    if (!cond) {
        ++failures();
        std::printf("FAIL %s:%d: %s\n", file, line, expr);
    }
}
}  // namespace jvtest

#define JV_CHECK(expr) jvtest::check(static_cast<bool>(expr), #expr, __FILE__, __LINE__)
#define JV_CHECK_EQ(a, b) jvtest::check((a) == (b), #a " == " #b, __FILE__, __LINE__)

#define JV_REPORT()                                                       \
    do {                                                                  \
        std::printf("%d checks, %d failures\n", jvtest::checks(),         \
                    jvtest::failures());                                  \
        return jvtest::failures() == 0 ? 0 : 1;                           \
    } while (0)
