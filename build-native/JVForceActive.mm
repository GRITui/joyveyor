// JVForceActive.mm — native macOS helper for the JoyVeyor player.
// Disables App Nap (which throttles backgrounded/occluded apps and pauses the
// display-link-driven game loop) and forces the app to the foreground so the
// window's display link runs at full speed. Called from PlayerCapture via DllImport.
#include <AppKit/AppKit.h>
#include <Foundation/Foundation.h>

extern "C"
{
    // Disable App Nap for this process and bring the app to the foreground.
    // Safe to call repeatedly.
    void JV_ForceActive()
    {
        @autoreleasepool
        {
            // Keep the system from napping this (backgrounded) app.
            [[NSProcessInfo processInfo] beginActivityWithOptions:
                (NSActivityIdleSystemSleepDisabled |
                 NSActivitySuddenTerminationDisabled |
                 NSActivityUserInitiatedAllowingIdleSystemSleep)
                reason:@"JoyVeyor hero capture"];

            // Force the app to the foreground so its window is unoccluded and
            // the display link runs.
            [[NSApplication sharedApplication] activateIgnoringOtherApps:YES];

            // Make the key window front, visible, and ABOVE other normal
            // windows (Chrome is at NSNormalWindowLevel and occludes us, which
            // makes the WindowServer skip compositing -> clear-only frames).
            NSWindow *w = [[NSApplication sharedApplication] keyWindow];
            if (w == nil) w = [[NSApplication sharedApplication] mainWindow];
            if (w == nil)
            {
                // Fall back to the first window in the app's window list.
                NSArray *wins = [[NSApplication sharedApplication] windows];
                if (wins.count > 0) w = wins[0];
            }
            if (w != nil)
            {
                [w setLevel:NSFloatingWindowLevel];
                [w makeKeyAndOrderFront:nil];
                [w orderFrontRegardless];
            }
        }
    }
}
