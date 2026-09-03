//! Winotch Native — standalone notch for Windows. No WebView2, no browser
//! engine, no runtime installs: one self-contained exe.
//!
//! Ported from the Tauri/HTML app (designv2): idle pill, widget panel,
//! settings deck with Apply/Reset, themes, live system stats, SMTC media,
//! weather, notes, app launcher.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod autostart;
mod config;
mod launcher;
mod media;
mod stats;
mod theme;
mod tray;
mod ui_style;
mod weather;

use eframe::egui;
use config::Config;
use egui::{
    Align, Align2, Color32, CornerRadius, FontId, Frame, Pos2, Rect, RichText,
    Sense, Stroke, Vec2, ViewportCommand,
};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

const PILL_H: f32 = 36.0;
const PANEL_MIN_H: f32 = 220.0;
const PANEL_MAX_H: f32 = 560.0;
const EXPAND_EXTRA_W: f32 = 130.0;

// ---------- palette helpers ----------
fn c3(rgb: [u8; 3]) -> Color32 { Color32::from_rgb(rgb[0], rgb[1], rgb[2]) }
fn alpha(rgb: [u8; 3], a: u8) -> Color32 { Color32::from_rgba_unmultiplied(rgb[0], rgb[1], rgb[2], a) }

fn main() -> eframe::Result {
    let mut cfg = Config::load();
    media::spawn();
    let startup_on = autostart::enabled();
    cfg.autostart = startup_on;

    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title("Winotch")
            .with_decorations(false)
            .with_transparent(true)
            .with_always_on_top()
            .with_resizable(false)
            .with_taskbar(false)
            .with_inner_size(Vec2::new(cfg.width as f32, PILL_H)),
        ..Default::default()
    };

    let tray = tray::init(startup_on);

    eframe::run_native(
        "Winotch",
        options,
        Box::new(move |cc| {
            let mut fonts = egui::FontDefinitions::default();
            // UI font: Segoe UI Variable / Segoe UI — the designv2 --ui stack.
            for path in [
                "C:\\Windows\\Fonts\\segoeui.ttf",
                "/System/Library/Fonts/SFNS.ttf",
                "/System/Library/Fonts/Helvetica.ttc",
            ] {
                if let Ok(data) = std::fs::read(path) {
                    fonts.font_data.insert("ui".into(), std::sync::Arc::new(egui::FontData::from_owned(data)));
                    fonts.families.entry(egui::FontFamily::Proportional).or_default().insert(0, "ui".into());
                    break;
                }
            }
            // Monospace: Cascadia Mono / Consolas — the designv2 --mono stack.
            for path in [
                "C:\\Windows\\Fonts\\cascadiamono-Regular.ttf",
                "C:\\Windows\\Fonts\\consola.ttf",
                "/System/Library/Fonts/Menlo.ttc",
            ] {
                if let Ok(data) = std::fs::read(path) {
                    fonts.font_data.insert("mono".into(), std::sync::Arc::new(egui::FontData::from_owned(data)));
                    fonts.families.entry(egui::FontFamily::Monospace).or_default().push("mono".into());
                    break;
                }
            }
            cc.egui_ctx.set_fonts(fonts);
            Ok(Box::new(App::new(cfg, tray)))
        }),
    )
}

struct App {
    cfg: Config,
    draft: Config,
    expanded: bool,
    settings_open: bool,
    setup_open: bool,
    quit_requested: bool,
    tray: tray::Tray,
    anim: f32,
    stats: stats::SystemStats,
    last_stats: Instant,
    weather: Option<weather::Weather>,
    weather_city: String,
    weather_err: String,
    last_weather: Instant,
    weather_tried: bool,
    media_art: Option<egui::TextureHandle>,
    art_key: String,
    toast: Option<(String, Instant)>,
    drag_off: Option<Vec2>,
    launcher_input: String,
    note_input: String,
    city_input: String,
    /// Window top-left anchor (screen points). The panel hangs from here.
    anchor: Pos2,
    /// Body content height measured on the previous expanded frame.
    last_body_h: f32,
    first_frame: bool,
    /// Which settings sections are expanded (designv2 accordion).
    deck_open: Vec<&'static str>,
    /// Explicit wizard preset selection (None until the user clicks one).
    wizard_pick: Option<&'static str>,
}

impl App {
    fn new(cfg: Config, tray: tray::Tray) -> Self {
        let note_input = cfg.notes.clone();
        let city_input = cfg.weather_city.clone();
        let draft = cfg.clone();
        let setup_open = cfg.config_is_fresh;
        Self {
            draft,
            cfg,
            // first run: open straight into the wizard
            expanded: setup_open,
            settings_open: false,
            setup_open,
            quit_requested: false,
            anim: 0.0,
            stats: Default::default(),
            last_stats: Instant::now() - Duration::from_secs(10),
            weather: None,
            weather_city: String::new(),
            weather_err: String::new(),
            last_weather: Instant::now() - Duration::from_secs(3600),
            weather_tried: false,
            media_art: None,
            art_key: String::new(),
            toast: None,
            drag_off: None,
            launcher_input: String::new(),
            note_input,
            city_input,
            tray,
            anchor: Pos2::ZERO,
            last_body_h: 320.0,
            first_frame: true,
            deck_open: Vec::new(),
            wizard_pick: None,
        }
    }

    fn pill_width(&self) -> f32 { self.cfg.width as f32 }

    fn expanded_size(&self, screen: Option<Vec2>) -> Vec2 {
        let w = (self.pill_width() + EXPAND_EXTRA_W).max(460.0);
        // Hug the measured content (head 56px + body), clamped to the screen
        // space below the anchor so nothing ever hangs off-screen.
        let mut h = (self.last_body_h + 56.0).clamp(PANEL_MIN_H, PANEL_MAX_H);
        if let Some(s) = screen {
            h = h.min((s.y - self.anchor.y - 12.0).max(PANEL_MIN_H));
        }
        Vec2::new(w, h)
    }

    fn target_size(&self, screen: Option<Vec2>) -> Vec2 {
        if self.expanded { self.expanded_size(screen) } else { Vec2::new(self.pill_width(), PILL_H) }
    }

    fn flash(&mut self, msg: &str) {
        self.toast = Some((msg.to_string(), Instant::now()));
    }
}

impl eframe::App for App {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        let pal = theme::resolve(&self.cfg);
        ui_style::apply_egui_style(&ctx, &pal);

        // ---- tray menu commands ----
        for cmd in tray::poll(&self.tray) {
            match cmd {
                tray::Cmd::Show => {
                    self.expanded = true;
                    ctx.send_viewport_cmd(ViewportCommand::Visible(true));
                    ctx.send_viewport_cmd(ViewportCommand::Focus);
                }
                tray::Cmd::ToggleAutostart => {
                    let next = !autostart::enabled();
                    match autostart::set(next) {
                        Ok(()) => {
                            self.cfg.autostart = next;
                            self.cfg.save();
                            self.tray.sync_autostart(autostart::enabled());
                            self.flash(if next { "Will start with Windows" } else { "Autostart off" });
                        }
                        Err(e) => self.flash(&format!("autostart failed: {e}")),
                    }
                }
                tray::Cmd::Quit => {
                    self.quit_requested = true;
                }
            }
        }
        if self.quit_requested {
            ctx.send_viewport_cmd(ViewportCommand::Close);
            return;
        }

        // ---- background pollers (stats 1s, weather 10min, toast expiry) ----
        if self.last_stats.elapsed() > Duration::from_secs(1) {
            self.stats = stats::poll();
            self.last_stats = Instant::now();
        }
        if !self.weather_tried {
            self.weather_tried = true;
            let cfg_city = self.cfg.weather_city.clone();
            if cfg_city.is_empty() {
                match weather::os_geo() {
                    Ok((lat, lon)) => {
                        if let Some(w) = weather::fetch(lat, lon) {
                            self.weather = Some(w);
                            self.weather_city = "Current location".into();
                        }
                    }
                    Err(e) => self.weather_err = e,
                }
            } else {
                if let Some((lat, lon, name)) = weather::geocode(&cfg_city) {
                    self.weather = weather::fetch(lat, lon);
                    self.weather_city = name;
                }
            }
            self.last_weather = Instant::now();
        }
        if self.last_weather.elapsed() > Duration::from_secs(600) {
            self.weather_tried = false; // re-trigger on next frame
            self.last_weather = Instant::now();
        }
        if let Some((_, at)) = &self.toast {
            if at.elapsed() > Duration::from_millis(2200) { self.toast = None; }
        }

        // ---- window size spring + clock repaint ----
        let screen = ctx.input(|i| i.viewport().monitor_size);
        let target = self.target_size(screen);
        let cur = ctx.input(|i| i.viewport().inner_rect.map(|r| r.size()));
        let goal = if self.expanded { 1.0 } else { 0.0 };
        let animating = (self.anim - goal).abs() > 0.002;
        if animating {
            self.anim += (goal - self.anim) * 0.18;
        } else {
            self.anim = goal;
        }
        if let Some(cur) = cur {
            if (cur.x - target.x).abs() > 0.5 || (cur.y - target.y).abs() > 0.5 {
                ctx.send_viewport_cmd(ViewportCommand::InnerSize(target));
            }
        }
        // live clock / media position / toast need steady frames when visible
        if self.expanded || animating || self.toast.is_some() {
            ctx.request_repaint_after(Duration::from_millis(250));
        }

        // ---- drag handling (idle pill only) ----
        let allow_drag = !self.expanded;

        // ---- paint ----
        egui::CentralPanel::default()
            .frame(Frame::NONE)
            .show(ui, |ui| {
                let size = target;
                let rect = Rect::from_min_size(Pos2::ZERO, size);
                let painter = ui.painter();
                let expanded = self.expanded;

                // island body
                let radius = if expanded {
                    CornerRadius::same(14)
                } else if self.cfg.shape_notch {
                    CornerRadius { nw: 0, ne: 0, sw: 18, se: 18 }
                } else {
                    CornerRadius::same(18)
                };
                let opa = if self.cfg.glass {
                    (self.cfg.opacity as f32 / 100.0 * 235.0) as u8
                } else {
                    245
                };
                let body = if self.cfg.glass {
                    alpha(pal.panel, opa)
                } else {
                    c3(pal.panel)
                };
                painter.rect_filled(rect, radius, body);
                // gradient overlay (designv2 gradients, vertex mesh)
                if self.cfg.gradient != "none" {
                    ui_style::gradient_mesh(painter, rect, radius, &self.cfg.gradient, &pal);
                }
                painter.rect_stroke(rect, radius, Stroke::new(1.0, alpha(pal.text, 26)), egui::StrokeKind::Outside);

                if !expanded {
                    // ================= IDLE PILL =================
                    let mid = rect.center();
                    let accent = c3(pal.accent);

                    match self.cfg.idle_content.as_str() {
                        "brand" => {
                            painter.text(mid, Align2::CENTER_CENTER, "WINOTCH", FontId::monospace(11.0), c3(pal.text2));
                        }
                        "custom" => {
                            let txt = if self.cfg.custom_text.is_empty() { "…" } else { &self.cfg.custom_text };
                            painter.text(mid, Align2::CENTER_CENTER, txt, FontId::monospace(13.0), c3(pal.text));
                        }
                        "stats" => {
                            let s = &self.stats;
                            let chip = |x: f32, label: &str, val: String| {
                                painter.text(Pos2::new(x, mid.y), Align2::LEFT_CENTER, label, FontId::monospace(9.0), c3(pal.text3));
                                painter.text(Pos2::new(x + 30.0, mid.y), Align2::LEFT_CENTER, val, FontId::monospace(11.0), c3(pal.text));
                            };
                            let mut x = rect.left() + 14.0;
                            if self.cfg.sys_cpu {
                                painter.circle_filled(Pos2::new(x + 3.0, mid.y), 3.0, accent);
                                x += 12.0;
                                chip(x, "CPU", format!("{}%", self.stats.cpu.round() as i32));
                                x += 62.0;
                            }
                            if self.cfg.sys_mem {
                                chip(x, "MEM", format!("{}%", ((s.mem_used_gb / s.mem_total_gb.max(0.1)) * 100.0).round() as i32));
                                x += 62.0;
                            }
                            if self.cfg.sys_bat {
                                if let Some(b) = &s.battery {
                                    chip(x, "BAT", format!("{}%", b.pct));
                                }
                            }
                        }
                        _ => {
                            // clock (default): LED + brand + divider + HH:MM(:SS)
                            painter.circle_filled(Pos2::new(rect.left() + 16.0, mid.y), 4.0, alpha(pal.text3, 160));
                            painter.circle_filled(Pos2::new(rect.left() + 16.0, mid.y), 4.0, accent);
                            painter.text(Pos2::new(rect.left() + 26.0, mid.y), Align2::LEFT_CENTER, "WINOTCH", FontId::monospace(10.0), c3(pal.text2));
                            let div_x = rect.left() + 92.0;
                    painter.line_segment(
                        [Pos2::new(div_x, mid.y - 7.0), Pos2::new(div_x, mid.y + 7.0)],
                        Stroke::new(1.0, alpha(pal.text, 15)),
                    );
                            let now = clock_hm();
                            painter.text(Pos2::new(div_x + 12.0, mid.y), Align2::LEFT_CENTER, &now, FontId::monospace(14.0), c3(pal.text));
                        }
                    }

                    // interactions: click expand, drag move
                    let resp = ui.interact(rect, egui::Id::new("pill"), Sense::click_and_drag());
                    if resp.drag_started() {
                        if let Some(p) = ctx.input(|i| i.pointer.latest_pos()) {
                            let win_pos = ctx.input(|i| i.viewport().inner_rect.map(|r| r.min).unwrap_or_default());
                            self.drag_off = Some(p - win_pos);
                        }
                    }
                    if resp.dragged() {
                        if let (Some(off), Some(p)) = (self.drag_off, ctx.input(|i| i.pointer.latest_pos())) {
                            let target_pos = p - off;
                            let _ = allow_drag;
                            ctx.send_viewport_cmd(ViewportCommand::OuterPosition(target_pos));
                            self.cfg.pos_mode = "free".into();
                            self.cfg.pos_x = target_pos.x;
                        }
        }
                    if resp.drag_stopped() {
                        self.drag_off = None;
                        self.cfg.save();
                    }
                    if resp.clicked() || self.first_frame {
                        self.first_frame = false;
                        if let Some(win_min) = ctx.input(|i| i.viewport().inner_rect.map(|r| r.min)) {
                            self.anchor = win_min;
                        }
                        self.expanded = true;
                    }
                } else {
                    // ================= EXPANDED PANEL =================
                    let body_start = Rect::from_min_size(Pos2::ZERO, Vec2::new(size.x, size.y));
                    self.draw_panel(ui, body_start, pal);
                    // measure what we actually painted (content cursor bottom)
                    if let Some(win_min) = ctx.input(|i| i.viewport().inner_rect.map(|r| r.min)) {
                        if self.anchor == Pos2::ZERO {
                            self.anchor = win_min;
                        }
                    }
                }
            });

        // ---- toast ----
        if let Some((msg, _)) = &self.toast {
            let Some(screen_rect) = ctx.input(|i| i.raw.screen_rect) else { return };
            let painter = ctx.debug_painter();
            ui_style::toast_pill(
                &painter,
                Pos2::new(screen_rect.center().x, screen_rect.bottom() - 40.0),
                msg,
                &pal,
            );
        }
    }
}

impl App {
    fn draw_panel(&mut self, ui: &mut egui::Ui, rect: Rect, pal: theme::Palette) {
        let ctx = ui.ctx().clone();
        // Keep the OS window pinned at the anchor (egui resizes can drift it).
        if let Some(win_min) = ctx.input(|i| i.viewport().inner_rect.map(|r| r.min)) {
            if (win_min - self.anchor).length() > 1.0 && self.anchor != Pos2::ZERO {
                ctx.send_viewport_cmd(ViewportCommand::OuterPosition(self.anchor));
            }
        }

        // ---- head: LED + tracked brand left, drawn gear + X right, hairline below ----
        let painter = ui.painter();
        let accent = c3(pal.accent);
        let head_h = 44.0;
        let head_mid = rect.top() + head_h / 2.0;
        painter.circle_filled(Pos2::new(rect.left() + 18.0, head_mid), 3.5, accent);
        ui_style::tracked(painter, Pos2::new(rect.left() + 30.0, head_mid), Align2::LEFT_CENTER,
            "WINOTCH", 10.0, c3(pal.text2), 2.2);
        let gear_r = Rect::from_center_size(Pos2::new(rect.right() - 44.0, head_mid), Vec2::splat(26.0));
        let close_r = Rect::from_center_size(Pos2::new(rect.right() - 17.0, head_mid), Vec2::splat(26.0));
        let gear = ui.interact(gear_r, egui::Id::new("gear"), Sense::click());
        let close = ui.interact(close_r, egui::Id::new("close"), Sense::click());
        // hover fills + active state (designv2 .icon-btn)
        if gear.hovered() {
            painter.rect_filled(gear_r, CornerRadius::same(8), alpha(pal.text, if self.settings_open { 20 } else { 14 }));
        }
        if self.settings_open {
            painter.rect_filled(gear_r, CornerRadius::same(8), alpha(pal.accent, 26));
        }
        if close.hovered() {
            painter.rect_filled(close_r, CornerRadius::same(8), alpha(pal.text, 14));
        }
        let gear_col = if self.settings_open || gear.hovered() { accent } else { c3(pal.text3) };
        ui_style::draw_gear(painter, gear_r.center(), 6.0, gear_col);
        let x_col = if close.hovered() { c3(pal.text) } else { c3(pal.text3) };
        ui_style::draw_x(painter, close_r.center(), 5.0, x_col);
        painter.line_segment(
            [Pos2::new(rect.left(), rect.top() + head_h), Pos2::new(rect.right(), rect.top() + head_h)],
            Stroke::new(1.0, alpha([255, 255, 255], 15)),
        );
        if close.clicked() { self.expanded = false; }
        if gear.clicked() { self.settings_open = !self.settings_open; self.setup_open = false; }

        // ---- body: an allocating child so we can measure natural height ----
        let body_top = rect.top() + head_h + 4.0;
        let avail = rect.bottom() - body_top;
        let (content_h, _setup, _settings_open) = if self.setup_open {
            let r = self.setup_body(ui, Vec2::new(rect.width(), avail), pal);
            (r, true, false)
        } else if self.settings_open {
            let r = self.settings_body(ui, Vec2::new(rect.width(), avail), pal);
            (r, false, true)
        } else {
            let r = self.widgets_body(ui, Vec2::new(rect.width(), avail), pal);
            (r, false, false)
        };
        // ---- sticky feet (settings / wizard) ----
        if self.settings_open {
            egui::Frame::NONE
                .inner_margin(egui::Margin::symmetric(14, 6))
                .show(ui, |ui| { self.settings_foot(ui, pal); });
        } else if self.setup_open {
            egui::Frame::NONE
                .inner_margin(egui::Margin::symmetric(18, 6))
                .show(ui, |ui| { self.setup_foot(ui, pal); });
        }

        // window hugs content (clamped inside expanded_size)
        self.last_body_h = content_h.max(0.0);
        let _ = _setup;
        let _ = _settings_open;
    }

    /// Widget stack. Returns the natural content height (unclipped).
    fn widgets_body(&mut self, ui: &mut egui::Ui, avail: Vec2, pal: theme::Palette) -> f32 {
        let inner = egui::ScrollArea::vertical()
            .id_salt("widgets-scroll")
            .show(ui, |ui| {
                ui.set_min_width(avail.x);
                egui::Frame::NONE.inner_margin(egui::Margin::symmetric(14, 12)).show(ui, |ui| {
                    let stats = self.stats.clone();
                    let widgets = self.cfg.widgets.clone();
                    for wid in widgets {
                        match wid.as_str() {
                            "clock" => self.widget_clock(ui, pal),
                            "system" => self.widget_system(ui, pal, &stats),
                            "media" => self.widget_media(ui, pal),
                            "weather" => self.widget_weather(ui, pal),
                            "notes" => self.widget_notes(ui, pal),
                            "launcher" => self.widget_launcher(ui, pal),
                            _ => {}
                        }
                        ui.add_space(10.0);
                    }
                });
            });
        inner.content_size.y + 8.0
    }

    fn settings_body(&mut self, ui: &mut egui::Ui, avail: Vec2, pal: theme::Palette) -> f32 {
        let inner = egui::ScrollArea::vertical()
            .id_salt("settings-scroll")
            .show(ui, |ui| {
                ui.set_min_width(avail.x);
                self.settings_rows(ui, pal);
            });
        inner.content_size.y + 8.0
    }

    fn setup_body(&mut self, ui: &mut egui::Ui, avail: Vec2, pal: theme::Palette) -> f32 {
        let inner = egui::ScrollArea::vertical()
            .id_salt("setup-scroll")
            .show(ui, |ui| {
                ui.set_min_width(avail.x);
                self.setup_rows(ui, pal);
            });
        inner.content_size.y + 8.0
    }

    // ---------- widgets ----------


    fn widget_clock(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        card(ui, pal, "Clock", |ui| {
            let now = clock_hms();
            ui.label(RichText::new(now.0).monospace().size(30.0).color(c3(pal.text)));
            ui.label(RichText::new(now.1).monospace().size(10.0).color(c3(pal.text3)));
        });
    }

    fn widget_system(&mut self, ui: &mut egui::Ui, pal: theme::Palette, s: &stats::SystemStats) {
        card(ui, pal, "System", |ui| {
            let row = |ui: &mut egui::Ui, label: &str, pct: f32, val: String, hot: bool| {
                ui.horizontal(|ui| {
                    ui.spacing_mut().item_spacing.x = 10.0;
                    // label: fixed 52px, tracked mono
                    let lab_w = 52.0;
                    let (lr, _) = ui.allocate_exact_size(Vec2::new(lab_w, 16.0), Sense::hover());
                    ui_style::tracked(ui.painter(), Pos2::new(lr.left(), lr.center().y), Align2::LEFT_CENTER,
                        label, 9.0, c3(pal.text3), 1.1);
                    // bar: gradient fill accent->led (warn->alarm when hot)
                    let bar_w = ui.available_width() - 70.0;
                    let (br, _) = ui.allocate_exact_size(Vec2::new(bar_w, 4.0), Sense::hover());
                    let p = ui.painter();
                    p.rect_filled(br, CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX }, alpha(pal.text, 14));
                    let frac = (pct / 100.0).clamp(0.0, 1.0);
                    if frac > 0.0 {
                        let fill_rect = Rect::from_min_size(br.min, Vec2::new((bar_w * frac).max(3.0), 4.0));
                        let c1 = if hot { c3(pal.warn) } else { c3(pal.accent) };
                        let c2 = if hot { c3(pal.alarm) } else { c3(pal.led_strong) };
                        // simple 3-band gradient approximation
                        let band = fill_rect.width() / 3.0;
                        for k in 0..3 {
                            let t = k as f32 / 2.0;
                            let col = ui_style::alpha(
                                [ (c1.r() as f32 + (c2.r() as f32 - c1.r() as f32) * t) as u8,
                                  (c1.g() as f32 + (c2.g() as f32 - c1.g() as f32) * t) as u8,
                                  (c1.b() as f32 + (c2.b() as f32 - c1.b() as f32) * t) as u8 ], 255);
                            p.rect_filled(
                                Rect::from_min_size(Pos2::new(fill_rect.left() + band * k as f32, fill_rect.top()), Vec2::new(band.ceil(), 4.0)),
                                CornerRadius::same(2), col);
                        }
                    }
                    // value: right, tabular
                    let (vr, _) = ui.allocate_exact_size(Vec2::new(62.0, 16.0), Sense::hover());
                    ui_style::tracked(ui.painter(), Pos2::new(vr.right(), vr.center().y), Align2::RIGHT_CENTER,
                        &val, 10.5, c3(pal.text), 0.4);
                });
            };
            if self.cfg.sys_cpu { row(ui, "CPU", s.cpu, format!("{}%", s.cpu.round() as i32), s.cpu > 82.0); }
            if self.cfg.sys_mem {
                let pct = (s.mem_used_gb / s.mem_total_gb.max(0.1)) * 100.0;
                row(ui, "MEM", pct, format!("{:.1}/{:.0}G", s.mem_used_gb, s.mem_total_gb), false);
            }
            if self.cfg.sys_bat {
                if let Some(b) = &s.battery {
                    row(ui, "BAT", b.pct as f32, format!("{}%{}", b.pct, if b.charging { " ⚡" } else { "" }), b.pct < 20);
                }
            }
            if self.cfg.sys_temp {
                if let Some(t) = s.temp_c {
                    row(ui, "TEMP", (t - 35.0) / 55.0 * 100.0, format!("{}°", t.round() as i32), t > 75.0);
                }
            }
            if self.cfg.sys_net {
                if let Some(n) = &s.network {
                    let fmt = |v: f64| if v > 1024.0 { format!("{:.1}M", v / 1024.0) } else { format!("{:.0}k", v) };
                    row(ui, "NET", 45.0, format!("↓{} ↑{}", fmt(n.down_bytes_per_sec), fmt(n.up_bytes_per_sec)), false);
                }
            }
        });
    }

    fn widget_media(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        let m = media::get();
        card(ui, pal, "Now playing", |ui| {
            match m {
                None => { ui.label(RichText::new("Nothing playing yet").monospace().size(11.0).color(c3(pal.text3))); }
                Some(m) => {
                    // album art
                    if let Some((rgba, w, h)) = &m.art_rgba {
                        let key = format!("{}|{}", m.title.clone().unwrap_or_default(), m.artist.clone().unwrap_or_default());
                        if self.art_key != key || self.media_art.is_none() {
                            let img = egui::ColorImage::from_rgba_unmultiplied([*w as usize, *h as usize], rgba);
                            self.media_art = Some(ui.ctx().load_texture("art", img, Default::default()));
                            self.art_key = key;
                        }
                    } else {
                        self.media_art = None;
                    }
                    ui.horizontal(|ui| {
                        if let Some(tex) = &self.media_art {
                            ui.add(egui::Image::new((tex.id(), Vec2::splat(44.0))).corner_radius(CornerRadius::same(8)));
                        }
                        ui.vertical(|ui| {
                            ui.label(RichText::new(m.title.clone().unwrap_or_default()).size(13.0).strong().color(c3(pal.text)));
                            ui.label(RichText::new(m.artist.clone().unwrap_or_default()).monospace().size(10.0).color(c3(pal.text3)));
                        });
                    });
                    // progress
                    if m.duration > 0.0 {
                        let pct = (m.position / m.duration).clamp(0.0, 1.0) as f32;
                        ui.add_space(4.0);
                        let bar_w = ui.available_width();
                        let (r, _) = ui.allocate_exact_size(Vec2::new(bar_w, 3.0), Sense::hover());
                        ui.painter().rect_filled(r, CornerRadius::same(2), alpha([128, 128, 128], 40));
                        ui.painter().rect_filled(
                            Rect::from_min_size(r.min, Vec2::new(bar_w * pct, 3.0)),
                            CornerRadius::same(2), c3(pal.accent),
                        );
                    }
                    // controls
                    ui.add_space(4.0);
                    ui.horizontal(|ui| {
                        ui.spacing_mut().item_spacing.x = 6.0;
                        let mb = |ui: &mut egui::Ui, glyph: &str, main: bool| -> egui::Response {
                            let t = RichText::new(glyph).monospace().size(10.0)
                                .color(if main { c3(pal.on_accent) } else { c3(pal.text2) });
                            let b = egui::Button::new(t)
                                .corner_radius(CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX })
                                .min_size(Vec2::new(if main { 34.0 } else { 30.0 }, 26.0));
                            let b = if main { b.fill(c3(pal.accent)).stroke(Stroke::NONE) } else { b.fill(Color32::TRANSPARENT).stroke(Stroke::new(1.0, alpha(pal.text, 26))) };
                            ui.add(b)
                        };
                        if mb(ui, "⏮", false).clicked() { media::previous(); }
                        if mb(ui, if m.playing { "⏸" } else { "▶" }, true).clicked() { media::play_pause(); }
                        if mb(ui, "⏭", false).clicked() { media::next(); }
                    });
                }
            }
        });
    }

    fn widget_weather(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        card(ui, pal, "Weather", |ui| {
            if let Some(w) = &self.weather {
                ui.horizontal(|ui| {
                    ui.label(RichText::new(weather::emoji(w.code, w.is_day)).size(26.0));
                    ui.label(RichText::new(format!("{:.0}°", w.temp_c)).monospace().size(26.0).color(c3(pal.text)));
                    ui.vertical(|ui| {
                        ui.label(RichText::new(format!("wind {:.0} km/h", w.wind_kmh)).monospace().size(9.5).color(c3(pal.text3)));
                        ui.label(RichText::new(format!("humidity {}%", w.humidity)).monospace().size(9.5).color(c3(pal.text3)));
                    });
                });
                ui.label(RichText::new(self.weather_city.clone()).size(11.0).color(c3(pal.text2)));
            } else if !self.weather_err.is_empty() {
                ui.label(RichText::new(self.weather_err.clone()).monospace().size(10.0).color(c3(pal.alarm)));
                city_editor(ui, pal, &mut self.city_input, &mut self.cfg.weather_city);
            } else {
                ui.label(RichText::new("Loading weather…").monospace().size(10.0).color(c3(pal.text3)));
            }
        });
    }

    fn widget_notes(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        card(ui, pal, "Notes", |ui| {
            let resp = ui.add(
                egui::TextEdit::multiline(&mut self.note_input)
                    .desired_rows(3)
                    .font(egui::TextStyle::Monospace),
            );
            if resp.changed() {
                self.cfg.notes = self.note_input.clone();
                self.cfg.save();
            }
        });
    }

    fn widget_launcher(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        card(ui, pal, "App launcher", |ui| {
            let paths = self.cfg.launchers.clone();
            if paths.is_empty() {
                ui.label(RichText::new("No apps yet — add one below.").monospace().size(10.0).color(c3(pal.text3)));
            }
            for p in &paths {
                ui.horizontal(|ui| {
                    let name = p.rsplit(['\\', '/']).next().unwrap_or(p).to_string();
                    let label = RichText::new(format!("▸ {name}")).monospace().size(9.5);
                    if ui.add(egui::Button::new(label)
                        .corner_radius(CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX })
                        .fill(ui_style::alpha(pal.text, 8))
                        .stroke(Stroke::new(1.0, ui_style::alpha(pal.text, 24)))
                        .min_size(Vec2::new(0.0, 22.0))).clicked() {
                        if let Err(e) = launcher::launch(p) {
                            self.flash(&format!("launch failed: {e}"));
                        }
                    }
                });
            }
            ui.add_space(4.0);
            ui.horizontal(|ui| {
                let resp_enter = ui_style::input_field(ui, &pal, &mut self.launcher_input, "App path, e.g. chrome.exe");
                if ui_style::pill_btn(ui, "Add", true, false, &pal).clicked() || resp_enter {
                    let v = self.launcher_input.trim().to_string();
                    if !v.is_empty() && !self.cfg.launchers.contains(&v) {
                        self.cfg.launchers.push(v);
                        self.cfg.save();
                    }
                    self.launcher_input.clear();
                }
            });
        });
    }

    // ---------- settings deck ----------

    fn settings_rows(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        ui.label(RichText::new("Changes are staged until you press Apply — Reset discards them.")
            .size(10.5).color(c3(pal.text3)));
        ui.add_space(8.0);

        // ---- General ----
        {
            let mut is_open = self.deck_open.contains(&"general");
            let summary = match self.draft.pos_mode.as_str() {
                "left" => "Left".to_string(),
                "right" => "Right".to_string(),
                _ => "Default".to_string(),
            };
            let mut pos_mode = self.draft.pos_mode.clone();
            let mut do_rerun = false;
            let mut do_factory = false;
            ui_style::section_card(ui, &pal, "General", "start here", &summary, &mut is_open, |ui| {
                ui.horizontal(|ui| {
                    ui.label(RichText::new("Position").size(12.0).color(c3(pal.text)));
                    ui.add_space(4.0);
                    for mode in ["default", "left", "right"] {
                        let sel = pos_mode == mode;
                        if ui_style::chip(ui, mode, sel, &pal).clicked() && !sel {
                            pos_mode = mode.into();
                        }
                    }
                });
                ui.horizontal(|ui| {
                    if ui_style::pill_btn(ui, "Rerun setup", false, false, &pal).clicked() {
                        do_rerun = true;
                    }
                    if ui_style::pill_btn(ui, "Factory reset", false, true, &pal).clicked() {
                        do_factory = true;
                    }
                });
            });
            self.draft.pos_mode = pos_mode;
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"general") { v.push("general"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "general").collect() };
            if do_rerun { self.setup_open = true; }
            if do_factory {
                self.cfg = Config::default();
                self.cfg.save();
                self.draft = self.cfg.clone();
                self.note_input.clear();
                self.city_input.clear();
                self.weather = None;
                self.weather_tried = false;
                self.setup_open = true;
                self.flash("Factory reset");
            }
        }

        // ---- Size ----
        {
            let mut is_open = self.deck_open.contains(&"size");
            let summary = format!("{}px", self.draft.width);
            let mut width = self.draft.width as f32;
            ui_style::section_card(ui, &pal, "Size", "how wide the idle pill is", &summary, &mut is_open, |ui| {
                ui.horizontal(|ui| {
                    ui.label(RichText::new("Idle width").size(12.0).color(c3(pal.text)));
                    ui.add_space(6.0);
                    ui.add(egui::Slider::new(&mut width, 240.0..=520.0).suffix("px"));
                });
            });
            self.draft.width = width.round() as u32;
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"size") { v.push("size"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "size").collect() };
        }

        // ---- Appearance ----
        {
            let mut is_open = self.deck_open.contains(&"appearance");
            let surface = if !self.draft.glass { "Solid" } else if !self.draft.frosted { "Glass" } else { "Frosted" };
            let summary = if self.draft.gradient == "none" { surface.to_string() } else { format!("{}, {}", surface, self.draft.gradient) };
            let mut g = self.draft.glass;
            let mut f = self.draft.frosted;
            let mut grad = self.draft.gradient.clone();
            let mut opac = self.draft.opacity as f32;
            ui_style::section_card(ui, &pal, "Appearance", "surface, glass and gradients", &summary, &mut is_open, |ui| {
                ui.horizontal(|ui| {
                    ui.label(RichText::new("Surface").size(12.0).color(c3(pal.text)));
                    let cur = if !g { 0 } else if !f { 1 } else { 2 };
                    let pick = ui_style::segmented(ui, &["Solid", "Glass", "Frosted"], cur, &pal);
                    if pick != cur { g = pick != 0; f = pick == 2; }
                });
                if g {
                    ui.horizontal(|ui| {
                        ui.label(RichText::new("Opacity").size(12.0).color(c3(pal.text)));
                        ui.add(egui::Slider::new(&mut opac, 10.0..=100.0).suffix("%"));
                    });
                }
                ui.horizontal_wrapped(|ui| {
                    ui.label(RichText::new("Gradient").size(12.0).color(c3(pal.text)));
                    for gr in ["none", "sunset", "ocean", "aurora", "candy", "graphite"] {
                        let sel = grad == gr;
                        let lab = gr.get(0..1).map(|x| x.to_uppercase()).unwrap_or_default() + &gr[1..];
                        if ui_style::chip(ui, &lab, sel, &pal).clicked() && !sel {
                            grad = gr.into();
                        }
                    }
                });
            });
            self.draft.glass = g;
            self.draft.frosted = f;
            self.draft.gradient = grad;
            self.draft.opacity = opac.round() as u32;
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"appearance") { v.push("appearance"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "appearance").collect() };
        }

        // ---- Theme ----
        {
            let mut is_open = self.deck_open.contains(&"theme");
            let summary = match self.draft.theme.as_str() {
                "light" => "Light".to_string(),
                "system" => "System".to_string(),
                "color" => format!("Color #{:02x}{:02x}{:02x}", self.draft.accent[0], self.draft.accent[1], self.draft.accent[2]),
                _ => "Dark".to_string(),
            };
            let mut th = self.draft.theme.clone();
            let mut acc = self.draft.accent;
            ui_style::section_card(ui, &pal, "Theme", "light, dark and the LED color", &summary, &mut is_open, |ui| {
                ui.horizontal(|ui| {
                    const THEMES: [&str; 4] = ["light", "dark", "system", "color"];
                    let cur = THEMES.iter().position(|&t| th == t).unwrap_or(1);
                    let pick = ui_style::segmented(ui, &["Light", "Dark", "System", "Color"], cur, &pal);
                    if pick != cur {
                        th = THEMES[pick].into();
                        if th == "color" && acc == [79, 195, 255] {
                            if let Some(a) = theme::os_accent() { acc = a; }
                        }
                    }
                });
                if th == "color" {
                    ui.horizontal(|ui| {
                        ui.label(RichText::new("LED color").size(12.0).color(c3(pal.text)));
                        for col in [[79, 195, 255], [61, 220, 151], [255, 107, 107], [255, 193, 77], [168, 85, 247]] {
                            let (r, resp) = ui.allocate_exact_size(Vec2::splat(18.0), egui::Sense::click());
                            let sel = acc == col;
                            ui.painter().rect_filled(r, CornerRadius::same(5), c3(col));
                            if sel {
                                ui.painter().rect_stroke(r, CornerRadius::same(5), Stroke::new(2.0, Color32::WHITE), egui::StrokeKind::Outside);
                            }
                            if resp.clicked() { acc = col; }
                        }
                    });
                }
            });
            self.draft.theme = th;
            self.draft.accent = acc;
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"theme") { v.push("theme"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "theme").collect() };
        }

        // ---- Idle ----
        {
            let mut is_open = self.deck_open.contains(&"idle");
            let label_of = |c: &str| match c {
                "brand" => "Brand", "stats" => "Stats", "custom" => "Custom", _ => "Clock",
            };
            let summary = if self.draft.idle_content == "custom" && !self.draft.custom_text.is_empty() {
                self.draft.custom_text.clone()
            } else {
                label_of(&self.draft.idle_content).to_string()
            };
            let mut idle = self.draft.idle_content.clone();
            let mut ctext = self.draft.custom_text.clone();
            ui_style::section_card(ui, &pal, "Idle", "what the bar shows when nothing is happening", &summary, &mut is_open, |ui| {
                ui.horizontal(|ui| {
                    ui.label(RichText::new("When idle").size(12.0).color(c3(pal.text)));
                    const IDLES: [&str; 4] = ["clock", "brand", "stats", "custom"];
                    let cur = IDLES.iter().position(|&c| idle == c).unwrap_or(0);
                    let pick = ui_style::segmented(ui, &["Clock", "Brand", "Stats", "Custom"], cur, &pal);
                    if pick != cur {
                        idle = IDLES[pick].into();
                    }
                });
                if idle == "custom" {
                    ui.add(egui::TextEdit::singleline(&mut ctext).desired_width(f32::INFINITY));
                }
            });
            self.draft.idle_content = idle;
            self.draft.custom_text = ctext;
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"idle") { v.push("idle"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "idle").collect() };
        }

        // ---- System widget ----
        {
            let mut is_open = self.deck_open.contains(&"syswidget");
            let n = [self.draft.sys_cpu, self.draft.sys_mem, self.draft.sys_bat, self.draft.sys_net, self.draft.sys_temp]
                .iter().filter(|b| **b).count();
            let summary = if n == 0 { "Off".to_string() } else { format!("{} of 5 rows", n) };
            let (mut c, mut m, mut b, mut nt, mut t) =
                (self.draft.sys_cpu, self.draft.sys_mem, self.draft.sys_bat, self.draft.sys_net, self.draft.sys_temp);
            ui_style::section_card(ui, &pal, "System widget", "CPU, memory, battery, network", &summary, &mut is_open, |ui| {
                for (flag, lab) in [
                    (&mut c, "CPU"), (&mut m, "Memory"), (&mut b, "Battery"),
                    (&mut nt, "Network"), (&mut t, "Temperature"),
                ] {
                    ui.horizontal(|ui| {
                        ui_style::toggle(ui, flag, &pal);
                        ui.label(RichText::new(lab).size(11.5).color(c3(pal.text)));
                    });
                }
            });
            (self.draft.sys_cpu, self.draft.sys_mem, self.draft.sys_bat, self.draft.sys_net, self.draft.sys_temp) = (c, m, b, nt, t);
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"syswidget") { v.push("syswidget"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "syswidget").collect() };
        }

        // ---- Weather ----
        {
            let mut is_open = self.deck_open.contains(&"weather");
            let summary = if self.draft.weather_city.is_empty() { "OS location".to_string() } else { self.draft.weather_city.clone() };
            let mut city = self.draft.weather_city.clone();
            let mut cin = self.city_input.clone();
            ui_style::section_card(ui, &pal, "Weather", "city or automatic", &summary, &mut is_open, |ui| {
                city_editor(ui, pal, &mut cin, &mut city);
                ui.label(RichText::new("Leave empty to use OS location (Windows).")
                    .monospace().size(9.0).color(c3(pal.text3)));
            });
            self.draft.weather_city = city;
            self.city_input = cin;
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"weather") { v.push("weather"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "weather").collect() };
        }

        // ---- Widgets ----
        {
            let mut is_open = self.deck_open.contains(&"widgets");
            let summary = format!("{} enabled", self.draft.widgets.len());
            let mut widgets = self.draft.widgets.clone();
            ui_style::section_card(ui, &pal, "Widgets", "show, hide and reorder", &summary, &mut is_open, |ui| {
                const ALL: [(&str, &str); 6] = [
                    ("clock", "Clock"), ("system", "System stats"), ("media", "Now playing"),
                    ("weather", "Weather"), ("notes", "Notes"), ("launcher", "App launcher"),
                ];
                for (id, label) in ALL {
                    let mut on = widgets.iter().any(|w| w == id);
                    ui.horizontal(|ui| {
                        if ui_style::toggle(ui, &mut on, &pal).clicked() {
                            if on {
                                widgets.push(id.to_string());
                            } else {
                                widgets.retain(|w| w != id);
                            }
                        }
                        ui.label(RichText::new(label).size(11.5).color(c3(pal.text)));
                    });
                }
                ui.add_space(6.0);
                ui.label(RichText::new("ORDER — TOP TO BOTTOM").monospace().size(8.5).color(c3(pal.text3)));
                let ids = widgets.clone();
                for (i, id) in ids.iter().enumerate() {
                    let label = ALL.iter().find(|(k, _)| k == id).map(|(_, l)| *l).unwrap_or(id);
                    ui.horizontal(|ui| {
                        ui.label(RichText::new(format!("{}. {}", i + 1, label)).size(11.0).color(c3(pal.text)));
                        ui.with_layout(egui::Layout::right_to_left(Align::Center), |ui| {
                            if ui_style::pill_btn(ui, "\u{2193}", false, false, &pal).clicked() && i + 1 < ids.len() {
                                widgets.swap(i, i + 1);
                            }
                            if ui_style::pill_btn(ui, "\u{2191}", false, false, &pal).clicked() && i > 0 {
                                widgets.swap(i - 1, i);
                            }
                        });
                    });
                }
            });
            self.draft.widgets = widgets;
            self.deck_open = if is_open {
                let mut v = self.deck_open.clone(); if !v.contains(&"widgets") { v.push("widgets"); } v
            } else { self.deck_open.iter().copied().filter(|k| *k != "widgets").collect() };
        }

        // ---- Autostart (Windows only) ----
        #[cfg(target_os = "windows")]
        {
            let mut au = self.draft.autostart;
            ui.horizontal(|ui| {
                ui_style::toggle(ui, &mut au, &pal);
                ui.label(RichText::new("Start with Windows").size(11.5).color(c3(pal.text)));
            });
            self.draft.autostart = au;
        }
    }

    /// Sticky Apply / Reset foot, drawn under the scroll.
    fn settings_foot(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        ui.add_space(4.0);
        ui.separator();
                    ui.horizontal(|ui| {
                if ui_style::pill_btn(ui, "Reset to defaults", false, false, &pal).clicked() {
                    self.draft = Config::default();
                }
                ui.with_layout(egui::Layout::right_to_left(Align::Center), |ui| {
                    if ui_style::pill_btn(ui, "Apply", true, false, &pal).clicked() {
                        // autostart: registry is the source of truth
                        if self.draft.autostart != autostart::enabled() {
                            if let Err(e) = autostart::set(self.draft.autostart) {
                                self.flash(&format!("autostart failed: {e}"));
                            }
                            self.tray.sync_autostart(autostart::enabled());
                        }
                        self.cfg = self.draft.clone();
                        self.cfg.save();
                        self.flash("Applied");
                    }
                    if ui_style::pill_btn(ui, "Reset", false, false, &pal).clicked() {
                        self.draft = self.cfg.clone();
                    }
                });
            });
        
    }
}

impl App {
    /// First-run / rerun wizard: quick presets, theme, widgets, done.
    fn setup_rows(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        ui.add_space(2.0);
        ui_style::tracked(ui.painter(), Pos2::new(ui.cursor().left() + 2.0, ui.cursor().top() + 12.0),
            Align2::LEFT_CENTER, "SET UP WINOTCH", 15.0, c3(pal.text), 2.6);
        ui.allocate_space(Vec2::new(ui.available_width(), 24.0));
        ui.label(RichText::new("Pick how the island should look. You can change all of this later in settings.")
            .size(11.0).color(c3(pal.text3)));
        ui.add_space(14.0);

        ui_style::eyebrow(ui, &pal, "Shape");
        ui.horizontal(|ui| {
            let presets: [(&str, &str, bool, bool, fn(&mut Config)); 3] = [
                (
                    "Classic notch", "dark glass, carved in", true, true,
                    |c: &mut Config| {
                        c.theme = "dark".into(); c.glass = true; c.frosted = false;
                        c.gradient = "none".into(); c.shape_notch = true;
                    },
                ),
                (
                    "Solid notch", "opaque, no blur", true, false,
                    |c: &mut Config| {
                        c.theme = "dark".into(); c.glass = false; c.frosted = false;
                        c.gradient = "none".into(); c.shape_notch = true;
                    },
                ),
                (
                    "Light pill", "floating glass capsule", false, true,
                    |c: &mut Config| {
                        c.theme = "light".into(); c.glass = true; c.frosted = false;
                        c.gradient = "none".into(); c.shape_notch = false;
                    },
                ),
            ];
            for (name, desc, dark, notch, apply) in presets {
                let active = self.wizard_pick == Some(name)
                    || {
                        let d = &self.draft;
                        d.theme == if dark { "dark" } else { "light" }
                            && d.glass && !d.frosted
                            && d.shape_notch == notch
                    };
                let (rect, resp) = ui.allocate_exact_size(Vec2::new(118.0, 74.0), egui::Sense::click());
                let p = ui.painter();
                // card
                p.rect_filled(rect, CornerRadius::same(12),
                    if active { ui_style::alpha(pal.accent, 26) } else { ui_style::alpha(pal.text, 8) });
                p.rect_stroke(rect, CornerRadius::same(12),
                    Stroke::new(1.0, if active { c3(pal.accent) } else { ui_style::alpha(pal.text, 24) }),
                    egui::StrokeKind::Outside);
                // preview strip: dark tiles sit on a lighter well so the notch reads
                let prev = Rect::from_min_size(Pos2::new(rect.left() + 14.0, rect.top() + 10.0), Vec2::new(90.0, 16.0));
                if dark {
                    p.rect_filled(prev, CornerRadius::same(4), ui_style::alpha(pal.text, 18));
                }
                ui_style::shape_preview(p, Rect::from_center_size(prev.center(), Vec2::new(64.0, 12.0)), notch, dark, c3(pal.accent));
                // label + desc
                ui_style::tracked(p, Pos2::new(rect.left() + 12.0, rect.top() + 44.0), Align2::LEFT_CENTER,
                    &name.to_uppercase(), 10.0, if active { c3(pal.text) } else { c3(pal.text2) }, 0.8);
                p.text(Pos2::new(rect.left() + 12.0, rect.top() + 58.0), Align2::LEFT_CENTER, desc,
                    FontId::proportional(9.0), c3(pal.text3));
                if resp.clicked() {
                    apply(&mut self.draft);
                    self.wizard_pick = Some(name);
                }
            }
        });
        ui.add_space(14.0);

        ui_style::eyebrow(ui, &pal, "Theme");
        ui.horizontal(|ui| {
            const THEMES: [&str; 4] = ["light", "dark", "system", "color"];
            let cur = THEMES.iter().position(|&t| self.draft.theme == t).unwrap_or(1);
            let pick = ui_style::segmented(ui, &["Light", "Dark", "System", "Color"], cur, &pal);
            if pick != cur {
                let t = THEMES[pick];
                self.draft.theme = t.into();
                if t == "color" && self.draft.accent == [79, 195, 255] {
                    if let Some(a) = theme::os_accent() { self.draft.accent = a; }
                }
            }
        });
        ui.add_space(14.0);

        ui_style::eyebrow(ui, &pal, "Widgets");
        ui.label(RichText::new("Choose what shows in the panel:").size(10.5).color(c3(pal.text3)));
        ui.add_space(4.0);
        ui.horizontal_wrapped(|ui| {
            const ALL: [(&str, &str); 6] = [
                ("clock", "Clock"), ("system", "System stats"), ("media", "Now playing"),
                ("weather", "Weather"), ("notes", "Notes"), ("launcher", "App launcher"),
            ];
            for (id, label) in ALL {
                let on = self.draft.widgets.iter().any(|w| w == id);
                if ui_style::chip(ui, label, on, &pal).clicked() {
                    if on {
                        self.draft.widgets.retain(|w| w != id);
                    } else {
                        self.draft.widgets.push(id.to_string());
                    }
                }
            }
        });
        ui.add_space(4.0);
    }

    /// Wizard foot: Cancel / Get started.
    fn setup_foot(&mut self, ui: &mut egui::Ui, pal: theme::Palette) {
        ui.add_space(4.0);
        ui.separator();
                    ui.horizontal(|ui| {
                if ui_style::pill_btn(ui, "Cancel", false, false, &pal).clicked() {
                    self.setup_open = false;
                    if self.cfg.config_is_fresh {
                        // first run with nothing picked: keep defaults
                        self.cfg.save();
                    }
                }
                ui.with_layout(egui::Layout::right_to_left(Align::Center), |ui| {
                    if ui_style::pill_btn(ui, "Get started", true, false, &pal).clicked() {
                        self.cfg = self.draft.clone();
                        self.cfg.save();
                        self.setup_open = false;
                        self.flash("Welcome to Winotch");
                    }
                });
            });
        
    }
}

// ---------- helpers ----------

fn card(ui: &mut egui::Ui, pal: theme::Palette, title: &str, add: impl FnOnce(&mut egui::Ui)) {
    Frame::default()
        .fill(alpha(pal.text, 5))
        .stroke(Stroke::new(1.0, alpha(pal.text, 15)))
        .corner_radius(CornerRadius::same(14))
        .inner_margin(egui::Margin::symmetric(13, 11))
        .show(ui, |ui| {
            ui_style::card_head(ui, &pal, title);
            ui.add_space(4.0);
            add(ui);
        });
}


fn clock_hm() -> String {
    let secs = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    // Local time: chrono handles the timezone.
    let now = chrono::Local::now();
    let _ = secs;
    now.format("%H:%M").to_string()
}

fn clock_hms() -> (String, String) {
    let now = chrono::Local::now();
    (now.format("%H:%M:%S").to_string(), now.format("%A, %B %e").to_string())
}

fn city_editor(ui: &mut egui::Ui, pal: theme::Palette, input: &mut String, target: &mut String) {
    ui.horizontal(|ui| {
        let mut changed = false;
        if ui_style::input_field(ui, &pal, input, "City name…") {
            changed = true;
        }
        if ui_style::pill_btn(ui, "Set city", true, false, &pal).clicked() {
            changed = true;
        }
        if changed {
            *target = input.trim().to_string();
        }
    });
}
