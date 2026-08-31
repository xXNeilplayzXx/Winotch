// macOS: let the island hug the absolute top edge of the screen.
//
// macOS clamps ordinary windows out of the menu-bar strip: NSWindow's
// `constrainFrameRect:toScreen:` pushes any proposed frame to start below the
// menu bar (30px on this Mac). Physical notch hardware isn't clamped, and
// neither is the island — we add an override of `constrainFrameRect:toScreen:`
// directly on the window's runtime class (tao's NSWindow subclass, which does
// not implement the method itself), so Y=0 lands on the monitor's first pixel
// row. No class replacement is involved, so tao's event plumbing is untouched.
//
// Windows has no such clamp — SetWindowPos places at Y=0 natively — so this
// module is compiled only on macOS.

use objc2::runtime::{AnyClass, AnyObject, Sel};
use objc2::sel;
use objc2_core_foundation::CGRect;

unsafe extern "C" fn constrain_frame_rect(
    _this: *const AnyObject,
    _cmd: Sel,
    frame: CGRect,
    _screen: *mut AnyObject,
) -> CGRect {
    // Accept the proposed frame verbatim — no menu-bar push-down.
    frame
}

/// Type encoding for `- (CGRect)constrainFrameRect:(CGRect)frame toScreen:(id)screen`:
/// return CGRect, self@0, SEL:8, CGRect by value at 16, screen object at 48.
const TYPE_ENCODINGS: &str =
    "{CGRect={CGPoint=dd}{CGSize=dd}}@0:8{CGRect={CGPoint=dd}{CGSize=dd}}16@48";

/// Patch the given NSWindow so it can be positioned flush with the top edge.
pub fn allow_top_edge(ns_window: *mut std::ffi::c_void) {
    unsafe {
        let obj = &*(ns_window as *const AnyObject);
        let imp: objc2::runtime::Imp = std::mem::transmute(
            constrain_frame_rect
                as unsafe extern "C" fn(*const AnyObject, Sel, CGRect, *mut AnyObject) -> CGRect,
        );
        // Returns NO if the class already implements the method (double init).
        let _ = objc2::ffi::class_addMethod(
            (obj.class() as *const AnyClass).cast_mut(),
            sel!(constrainFrameRect:toScreen:),
            imp,
            TYPE_ENCODINGS.as_ptr().cast(),
        );
    }
}

/// Put the window in the highest sensible layer so it floats above every app
/// window (including the frontmost one) AND the menu bar, and keep it visible
/// on every Space / next to fullscreen apps. Windows gets this for free from
/// HWND_TOPMOST; this is the macOS equivalent.
///
/// Layer 25 = NSStatusWindowLevel: above normal apps (0), floating panels (3)
/// and the menu bar (24). Hover hit-testing follows z-order, so once the island
/// is the topmost window under the cursor it receives enter/exit regardless of
/// which app has keyboard focus.
pub fn float_above_all(ns_window: *mut std::ffi::c_void) {
    unsafe {
        let ns = ns_window as *mut AnyObject;
        let status_window_level: i64 = 25;
        let _: () = objc2::msg_send![ns, setLevel: status_window_level];
        // NSWindowCollectionBehaviorCanJoinAllSpaces (1) |
        // NSWindowCollectionBehaviorFullScreenAuxiliary (8)
        let behavior: i64 = 1 | 8;
        let _: () = objc2::msg_send![ns, setCollectionBehavior: behavior];
        // Make sure the window always participates in hit-testing / hover.
        let _: () = objc2::msg_send![ns, setIgnoresMouseEvents: false];
        let _: () = objc2::msg_send![ns, setAcceptsMouseMovedEvents: true];
    }
}
