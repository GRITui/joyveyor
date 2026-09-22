#include <CoreGraphics/CoreGraphics.h>
#include <CoreFoundation/CoreFoundation.h>
#include <stdio.h>
int main() {
    CFArrayRef list = CGWindowListCopyWindowInfo(
        kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements,
        kCGNullWindowID);
    CFIndex n = CFArrayGetCount(list);
    printf("onscreen windows: %ld\n", (long)n);
    for (CFIndex i = 0; i < n; i++) {
        CFDictionaryRef w = (CFDictionaryRef)CFArrayGetValueAtIndex(list, i);
        CFStringRef owner = (CFStringRef)CFDictionaryGetValue(w, kCGWindowOwnerName);
        CFStringRef wname = (CFStringRef)CFDictionaryGetValue(w, kCGWindowName);
        CFNumberRef layer = (CFNumberRef)CFDictionaryGetValue(w, kCGWindowLayer);
        CFNumberRef alpha = (CFNumberRef)CFDictionaryGetValue(w, kCGWindowAlpha);
        int lv = 0, al = 0;
        if (layer) CFNumberGetValue(layer, kCFNumberIntType, &lv);
        if (alpha) CFNumberGetValue(alpha, kCFNumberIntType, &al);
        char obuf[256] = "?", nbuf[256] = "";
        if (owner) CFStringGetCString(owner, obuf, sizeof(obuf), kCFStringEncodingUTF8);
        if (wname) CFStringGetCString(wname, nbuf, sizeof(nbuf), kCFStringEncodingUTF8);
        printf("  owner=%-22s layer=%-4d alpha=%d name=%s\n", obuf, lv, al, nbuf);
    }
    CFRelease(list);
    return 0;
}
