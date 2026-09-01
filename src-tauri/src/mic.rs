// Microphone-in-use detection for the island's status dot.
//
// On Windows this meters every active capture audio endpoint (the same signal
// the OS privacy indicator uses): if any capture device's loudness meter rises
// above a quiet threshold, *something* is recording or on a call. The frontend
// combines this with the call watcher to color the island dot (red = mic in
// use, blue = mic + call, green = call with mic muted). On other platforms
// every entry point is a no-op, so the dot stays grey.

use serde::Serialize;
use std::sync::{Mutex, OnceLock};

#[derive(Clone, Serialize, Default, PartialEq)]
pub struct MicState {
    pub on: bool,
}

/// Last snapshot we emitted, so the frontend can pull initial state on load.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
static LAST: OnceLock<Mutex<Option<MicState>>> = OnceLock::new();

#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn set_last(on: bool) {
    let cell = LAST.get_or_init(|| Mutex::new(None));
    if let Ok(mut guard) = cell.lock() {
        *guard = Some(MicState { on });
    }
}

/// Pull the currently known mic state (null when never sampled, treated as off).
#[tauri::command]
pub fn mic_get_state() -> Option<MicState> {
    LAST.get()
        .and_then(|m| m.lock().ok())
        .and_then(|g| g.clone())
}

/// Start the mic watcher. No-op off Windows.
pub fn spawn(app: tauri::AppHandle) {
    #[cfg(target_os = "windows")]
    win::spawn(app);
    #[cfg(not(target_os = "windows"))]
    let _ = app;
}

#[cfg(target_os = "windows")]
mod win {
    use super::set_last;
    use std::time::Duration;
    use tauri::Emitter;
    use windows::Win32::Media::Audio::Endpoints::IAudioMeterInformation;
    use windows::Win32::Media::Audio::{
        eCapture, DEVICE_STATE_ACTIVE, IMMDeviceEnumerator, MMDeviceEnumerator,
    };
    use windows::Win32::System::Com::{CoCreateInstance, CoInitializeEx, CLSCTX_ALL, COINIT_MULTITHREADED};

    /// Peak meter level. Meters read ~0 on a silent device; anything above this
    /// sustained for a couple of polls counts as "mic in use".
    const THRESHOLD: f32 = 0.03;
    const CONFIRM_POLLS: u8 = 2;
    const END_POLLS: u8 = 4;

    /// Highest peak value across all active capture endpoints (0.0–1.0).
    fn mic_peak() -> f32 {
        let Ok(enumerator): Result<IMMDeviceEnumerator, _> =
            (unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) })
        else {
            return 0.0;
        };
        let Ok(collection) =
            (unsafe { enumerator.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE) })
        else {
            return 0.0;
        };
        let Ok(count) = (unsafe { collection.GetCount() }) else {
            return 0.0;
        };
        let mut peak = 0.0f32;
        for i in 0..count {
            let Ok(device) = (unsafe { collection.Item(i) }) else {
                continue;
            };
            let Ok(meter) =
                (unsafe { device.Activate::<IAudioMeterInformation>(CLSCTX_ALL, None) })
            else {
                continue;
            };
            let Ok(v) = (unsafe { meter.GetPeakValue() }) else {
                continue;
            };
            if v > peak {
                peak = v;
            }
        }
        peak
    }

    pub fn spawn(app: tauri::AppHandle) {
        std::thread::spawn(move || {
            unsafe {
                let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
            }

            let mut on = false;
            let mut on_streak: u8 = 0;
            let mut off_streak: u8 = 0;

            loop {
                if mic_peak() >= THRESHOLD {
                    on_streak = on_streak.saturating_add(1);
                    off_streak = 0;
                    if on_streak >= CONFIRM_POLLS && !on {
                        on = true;
                        set_last(true);
                        let _ = app.emit("mic://state", true);
                    }
                } else {
                    off_streak = off_streak.saturating_add(1);
                    on_streak = 0;
                    if off_streak >= END_POLLS && on {
                        on = false;
                        set_last(false);
                        let _ = app.emit("mic://state", false);
                    }
                }

                std::thread::sleep(Duration::from_millis(400));
            }
        });
    }
}