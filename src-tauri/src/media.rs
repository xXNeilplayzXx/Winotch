// Media session integration for the island.
//
// On Windows this reads the System Media Transport Controls (SMTC) feed — the
// same source the OS media flyout uses — and pushes the active track (title,
// artist, album art, position, playback state) to the frontend, plus play/pause
// and skip commands back to the app. On other platforms every entry point is a
// no-op, so the island keeps its "Nothing playing yet" empty state.

use serde::Serialize;
use std::sync::{mpsc, Mutex, OnceLock};

#[derive(Clone, Serialize, Default)]
pub struct MediaState {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub title: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub artist: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub album: Option<String>,
    /// Album art as a data URL (`data:image/...;base64,...`).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub art: Option<String>,
    pub playing: bool,
    pub position: f64,
    pub duration: f64,
}

#[derive(Clone, Copy, PartialEq)]
enum Action {
    PlayPause,
    Next,
    Previous,
}

/// Watcher thread consumes these, so WinRT calls always happen on the one
/// thread we know has an initialized COM apartment.
static ACTIONS: OnceLock<mpsc::Sender<Action>> = OnceLock::new();

/// Last snapshot we emitted, so the frontend can pull initial state on load
/// (it may mount after the watcher has already picked a session up).
/// Only the Windows watcher writes it, hence the allow on other platforms.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
static LAST: OnceLock<Mutex<Option<MediaState>>> = OnceLock::new();

#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn set_last(state: Option<MediaState>) {
    let cell = LAST.get_or_init(|| Mutex::new(None));
    if let Ok(mut guard) = cell.lock() {
        *guard = state;
    }
}

/// Pull the currently known media state (null when nothing is playing).
#[tauri::command]
pub fn media_get_state() -> Option<MediaState> {
    LAST.get()
        .and_then(|m| m.lock().ok())
        .and_then(|g| g.clone())
}

fn send_action(action: Action) {
    if let Some(tx) = ACTIONS.get() {
        let _ = tx.send(action);
    }
}

#[tauri::command]
pub fn media_play_pause() {
    send_action(Action::PlayPause);
}

#[tauri::command]
pub fn media_next() {
    send_action(Action::Next);
}

#[tauri::command]
pub fn media_previous() {
    send_action(Action::Previous);
}

/// Start the media watcher. No-op off Windows.
pub fn spawn(app: tauri::AppHandle) {
    #[cfg(target_os = "windows")]
    win::spawn(app);
    #[cfg(not(target_os = "windows"))]
    let _ = app;
}

#[cfg(target_os = "windows")]
mod win {
    use super::{set_last, Action, MediaState, ACTIONS};
    use base64::Engine;
    use std::sync::mpsc;
    use std::sync::OnceLock;
    use std::time::Duration;
    use tauri::Emitter;
    use windows::Foundation::TimeSpan;
    use windows::Media::Control::{
        GlobalSystemMediaTransportControlsSession, GlobalSystemMediaTransportControlsSessionManager,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus,
    };
    use windows::Storage::Streams::{DataReader, IRandomAccessStreamReference};
    use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};

    type Manager = GlobalSystemMediaTransportControlsSessionManager;

    fn secs(t: TimeSpan) -> f64 {
        t.Duration as f64 / 10_000_000.0
    }

    fn manager() -> Option<Manager> {
        static MANAGER: OnceLock<Manager> = OnceLock::new();
        if let Some(m) = MANAGER.get() {
            return Some(m.clone());
        }
        let m = GlobalSystemMediaTransportControlsSessionManager::RequestAsync()
            .ok()?
            .get()
            .ok()?;
        Some(MANAGER.get_or_init(|| m).clone())
    }

    /// Album art -> data URL. WinRT streams have to be drained manually.
    fn art_data_url(thumb: &IRandomAccessStreamReference) -> Option<String> {
        let stream = thumb.OpenReadAsync().ok()?.get().ok()?;
        let mime = stream.ContentType().ok()?.to_string();
        let size = stream.Size().ok()? as u32;
        if size == 0 || size > 4_000_000 {
            return None;
        }
        let reader = DataReader::CreateDataReader(&stream).ok()?;
        reader.LoadAsync(size).ok()?.get().ok()?;
        let mut buf = vec![0u8; size as usize];
        reader.ReadBytes(&mut buf).ok()?;
        Some(format!(
            "data:{mime};base64,{}",
            base64::engine::general_purpose::STANDARD.encode(buf)
        ))
    }

    fn read_state(
        manager: &Manager,
        art_cache: &mut (String, Option<String>),
    ) -> Option<MediaState> {
        let session: GlobalSystemMediaTransportControlsSession = manager.GetCurrentSession().ok()?;
        let props = session.TryGetMediaPropertiesAsync().ok()?.get().ok()?;

        let title = props.Title().ok()?.to_string();
        let artist = props.Artist().ok()?.to_string();
        let album = props.AlbumTitle().ok().map(|h| h.to_string());

        // Artwork only changes with the track, so cache it by track key.
        let key = format!("{title}|{artist}|{album:?}");
        if art_cache.0 != key {
            let art = props.Thumbnail().ok().and_then(|t| art_data_url(&t));
            *art_cache = (key, art);
        }
        let art = art_cache.1.clone();

        let info = session.GetPlaybackInfo().ok()?;
        let playing = info.PlaybackStatus().ok()?
            == GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing;

        let timeline = session.GetTimelineProperties().ok()?;
        let position = secs(timeline.Position().ok()?);
        let duration = secs(timeline.EndTime().ok()?);

        Some(MediaState {
            title: Some(title),
            artist: Some(artist),
            album,
            art,
            playing,
            position,
            duration,
        })
    }

    fn perform(manager: &Manager, action: Action) {
        let Ok(session) = manager.GetCurrentSession() else {
            return;
        };
        // SMTC has no toggle API, so read the current status and pick.
        let playing = session
            .GetPlaybackInfo()
            .ok()
            .and_then(|i| i.PlaybackStatus().ok())
            .map(|s| s == GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing)
            .unwrap_or(false);
        let res = match action {
            Action::PlayPause => {
                if playing {
                    session.TryPauseAsync()
                } else {
                    session.TryPlayAsync()
                }
            }
            Action::Next => session.TrySkipNextAsync(),
            Action::Previous => session.TrySkipPreviousAsync(),
        };
        // Fire-and-forget; the request is queued on success.
        if let Ok(op) = res {
            let _ = op.get();
        }
    }

    pub fn spawn(app: tauri::AppHandle) {
        std::thread::spawn(move || {
            // WinRT activation needs an initialized apartment on this thread.
            unsafe {
                let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
            }

            let Some(manager) = manager() else {
                return;
            };
            let (tx, rx) = mpsc::channel::<Action>();
            let _ = ACTIONS.set(tx);

            let mut art_cache: (String, Option<String>) = (String::new(), None);
            let mut last: Option<MediaState> = None;

            loop {
                if let Ok(action) = rx.try_recv() {
                    perform(&manager, action);
                }

                if let Some(mut st) = read_state(&manager, &mut art_cache) {
                    // Quantize position so playing tracks emit ~2x/second, and
                    // idle sessions emit nothing at all.
                    st.position = (st.position * 2.0).round() / 2.0;
                    let changed = match &last {
                        Some(p) => {
                            p.title != st.title
                                || p.artist != st.artist
                                || p.playing != st.playing
                                || p.art != st.art
                                || (p.position - st.position).abs() > 0.4
                        }
                        None => true,
                    };
                    if changed {
                        set_last(Some(st.clone()));
                        let _ = app.emit("media://state", Some(st.clone()));
                        last = Some(st);
                    }
                } else if last.is_some() {
                    set_last(None);
                    let _ = app.emit("media://state", None::<MediaState>);
                    last = None;
                }

                std::thread::sleep(Duration::from_millis(400));
            }
        });
    }
}
