//! In-memory clipboard history — ported from the reference C# Winotch
//! (ClipboardHistoryEntry / ClipboardHistoryStore). A watcher thread samples
//! the clipboard and keeps the latest 10 unique items in memory; every change
//! pushes the full list to the page as `winotch://clipboard`. Nothing is ever
//! written to disk — closing the app clears its private history.
//!
//! The reference listens for WM_CLIPBOARDUPDATE; a poll is used here instead
//! (same capture rules, no hidden window/message loop). Off Windows this is an
//! empty no-op history.

use serde::Serialize;
use std::sync::{Mutex, OnceLock};
use tauri::AppHandle;

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ClipboardEntry {
    pub id: u64,
    pub kind: String, // "text" | "files" | "image"
    pub text: Option<String>,
    pub files: Option<Vec<String>>,
    /// Base64 PNG thumbnail (~56px) — the raw bitmap never leaves the backend.
    pub thumb: Option<String>,
    pub ts: u64,
}

#[derive(Clone)]
pub struct RawEntry {
    pub entry: ClipboardEntry,
    pub raw: Vec<u8>,
}

struct Store {
    items: Vec<RawEntry>,
    next_id: u64,
    last_raw: Vec<u8>,
}

const CAP: usize = 10;

static STORE: OnceLock<Mutex<Store>> = OnceLock::new();

fn store() -> &'static Mutex<Store> {
    STORE.get_or_init(|| Mutex::new(Store {
        items: Vec::new(),
        next_id: 1,
        last_raw: Vec::new(),
    }))
}

fn public_list() -> Vec<ClipboardEntry> {
    store().lock().map(|s| s.items.iter().map(|i| i.entry.clone()).collect()).unwrap_or_default()
}

/// Insert a sample; returns true when it's a genuinely new clipboard state.
fn upsert(raw: Vec<u8>, entry: ClipboardEntry) -> bool {
    let mut s = match store().lock() { Ok(g) => g, Err(p) => p.into_inner() };
    if raw == s.last_raw { return false; }
    s.last_raw = raw.clone();
    s.items.insert(0, RawEntry { entry, raw });
    s.items.truncate(CAP);
    true
}

fn delete_id(id: u64) {
    if let Ok(mut s) = store().lock() {
        s.items.retain(|i| i.entry.id != id);
    }
}

fn find_raw(id: u64) -> Option<RawEntry> {
    store().lock().ok().and_then(|s| s.items.iter().find(|i| i.entry.id == id).cloned())
}

#[tauri::command]
pub fn get_clipboard_history() -> Vec<ClipboardEntry> { public_list() }

#[tauri::command]
pub fn clipboard_copy(id: u64) -> Vec<ClipboardEntry> {
    if let Some(item) = find_raw(id) {
        #[cfg(target_os = "windows")]
        win::write_back(&item);
        #[cfg(not(target_os = "windows"))]
        let _ = item;
    }
    public_list()
}

#[tauri::command]
pub fn clipboard_delete(id: u64) -> Vec<ClipboardEntry> {
    delete_id(id);
    public_list()
}

#[tauri::command]
pub fn clipboard_clear() -> Vec<ClipboardEntry> {
    if let Ok(mut s) = store().lock() {
        s.items.clear();
    }
    public_list()
}

/// Start the watcher thread. No-op off Windows.
pub fn spawn(app: AppHandle) {
    #[cfg(target_os = "windows")]
    win::spawn(app);
    #[cfg(not(target_os = "windows"))]
    { let _ = app; }
}

#[cfg(target_os = "windows")]
mod win {
    use super::{public_list, store, upsert, ClipboardEntry, RawEntry};
    use std::time::Duration;
    use tauri::{AppHandle, Emitter};
    use windows::Win32::Foundation::{HANDLE, HGLOBAL};
    use windows::Win32::System::DataExchange::{
        CloseClipboard, EmptyClipboard, GetClipboardData, OpenClipboard, SetClipboardData,
    };
    use windows::Win32::System::Memory::{
        GlobalAlloc, GlobalLock, GlobalSize, GlobalUnlock, GMEM_MOVEABLE,
    };
    use windows::Win32::UI::Shell::{DragQueryFileW, HDROP};

    // Standard clipboard formats (kept local to avoid the heavy Ole feature).
    const CF_UNICODETEXT: u32 = 13;
    const CF_HDROP: u32 = 15;
    const CF_DIB: u32 = 8;

    fn with_clipboard<T>(f: impl FnOnce() -> Option<T>) -> Option<T> {
        for _ in 0..4 {
            if unsafe { OpenClipboard(None) }.is_ok() {
                let out = f();
                unsafe { let _ = CloseClipboard(); }
                return out;
            }
            std::thread::sleep(Duration::from_millis(25));
        }
        None
    }

    /// Raw bytes of a clipboard handle (for dedupe + image storage).
    fn handle_bytes(h: HANDLE) -> Option<Vec<u8>> {
        let hg = HGLOBAL(h.0);
        let p = unsafe { GlobalLock(hg) };
        if p.is_null() { return None; }
        let size = unsafe { GlobalSize(hg) };
        let mut buf = vec![0u8; size];
        unsafe {
            std::ptr::copy_nonoverlapping(p as *const u8, buf.as_mut_ptr(), size);
            let _ = GlobalUnlock(hg);
        }
        Some(buf)
    }

    fn read_text() -> Option<(Vec<u8>, String)> {
        let h = unsafe { GetClipboardData(CF_UNICODETEXT) }.ok()?;
        if h.is_invalid() { return None; }
        let raw = handle_bytes(h)?;
        if raw.len() < 2 { return None; }
        let mut units: Vec<u16> = raw.chunks_exact(2)
            .map(|c| u16::from_le_bytes([c[0], c[1]]))
            .collect();
        while units.last() == Some(&0) { units.pop(); }
        let text = String::from_utf16_lossy(&units);
        let text = text.trim().to_string();
        if text.is_empty() { return None; }
        Some((raw, text))
    }

    fn read_files() -> Option<(Vec<u8>, Vec<String>)> {
        let h = unsafe { GetClipboardData(CF_HDROP) }.ok()?;
        if h.is_invalid() { return None; }
        let raw = handle_bytes(h)?;
        let hd = HDROP(h.0);
        let count = unsafe { DragQueryFileW(hd, u32::MAX, None) };
        if count == 0 { return None; }
        let mut files = Vec::new();
        for i in 0..count {
            let len = unsafe { DragQueryFileW(hd, i, None) };
            if len == 0 { continue; }
            let mut buf = vec![0u16; (len + 1) as usize];
            unsafe { DragQueryFileW(hd, i, Some(&mut buf)); }
            let mut name = String::from_utf16_lossy(&buf);
            while name.ends_with('\0') { name.pop(); }
            if !name.is_empty() { files.push(name); }
        }
        if files.is_empty() { return None; }
        Some((raw, files))
    }

    /// CF_DIB → small RGBA PNG thumbnail (24/32bpp BI_RGB only).
    fn read_image() -> Option<(Vec<u8>, String)> {
        let h = unsafe { GetClipboardData(CF_DIB) }.ok()?;
        if h.is_invalid() { return None; }
        let raw = handle_bytes(h)?;
        if raw.len() < 40 { return None; }
        let rd = |o: usize| -> i32 { i32::from_le_bytes(raw[o..o + 4].try_into().unwrap()) };
        let w = rd(4);
        let hgt = rd(8);
        let bpp = u16::from_le_bytes([raw[14], raw[15]]);
        let comp = rd(16);
        if w <= 0 || hgt == 0 || w > 4096 || hgt.abs() > 4096 || comp != 0 { return None; }
        let stride = match bpp {
            24 => (w as usize * 3 + 3) & !3,
            32 => w as usize * 4,
            _ => return None,
        };
        let pix_start = 40usize;
        let bottom_up = hgt > 0;
        let height = hgt.unsigned_abs() as usize;
        if pix_start + stride * height > raw.len() { return None; }
        let mut rgba = Vec::with_capacity(w as usize * height * 4);
        for y in 0..height {
            let src_y = if bottom_up { height - 1 - y } else { y };
            let row = &raw[pix_start + src_y * stride..pix_start + src_y * stride + w as usize * (bpp as usize)];
            for px in row.chunks_exact(bpp as usize) {
                let (b, g, r) = (px[0], px[1], px[2]);
                rgba.extend_from_slice(&[r, g, b, 255]);
            }
        }
        let img = image::RgbaImage::from_raw(w as u32, height as u32, rgba)?;
        let thumb = image::imageops::resize(&img, 56, 56, image::imageops::FilterType::Triangle);
        let mut png = std::io::Cursor::new(Vec::new());
        image::DynamicImage::ImageRgba8(thumb)
            .write_to(&mut png, image::ImageFormat::Png)
            .ok()?;
        use base64::Engine as _;
        Some((raw, base64::engine::general_purpose::STANDARD.encode(png.into_inner())))
    }

    /// Build a clipboard sample for the current contents, if any.
    fn sample(ts: u64) -> Option<(Vec<u8>, ClipboardEntry)> {
        with_clipboard(|| {
            if let Some((raw, text)) = read_text() {
                return Some((raw, ClipboardEntry {
                    id: 0, kind: "text".into(), text: Some(text), files: None, thumb: None, ts,
                }));
            }
            if let Some((raw, files)) = read_files() {
                return Some((raw, ClipboardEntry {
                    id: 0, kind: "files".into(), text: None, files: Some(files), thumb: None, ts,
                }));
            }
            read_image().map(|(raw, thumb)| (raw, ClipboardEntry {
                id: 0, kind: "image".into(), text: None, files: None, thumb: Some(thumb), ts,
            }))
        })
    }

    /// Re-push the stored content onto the system clipboard.
    pub fn write_back(item: &RawEntry) {
        let _ = with_clipboard(|| {
            unsafe { let _ = EmptyClipboard(); }
            match item.entry.kind.as_str() {
                "text" => {
                    if let Some(text) = &item.entry.text {
                        let wide: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
                        let bytes = wide.len() * 2;
                        let h = unsafe { GlobalAlloc(GMEM_MOVEABLE, bytes) }.ok()?;
                        let p = unsafe { GlobalLock(h) };
                        if p.is_null() { return None; }
                        unsafe {
                            std::ptr::copy_nonoverlapping(wide.as_ptr() as *const u8, p as *mut u8, bytes);
                            let _ = GlobalUnlock(h);
                        }
                        if unsafe { SetClipboardData(CF_UNICODETEXT, HANDLE(h.0)) }.is_err() {
                            return None;
                        }
                    }
                }
                "files" => {
                    if let Some(files) = &item.entry.files {
                        let mut payload: Vec<u8> = Vec::new();
                        // DROPFILES: pFiles(4) + pt(4) + fNC(4) + wide paths, double-NUL
                        payload.extend_from_slice(&20u32.to_le_bytes());
                        payload.extend_from_slice(&0u32.to_le_bytes());
                        payload.extend_from_slice(&0u32.to_le_bytes());
                        let mut wrote = false;
                        for f in files {
                            for u in f.encode_utf16() { payload.extend_from_slice(&u.to_le_bytes()); }
                            payload.extend_from_slice(&0u16.to_le_bytes());
                            wrote = true;
                        }
                        payload.extend_from_slice(&0u16.to_le_bytes());
                        if !wrote { return None; }
                        let h = unsafe { GlobalAlloc(GMEM_MOVEABLE, payload.len()) }.ok()?;
                        let p = unsafe { GlobalLock(h) };
                        if p.is_null() { return None; }
                        unsafe {
                            std::ptr::copy_nonoverlapping(payload.as_ptr(), p as *mut u8, payload.len());
                            let _ = GlobalUnlock(h);
                        }
                        if unsafe { SetClipboardData(CF_HDROP, HANDLE(h.0)) }.is_err() {
                            return None;
                        }
                    }
                }
                "image" => {
                    let h = unsafe { GlobalAlloc(GMEM_MOVEABLE, item.raw.len()) }.ok()?;
                    let p = unsafe { GlobalLock(h) };
                    if p.is_null() { return None; }
                    unsafe {
                        std::ptr::copy_nonoverlapping(item.raw.as_ptr(), p as *mut u8, item.raw.len());
                        let _ = GlobalUnlock(h);
                    }
                    if unsafe { SetClipboardData(CF_DIB, HANDLE(h.0)) }.is_err() {
                        return None;
                    }
                }
                _ => return None,
            }
            Some(())
        });
    }

    pub fn spawn(app: AppHandle) {
        std::thread::spawn(move || {
            let mut last_sig: Option<Vec<u8>> = None;
            loop {
                let ts = std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .map(|d| d.as_secs())
                    .unwrap_or(0);
                if let Some((raw, mut entry)) = sample(ts) {
                    let id = store().lock().map(|s| s.next_id).unwrap_or(1);
                    entry.id = id;
                    if upsert(raw, entry) {
                        if let Ok(mut s) = store().lock() { s.next_id += 1; }
                        let list = public_list();
                        let sig: Vec<u8> = list.iter().flat_map(|e| e.id.to_le_bytes()).collect();
                        if last_sig.as_ref() != Some(&sig) {
                            last_sig = Some(sig);
                            let _ = app.emit("winotch://clipboard", &list);
                        }
                    }
                }
                std::thread::sleep(Duration::from_millis(1200));
            }
        });
    }
}