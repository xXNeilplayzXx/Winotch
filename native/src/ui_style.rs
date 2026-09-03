//! designv2 visual language helpers — pill buttons, tracked mono labels,
//! gradient meshes, hand-drawn toggles. Shared by panel, deck and wizard.

use eframe::egui;
use egui::{
    epaint, Align2, Button, Color32, CornerRadius, FontId, Frame, Mesh, Pos2, Rect, RichText,
    Sense, Stroke, Vec2,
};
use crate::theme::Palette;

pub fn c3(rgb: [u8; 3]) -> Color32 { Color32::from_rgb(rgb[0], rgb[1], rgb[2]) }
pub fn alpha(rgb: [u8; 3], a: u8) -> Color32 { Color32::from_rgba_unmultiplied(rgb[0], rgb[1], rgb[2], a) }

/// Mono text with tracking (letter-spacing) — designv2's `letter-spacing` look.
pub fn tracked(painter: &egui::Painter, pos: Pos2, anchor: Align2, s: &str, size: f32, color: Color32, tracking: f32) {
    if s.is_empty() { return; }
    let font = FontId::monospace(size);
    // Measure each glyph so the advance is width + tracking, not tracking alone.
    let widths: Vec<f32> = s.chars()
        .map(|ch| painter.layout_no_wrap(ch.to_string(), font.clone(), color).size().x)
        .collect();
    let total: f32 = widths.iter().sum::<f32>() + tracking * s.chars().count().saturating_sub(1) as f32;
    let mut x = match anchor {
        Align2::LEFT_TOP | Align2::LEFT_CENTER | Align2::LEFT_BOTTOM => pos.x,
        Align2::CENTER_TOP | Align2::CENTER_CENTER | Align2::CENTER_BOTTOM => pos.x - total / 2.0,
        _ => pos.x - total,
    };
    for (ch, w) in s.chars().zip(widths) {
        painter.text(Pos2::new(x, pos.y), anchor, ch.to_string(), font.clone(), color);
        x += w + tracking;
    }
}

/// Wire the palette into egui so native widgets (sliders, text fields,
/// checkboxes, scroll bars) match the instrument look instead of egui gray.
pub fn apply_egui_style(ctx: &egui::Context, pal: &Palette) {
    ctx.all_styles_mut(|style| apply_to(style, pal));
}

fn apply_to(style: &mut egui::Style, pal: &Palette) {
    let text = c3(pal.text);
    let text2 = c3(pal.text2);
    let text3 = c3(pal.text3);
    let accent = c3(pal.accent);

    style.visuals.widgets.noninteractive.bg_fill = alpha(pal.ink, 0);
    style.visuals.widgets.noninteractive.fg_stroke = Stroke::new(1.0, text3);
    style.visuals.widgets.inactive.bg_fill = alpha(pal.text, 18);
    style.visuals.widgets.inactive.fg_stroke = Stroke::new(1.0, text2);
    style.visuals.widgets.hovered.bg_fill = alpha(pal.text, 34);
    style.visuals.widgets.hovered.fg_stroke = Stroke::new(1.0, text);
    style.visuals.widgets.active.bg_fill = alpha(pal.accent, 46);
    style.visuals.widgets.active.fg_stroke = Stroke::new(1.0, text);
    style.visuals.selection.bg_fill = alpha(pal.accent, 60);
    style.visuals.selection.stroke = Stroke::new(1.0, accent);
    style.visuals.window_fill = alpha(pal.panel, 0);
    style.visuals.extreme_bg_color = alpha(pal.ink, 90);
    style.visuals.faint_bg_color = alpha(pal.text, 12);
    style.visuals.widgets.noninteractive.bg_stroke = Stroke::new(1.0, alpha(pal.text, 22));
    style.visuals.widgets.inactive.bg_stroke = Stroke::new(1.0, alpha(pal.text, 24));
    style.visuals.widgets.hovered.bg_stroke = Stroke::new(1.0, alpha(pal.text, 60));
    style.visuals.window_corner_radius = CornerRadius::same(14);
    style.visuals.menu_corner_radius = CornerRadius::same(12);
    style.spacing.item_spacing = Vec2::new(8.0, 6.0);
}

/// designv2 pill button (`.btn`): tracked mono uppercase, 999px radius.
pub fn pill_btn(ui: &mut egui::Ui, label: &str, primary: bool, danger: bool, pal: &Palette) -> egui::Response {
    let text_col = if primary { alpha(pal.on_accent, 255) } else if danger { c3(pal.alarm) } else { c3(pal.text2) };
    let text = RichText::new(label.to_uppercase()).monospace().size(10.0).color(text_col);
    let mut btn = Button::new(text)
        .corner_radius(CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX })
        .min_size(Vec2::new(0.0, 26.0));
    if primary {
        btn = btn.fill(c3(pal.accent)).stroke(Stroke::NONE);
    } else {
        btn = btn.fill(alpha(pal.text, 8)).stroke(Stroke::new(1.0, alpha(pal.text, 26)));
    }
    ui.add(btn)
}

/// A designv2 toggle switch (`.switch`), hand-drawn: 30x17 pill, LED knob.
/// Returns the new value if toggled.
pub fn toggle(ui: &mut egui::Ui, on: &mut bool, pal: &Palette) -> egui::Response {
    let (rect, resp) = ui.allocate_exact_size(Vec2::new(30.0, 17.0), Sense::click());
    let p = ui.painter();
    let track = if *on { alpha(pal.accent, 46) } else { alpha(pal.text, 28) };
    p.rect_filled(rect, CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX }, track);
    let knob_r = 6.5;
    let knob_x = if *on { rect.right() - 2.0 - knob_r } else { rect.left() + 2.0 + knob_r };
    let knob_col = if *on { c3(pal.accent) } else { Color32::from_rgb(0xcf, 0xd6, 0xe2) };
    p.circle_filled(Pos2::new(knob_x, rect.center().y), knob_r, knob_col);
    if resp.clicked() { *on = !*on; }
    resp
}

/// A designv2 chip (`.chip`): mono, 999px radius, accent when selected.
pub fn chip(ui: &mut egui::Ui, label: &str, on: bool, pal: &Palette) -> egui::Response {
    let text = RichText::new(label).monospace().size(10.5).color(if on { alpha(pal.led_strong, 255) } else { c3(pal.text2) });
    let mut btn = Button::new(text)
        .corner_radius(CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX })
        .fill(if on { alpha(pal.accent, 26) } else { alpha(pal.text, 6) })
        .stroke(Stroke::new(1.0, if on { c3(pal.accent) } else { alpha(pal.text, 24) }));
    btn = btn.min_size(Vec2::new(0.0, 22.0));
    ui.add(btn)
}

/// A designv2 segmented control (`.segmented`): pill row of options.
pub fn segmented(ui: &mut egui::Ui, options: &[&str], selected: usize, pal: &Palette) -> usize {
    let mut picked = selected;
    ui.horizontal(|ui| {
        let frame = Frame::default()
            .fill(alpha(pal.text, 14))
            .corner_radius(CornerRadius::same(10))
            .inner_margin(egui::Margin::same(2));
        frame.show(ui, |ui| {
            ui.spacing_mut().item_spacing = Vec2::new(2.0, 0.0);
            for (i, opt) in options.iter().enumerate() {
                let on = i == selected;
                let text = RichText::new(*opt).size(11.0).color(if on { c3(pal.accent) } else { c3(pal.text2) });
                let btn = Button::new(text)
                    .corner_radius(CornerRadius::same(8))
                    .fill(if on { alpha(pal.accent, 26) } else { Color32::TRANSPARENT })
                    .stroke(Stroke::new(1.0, if on { alpha(pal.accent, 90) } else { Stroke::NONE.color }))
                    .min_size(Vec2::new(0.0, 22.0));
                if ui.add(btn).clicked() && !on {
                    picked = i;
                }
            }
        });
    });
    picked
}

/// Designv2 gradient overlay, drawn as a 2-stop diagonal vertex-color mesh.
pub fn gradient_mesh(painter: &egui::Painter, rect: Rect, radius: CornerRadius, id: &str, pal: &Palette) {
    let (c1, c2) = match id {
        "sunset" => (alpha([255, 122, 89], 118), alpha([168, 85, 247], 46)),
        "ocean" => (alpha([14, 165, 233], 108), alpha([6, 182, 212], 52)),
        "aurora" => (alpha([16, 185, 129], 102), alpha([139, 92, 246], 42)),
        "candy" => (alpha([236, 72, 153], 102), alpha([59, 130, 246], 42)),
        "graphite" => (alpha([148, 163, 184], 70), alpha([71, 85, 105], 52)),
        _ => return,
    };
    let a = alpha(pal.panel, 0);
    let _ = a;
    let c1 = premul(c1);
    let c2 = premul(c2);
    let quad = rounded_quad(rect, radius, 8);
    if quad.len() < 4 { return; }
    // designv2 uses `linear-gradient(135deg, c1, c2)` — the color varies along
    // the top-left → bottom-right diagonal. Color each vertex by its projection
    // on that diagonal (not by outline index, which painted a rainbow ring).
    let n = quad.len();
    let span = (rect.width() + rect.height()).max(1.0);
    let t = |p: Pos2| (((p.x - rect.left()) + (p.y - rect.top())) / span).clamp(0.0, 1.0);
    let center = rect.center();
    let mut mesh = Mesh::default();
    for i in 0..n {
        let a = quad[i];
        let b = quad[(i + 1) % n];
        // one triangle per outline edge, fanned from the center — vertex colors
        // interpolate the diagonal gradient across the whole shape.
        let base = mesh.vertices.len() as u32;
        mesh.vertices.push(epaint::Vertex { pos: a, uv: epaint::WHITE_UV, color: lerp_color(c1, c2, t(a)) });
        mesh.vertices.push(epaint::Vertex { pos: b, uv: epaint::WHITE_UV, color: lerp_color(c1, c2, t(b)) });
        mesh.vertices.push(epaint::Vertex { pos: center, uv: epaint::WHITE_UV, color: lerp_color(c1, c2, t(center)) });
        mesh.indices.extend_from_slice(&[base, base + 1, base + 2]);
    }
    painter.add(egui::Shape::Mesh(std::sync::Arc::new(mesh)));
}

fn premul(c: Color32) -> Color32 {
    let a = c.a() as u32;
    Color32::from_rgba_premultiplied(
        (c.r() as u32 * a / 255) as u8,
        (c.g() as u32 * a / 255) as u8,
        (c.b() as u32 * a / 255) as u8,
        c.a(),
    )
}

fn lerp_color(a: Color32, b: Color32, t: f32) -> Color32 {
    let l = |x: u8, y: u8| (x as f32 + (y as f32 - x as f32) * t).round() as u8;
    Color32::from_rgba_premultiplied(l(a.r(), b.r()), l(a.g(), b.g()), l(a.b(), b.b()), l(a.a(), b.a()))
}

/// Approximate a rounded rect as a polygon of `seg` points per corner.
fn rounded_quad(rect: Rect, radius: CornerRadius, seg: usize) -> Vec<Pos2> {
    let seg = seg.max(2);
    let r = |v: u8| v as f32;
    let mut pts = Vec::new();
    let mut arc = |cx: f32, cy: f32, rad: f32, a0: f32, a1: f32| {
        for k in 0..=seg {
            let t = a0 + (a1 - a0) * (k as f32 / seg as f32);
            pts.push(Pos2::new(cx + rad * t.cos(), cy + rad * t.sin()));
        }
    };
    let (l, t, ri, b) = (rect.left(), rect.top(), rect.right(), rect.bottom());
    arc(ri - r(radius.ne), t + r(radius.ne), r(radius.ne), -std::f32::consts::FRAC_PI_2, 0.0);
    arc(ri - r(radius.se), b - r(radius.se), r(radius.se), 0.0, std::f32::consts::FRAC_PI_2);
    arc(l + r(radius.sw), b - r(radius.sw), r(radius.sw), std::f32::consts::FRAC_PI_2, std::f32::consts::PI);
    arc(l + r(radius.nw), t + r(radius.nw), r(radius.nw), std::f32::consts::PI, 3.0 * std::f32::consts::FRAC_PI_2);
    pts
}

/// widget card head: mini LED + tracked uppercase label
pub fn card_head(ui: &mut egui::Ui, pal: &Palette, label: &str) {
    ui.horizontal(|ui| {
        let (r, _) = ui.allocate_exact_size(Vec2::new(5.0, 5.0), Sense::hover());
        ui.painter().circle_filled(r.center(), 2.5, c3(pal.accent));
        ui.spacing_mut().item_spacing.x = 6.0;
        tracked(ui.painter(), Pos2::new(r.right() + 6.0, r.center().y), Align2::LEFT_CENTER,
            &label.to_uppercase(), 9.0, alpha(pal.text3, 255), 1.6);
        ui.add_space(ui.available_width()); // consume row so nothing follows inline
    });
}

/// designv2 settings section (`.section`): a glass card with a mark dot,
/// tracked title + sub, right value summary, and chevron. `open` bodies are
/// drawn by `body` inside a `.section-body` padded region under a hairline.
pub fn section_card(
    ui: &mut egui::Ui,
    pal: &Palette,
    title: &str,
    sub: &str,
    summary: &str,
    open: &mut bool,
    body: impl FnOnce(&mut egui::Ui),
) {
    Frame::default()
        .fill(alpha(pal.text, 12))
        .stroke(Stroke::new(1.0, alpha(pal.text, 23)))
        .corner_radius(CornerRadius::same(14))
        .inner_margin(0.0)
        .show(ui, |ui| {
            ui.add_space(2.0);
            // -- head: whole row is the click target --
            let head = ui.horizontal(|ui| {
                ui.add_space(12.0);
                // mark dot
                let (dr, _) = ui.allocate_exact_size(Vec2::new(6.0, 6.0), Sense::hover());
                let mark = if *open { c3(pal.accent) } else { c3(pal.text3) };
                ui.painter().circle_filled(dr.center(), 3.0, mark);
                if *open {
                    ui.painter().circle_filled(dr.center(), 5.5, alpha(pal.accent, 60));
                }
                ui.add_space(4.0);
                // titles
                ui.vertical(|ui| {
                    tracked(ui.painter(), Pos2::new(ui.cursor().left(), ui.cursor().top() + 6.0),
                        Align2::LEFT_CENTER, &title.to_uppercase(), 10.5, c3(pal.text2), 1.6);
                    ui.allocate_space(Vec2::new(0.0, 11.0));
                    if !sub.is_empty() {
                        ui.label(RichText::new(sub).size(9.5).color(c3(pal.text3)));
                    }
                });
                ui.add_space(8.0);
                // summary + chevron, right-aligned
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    ui.add_space(12.0);
                    let (chr, _) = ui.allocate_exact_size(Vec2::new(10.0, 12.0), Sense::hover());
                    // draw chevron pointing right (closed) or down (open)
                    let cr = ui.painter();
                    let c = chr.center();
                    let (dx, dy) = if *open { (3.0, 3.0) } else { (1.5, 3.5) };
                    let col = c3(pal.text3);
                    if *open {
                        cr.line_segment([Pos2::new(c.x - dx, c.y - 1.0), Pos2::new(c.x, c.y + dy)], Stroke::new(1.2, col));
                        cr.line_segment([Pos2::new(c.x, c.y + dy), Pos2::new(c.x + dx, c.y - 1.0)], Stroke::new(1.2, col));
                    } else {
                        cr.line_segment([Pos2::new(c.x - 1.0, c.y - dy), Pos2::new(c.x + 1.5, c.y)], Stroke::new(1.2, col));
                        cr.line_segment([Pos2::new(c.x + 1.5, c.y), Pos2::new(c.x - 1.0, c.y + dy), ], Stroke::new(1.2, col));
                    }
                    if !summary.is_empty() {
                        ui.label(RichText::new(summary).monospace().size(9.5).color(c3(pal.text3)));
                    }
                });
            });
            // click anywhere on the head row toggles
            let head_resp = ui.interact(head.response.rect, ui.id().with(("sec-head", title)), egui::Sense::click());
            if head_resp.clicked() { *open = !*open; }

            // -- body --
            if *open {
                ui.add_space(2.0);
                ui.painter().line_segment(
                    [Pos2::new(head.response.rect.left() + 12.0, head.response.rect.bottom() + 1.0),
                     Pos2::new(head.response.rect.right() - 12.0, head.response.rect.bottom() + 1.0)],
                    Stroke::new(1.0, alpha(pal.text, 16)));
                Frame::default()
                    .inner_margin(egui::Margin { left: 14, right: 14, top: 4, bottom: 12 })
                    .show(ui, body);
            }
            ui.add_space(2.0);
        });
    ui.add_space(8.0);
}

/// Tracked small-caps eyebrow label (`.section-title` scale) — section openers.
pub fn eyebrow(ui: &mut egui::Ui, pal: &Palette, label: &str) {
    let pos = ui.cursor().min;
    tracked(ui.painter(), Pos2::new(pos.x, pos.y + 5.0), Align2::LEFT_CENTER,
        &label.to_uppercase(), 9.0, c3(pal.text3), 1.8);
    ui.allocate_space(Vec2::new(ui.available_width(), 13.0));
}

/// settings input field (`.widget-input`): dark well, hairline border,
/// accent border on focus. Returns true on Enter.
pub fn input_field(ui: &mut egui::Ui, pal: &Palette, text: &mut String, hint: &str) -> bool {
    let enter = ui
        .add(
            egui::TextEdit::singleline(text)
                .hint_text(RichText::new(hint).monospace().size(10.0).color(c3(pal.text3)))
                .font(egui::TextStyle::Monospace)
                .desired_width(ui.available_width() - 64.0)
                .min_size(Vec2::new(0.0, 26.0)),
        )
        .lost_focus()
        && ui.input(|i| i.key_pressed(egui::Key::Enter));
    enter
}

/// A drawn gear icon (8-tooth ring) — crisper than the ⚙ glyph at small size.
pub fn draw_gear(painter: &egui::Painter, c: Pos2, r: f32, col: Color32) {
    let teeth = 8;
    for i in 0..teeth {
        let a = (i as f32) * std::f32::consts::TAU / teeth as f32 + 0.2;
        let (s, co) = a.sin_cos();
        painter.line_segment(
            [Pos2::new(c.x + co * r * 0.78, c.y + s * r * 0.78), Pos2::new(c.x + co * r * 1.08, c.y + s * r * 1.08)],
            Stroke::new(1.8, col),
        );
    }
    painter.circle_stroke(c, r * 0.62, Stroke::new(1.8, col));
    painter.circle_filled(c, r * 0.18, col);
}

/// A drawn X (close) icon.
pub fn draw_x(painter: &egui::Painter, c: Pos2, r: f32, col: Color32) {
    let d = r * 0.72;
    painter.line_segment([Pos2::new(c.x - d, c.y - d), Pos2::new(c.x + d, c.y + d)], Stroke::new(1.6, col));
    painter.line_segment([Pos2::new(c.x - d, c.y + d), Pos2::new(c.x + d, c.y - d)], Stroke::new(1.6, col));
}

/// Shape preview: a tiny island rendering for the setup tiles.
/// `notch` = square top corners hugging an implied screen edge; else a capsule.
pub fn shape_preview(painter: &egui::Painter, rect: Rect, notch: bool, dark: bool, accent: Color32) {
    let face = if dark { Color32::from_rgb(21, 26, 36) } else { Color32::from_rgb(248, 250, 253) };
    let stroke_col = if dark { alpha([255, 255, 255], 40) } else { alpha([10, 13, 20], 45) };
    let radius = if notch { CornerRadius { nw: 0, ne: 0, sw: 9, se: 9 } } else { CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX } };
    painter.rect_filled(rect, radius, face);
    painter.rect_stroke(rect, radius, Stroke::new(1.0, stroke_col), egui::StrokeKind::Outside);
    // LED + brand dashes
    let my = rect.center().y;
    let x0 = rect.left() + rect.width() * 0.22;
    painter.circle_filled(Pos2::new(x0, my), 2.2, accent);
    painter.line_segment(
        [Pos2::new(x0 + 7.0, my), Pos2::new(x0 + 22.0, my)],
        Stroke::new(2.0, if dark { alpha([233, 237, 244], 150) } else { alpha([10, 13, 20], 130) }),
    );
}

/// Toast pill (`.toast`): centered, tracked mono, accent hairline border.
pub fn toast_pill(painter: &egui::Painter, center: Pos2, msg: &str, pal: &Palette) {
    let font = FontId::monospace(10.5);
    let galley = painter.layout_no_wrap(msg.to_string(), font.clone(), c3(pal.text));
    let size = galley.size() + Vec2::new(28.0, 16.0);
    let rect = Rect::from_center_size(center, size);
    painter.rect_filled(rect, CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX }, Color32::from_rgba_unmultiplied(24, 26, 32, 242));
    painter.rect_stroke(rect, CornerRadius { nw: u8::MAX, ne: u8::MAX, sw: u8::MAX, se: u8::MAX }, Stroke::new(1.0, alpha(pal.accent, 82)), egui::StrokeKind::Outside);
    painter.galley(Pos2::new(rect.left() + 14.0, rect.center().y - galley.size().y / 2.0), galley, c3(pal.text));
}
