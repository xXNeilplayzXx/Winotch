//! Winotch (WebView build) — desktop entry point.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    winotch_webview_lib::run()
}
