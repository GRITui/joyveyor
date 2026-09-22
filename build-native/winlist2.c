#include <CoreGraphics/CoreGraphics.h>
#include <CoreFoundation/CoreFoundation.h>
#include <stdio.h>
int main() {
    // ALL windows (including off-screen)
    CFArrayRef list = CGWindowListCopyWindowInfo(
        kCGWindowListOptionAll, kCGNullWindowID);
    CFIndex n = CFArrayGetCount(list);
    printf("ALL windows: %ld\n", (long)n);
    for (CFIndex i = 0; i < n; i++) {
        CFDictionaryRef w = (CFDictionaryRef)CFArrayGetValueAtIndex(list, i);
        CFStringRef owner = (CFStringRef)CFDictionaryGetValue(w, kCGWindowOwnerName);
        char obuf[256] = "?";
        if (owner) CFStringGetCString(owner, obuf, sizeof(obuf), kCFStringEncodingUTF8);
        // only print JoyVeyor + a few key ones
        if (strstr(obuf, "JoyVeyor") || strstr(obuf, "Unity")) {
            CFNumberRef layer = (CFNumberRef)CFDictionaryGetValue(w, kCGWindowLayer);
            CFNumberRef alpha = (CFNumberRef)CFDictionaryGetValue(w, kCGWindowAlpha);
            CFNumberRef wid = (CFNumberRef)CFDictionaryGetValue(w, kCGWindowNumber);
            CFDictionaryRef b = (CFDictionaryRef)CFDictionaryGetValue(w, kCGWindowBounds);
            int lv=0, al=0, widv=0;
            if (layer) CFNumberGetValue(layer, kCFNumberIntType, &lv);
            if (alpha) CFNumberGetValue(alpha, kCFNumberIntType, &al);
            if (wid) CFNumberGetValue(wid, kCFNumberIntType, &widv);
            double x=0,y=0,wd=0,ht=0;
            if (b) {
                CFNumberRef nx=(CFNumberRef)CFDictionaryGetValue(b,CFSTR("X"));
                CFNumberRef ny=(CFNumberRef)CFDictionaryGetValue(b,CFSTR("Y"));
                CFNumberRef nw=(CFNumberRef)CFDictionaryGetValue(b,CFSTR("Width"));
                CFNumberRef nh=(CFNumberRef)CFDictionaryGetValue(b,CFSTR("Height"));
                if(nx)CFNumberGetValue(nx,kCFNumberDoubleType,&x);
                if(ny)CFNumberGetValue(ny,kCFNumberDoubleType,&y);
                if(nw)CFNumberGetValue(nw,kCFNumberDoubleType,&wd);
                if(nh)CFNumberGetValue(nh,kCFNumberDoubleType,&ht);
            }
            printf("  WIN owner=%s wid=%d layer=%d alpha=%d bounds=(%g,%g %g x %g)\n",
                   obuf, widv, lv, al, x, y, wd, ht);
        }
    }
    CFRelease(list);
    return 0;
}
