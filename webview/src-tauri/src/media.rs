//! Media session (Windows SMTC) — ported from src-tauri/src/media.rs.
//! A watcher thread keeps the latest MediaState; the UI polls it each frame.
//! Off Windows everything is a no-op empty state.

use serde::Serialize;
use std::sync::{mpsc, Mutex, OnceLock};


#[derive(Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MediaState {
    pub title: Option<String>,
    pub artist: Option<String>,
    pub playing: bool,
    pub position: f64,
    pub duration: f64,
    /// Decoded album-art RGBA for egui (downsampled to 96x96).
    #[serde(skip)]
    pub art_rgba: Option<(Vec<u8>, u32, u32)>,
}

#[derive(Clone, Copy)]
enum Action { PlayPause, Next, Previous }

static ACTIONS: OnceLock<mpsc::Sender<Action>> = OnceLock::new();
static LAST: OnceLock<Mutex<Option<MediaState>>> = OnceLock::new();

pub fn get() -> Option<MediaState> {
    LAST.get().and_then(|m| m.lock().ok()).and_then(|g| g.clone())
}

#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn set_last(state: Option<MediaState>) {
    let cell = LAST.get_or_init(|| Mutex::new(None));
    if let Ok(mut g) = cell.lock() { *g = state; }
}

fn send_action(a: Action) {
    if let Some(tx) = ACTIONS.get() { let _ = tx.send(a); }
}

pub fn play_pause() { send_action(Action::PlayPause); }
pub fn next() { send_action(Action::Next); }
pub fn previous() { send_action(Action::Previous); }

/// Start the watcher thread. No-op off Windows.
pub fn spawn() {
    #[cfg(target_os = "windows")]
    win::spawn();
}

#[cfg(target_os = "windows")]
mod win {
    use super::{set_last, Action, MediaState, ACTIONS};

    use std::sync::mpsc;
    use std::sync::OnceLock;
    use std::time::Duration;
    use windows::Foundation::TimeSpan;
    use windows::Media::Control::*;
    use windows::Storage::Streams::{DataReader, IRandomAccessStreamReference};
    use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};

    type Manager = GlobalSystemMediaTransportControlsSessionManager;

    fn secs(t: TimeSpan) -> f64 { t.Duration as f64 / 10_000_000.0 }

    fn manager() -> Option<Manager> {
        static MANAGER: OnceLock<Manager> = OnceLock::new();
        if let Some(m) = MANAGER.get() { return Some(m.clone()); }
        let m = GlobalSystemMediaTransportControlsSessionManager::RequestAsync().ok()?.get().ok()?;
        Some(MANAGER.get_or_init(|| m).clone())
    }

    fn decode_art(thumb: &IRandomAccessStreamReference) -> Option<(Vec<u8>, u32, u32)> {
        let stream = thumb.OpenReadAsync().ok()?.get().ok()?;
        let size = stream.Size().ok()? as u32;
        if size == 0 || size > 4_000_000 { return None; }
        let reader = DataReader::CreateDataReader(&stream).ok()?;
        reader.LoadAsync(size).ok()?.get().ok()?;
        let mut buf = vec![0u8; size as usize];
        reader.ReadBytes(&mut buf).ok()?;
        let img = image::load_from_memory(&buf).ok()?;
        let img = img.resize(96, 96, image::imageops::FilterType::Triangle);
        let rgba = img.to_rgba8();
        Some((rgba.to_vec(), rgba.width(), rgba.height()))
    }

    fn read_state(manager: &Manager, art_cache: &mut (String, Option<(Vec<u8>, u32, u32)>)) -> Option<MediaState> {
        let session = manager.GetCurrentSession().ok()?;
        let props = session.TryGetMediaPropertiesAsync().ok()?.get().ok()?;
        let title = props.Title().ok()?.to_string();
        let artist = props.Artist().ok()?.to_string();
        let key = format!("{title}|{artist}");
        if art_cache.0 != key {
            let art = props.Thumbnail().ok().and_then(|t| decode_art(&t));
            *art_cache = (key, art);
        }
        let art_rgba = art_cache.1.clone();
        let info = session.GetPlaybackInfo().ok()?;
        let playing = info.PlaybackStatus().ok()?
            == GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing;
        let timeline = session.GetTimelineProperties().ok()?;
        Some(MediaState {
            title: Some(title), artist: Some(artist), playing,
            position: secs(timeline.Position().ok()?),
            duration: secs(timeline.EndTime().ok()?),
            art_rgba,
        })
    }

    fn perform(manager: &Manager, action: Action) {
        let Ok(session) = manager.GetCurrentSession() else { return };
        let playing = session.GetPlaybackInfo().ok()
            .and_then(|i| i.PlaybackStatus().ok())
            .map(|s| s == GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing)
            .unwrap_or(false);
        let res = match action {
            Action::PlayPause => if playing { session.TryPauseAsync() } else { session.TryPlayAsync() },
            Action::Next => session.TrySkipNextAsync(),
            Action::Previous => session.TrySkipPreviousAsync(),
        };
        if let Ok(op) = res { let _ = op.get(); }
    }

    pub fn spawn() {
        std::thread::spawn(move || {
            unsafe { let _ = CoInitializeEx(None, COINIT_MULTITHREADED); }
            let Some(manager) = manager() else { return };
            let (tx, rx) = mpsc::channel::<Action>();
            let _ = ACTIONS.set(tx);
            let mut art_cache: (String, Option<(Vec<u8>, u32, u32)>) = (String::new(), None);
            let mut last: Option<MediaState> = None;
            loop {
                if let Ok(action) = rx.try_recv() { perform(&manager, action); }
                if let Some(mut st) = read_state(&manager, &mut art_cache) {
                    st.position = (st.position * 2.0).round() / 2.0;
                    let changed = match &last {
                        Some(p) => p.title != st.title || p.artist != st.artist
                            || p.playing != st.playing || (p.position - st.position).abs() > 0.4,
                        None => true,
                    };
                    if changed { set_last(Some(st.clone())); last = Some(st); }
                } else if last.is_some() { set_last(None); last = None; }
                std::thread::sleep(Duration::from_millis(400));
            }
        });
    }
}
