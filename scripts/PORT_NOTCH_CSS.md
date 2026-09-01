# Porting the notch CSS fixes into the Winotch source

These are the exact, verified changes made to `preview.html` (the bundled build artifact
in this workspace), mapped to the real Tauri/React source. All rules below are the
**current** values in `preview.html` — the layout regression harness
(`preview.html?test=1`) passes 4/4 states against them.

The real project (this machine only has `preview.html` + `winotch.exe`) is a Tauri +
React app with the usual layout:

```
package.json          # npm run build -> bundles the React app
src/                  # React components + stylesheet(s)
  App.tsx / Island.tsx ...
  App.css / styles.css # the island styles (rules below)
src-tauri/            # Rust: window creation, update_notch_size, move_notch_to_monitor
index.html            # or wherever the guarded test harness should be added
```

Find the stylesheet that defines `.island-shell`, `.island`, `.island-strip`,
`.island-panel` and `@keyframes island-in` — that is the file to edit.

---

## 1. Flush top-center docking (no gap above the notch)

In the **app stylesheet**, ensure the shell pins the island to the top edge:

```css
.island-shell {
  width: 100%;
  height: 100%;
  display: flex;
  justify-content: center;   /* horizontal centering — flex path, no translateX hack */
  align-items: flex-start;   /* flush against the top edge */
  padding: 0;
  margin: 0;
}
```

And on the island itself — zero offsets, anchored to the top:

```css
.island {
  position: relative;
  top: 0;
  margin: 0;
  /* ...existing props... */
}
```

> Use flex centering (`justify-content: center`) rather than
> `left: 50%; transform: translateX(-50%)`: `transform` is already used by the
> `island-in` entrance keyframes, and a static `translateX` would be overridden
> mid-animation and cause a horizontal snap.

In the **window/preview stylesheet** (the one that sizes and pins the island — it
matches the second `<style>` block in `preview.html`):

- The body/container must NOT vertically center the island. If you center the
  preview stage with flex, use `align-items: flex-start` (the old
  `align-items: center` was the gap-above-the-notch bug).
- Keep the shape size rules, and add the expanded-height unlock:

```css
.island.shape-notch { width: 256px; height: 58px; }
.island.shape-notch.mode-expanded { width: 460px; max-height: none; }
.island.shape-pill { width: 330px; height: 48px; }
.island.shape-pill.mode-expanded { width: 460px; max-height: none; }
.island.mode-expanded { height: auto; }   /* content-sized, required for the spring */
```

**Rust side (src-tauri) — the other half of "flush docking":** CSS can only pin the
island *inside* its window; the window itself must be placed flush. In the window
setup / `move_notch_to_monitor` / `update_notch_size` handlers:

- Set the window position to the **top-center of the monitor's work area**:
  `x = monitor.x + (monitor.width - window.width) / 2`, `y = monitor.y` (use the work
  area so an auto-hiding taskbar doesn't overlap).
- On `update_notch_size`, keep the window centered as it resizes:
  `x -= (new_width - old_width) / 2` (top edge stays pinned at `y`).
- Keep `decorations: false` + `transparent: true` so the transparent window edge sits
  exactly at the screen edge with no chrome offset.

---

## 2. Anti-glitch / animation hardening

Add to `.island` (compose with what's already there):

```css
.island {
  /* existing */
  overflow: hidden;                    /* keep: clips content during open/close */
  transform-origin: top center;        /* scaling grows down from the top edge */
  -webkit-backface-visibility: hidden;
  backface-visibility: hidden;         /* GPU compositing + flicker guard */
  will-change: transform, width, height;
}
```

Fix the entrance keyframes — remove the translate that shifted the pill and made the
top edge settle (origin is now `top center`, so a scale grows downward in place):

```css
@keyframes island-in {
  0% { opacity: 0; transform: scale(.96); }
  to { opacity: 1; transform: scale(1); }
}
```

Panel collapse must hide without removing layout (or content pops during the tween)
and without creating a tab-order hole:

```css
.island.mode-collapsed .island-panel { visibility: hidden; }  /* was display: none */
```

Keep the strip `overflow: hidden` (add `min-height: 0` if it's a flex/grid child):

```css
.island-strip { /* existing */ overflow: hidden; min-height: 0; }
.island-panel { /* existing */ overflow: hidden; min-height: 0; }
```

---

## 3. Spring morph between collapsed and expanded

Add `interpolate-size` to the root (needed so `height: auto` ↔ `px` actually
interpolates — Chromium 129+; older browsers snap the height but still spring the
width):

```css
:root {
  /* existing custom properties */
  interpolate-size: allow-keywords;
}
```

And the spring transition on `.island` (the overshooting bezier gives the spring
feel; combine with the `transform-origin: top center` above so growth runs downward
from the top edge):

```css
.island {
  transition:
    width .55s cubic-bezier(.34, 1.56, .64, 1),
    height .55s cubic-bezier(.34, 1.56, .64, 1),
    border-radius .55s cubic-bezier(.34, 1.56, .64, 1);
}
```

Optional a11y guard — snap for users who opt out of motion:

```css
@media (prefers-reduced-motion: reduce) {
  .island { transition: none; }
}
```

---

## 4. Verify with the regression harness

The guarded test script at the end of `preview.html` (the block starting
`/* Winotch layout regression test - opt-in, dormant in normal use. */`) is
self-contained and dormant unless `?test=` is in the URL. Drop it into the real
app's `index.html` (before `</body>`), or keep a dev-only `preview.html` alongside
the source, then:

- `preview.html?test=1` — audits all 4 shape×mode states (9 assertions each: flush
  top, centering, size, origin, overflow, margins, will-change, backface, radius)
  plus global checks (shell centering, entrance keyframes, spring morph config) and
  shows a labeled contact sheet + spring morph demo.
- `preview.html?test=auto` — same audit with an auto-advancing showcase.

---

## What changed in `preview.html` (for reference)

1. `.island-shell` → `padding:0; margin:0`; preview `body` → `align-items:flex-start`
   (removed the vertical-centering gap above the notch).
2. `.island` → `top:0; margin:0; transform-origin:top center; backface-visibility:hidden
   (with -webkit-); will-change:transform,width,height; spring transition`.
3. `@keyframes island-in` → `scale(.96)` only (no `translateY`, no shift).
4. `:root` → `interpolate-size:allow-keywords`.
5. `.island.mode-expanded{height:auto}` (main + preview blocks) — unlocks content
   height and the height tween (was clipped at collapsed height).
6. `.island.mode-collapsed .island-panel{display:none}` — keep the panel out of flex
   layout when idle. Do NOT use `visibility:hidden` here: a hidden-but-in-layout panel
   (flex:1 1 auto with ~470px of widgets) squeezes the collapsed strip to ~0px and
   clips the clock text. The height spring still works with `display:none` because the
   collapsed height is explicit (36px) and the expanded height is `auto` (content),
   interpolated via `interpolate-size`; `overflow:hidden` clips during the tween.

## 5. Slim idle/collapsed height (native OS look)

Cap the idle pill at a slim fixed height so it reads as a native, unobtrusive
status element (applies only in collapsed mode — expanded keeps `height:auto`):

```css
.island.mode-collapsed {
  height: 36px;
  max-height: 36px;
}
```

In the preview this is backed by shape sizes `256×36` (notch) and `330×36` (pill).
The collapsed strip fills via its existing `flex:1 1 auto; height:100%` rule and
centers content (`align-items:center`), so no per-element line-height tweaks are
needed. If the real app animates the island box through framer-motion (the bundle
passes `animate={{width,height}}` for collapsed vs expanded), add `!important` to
the collapsed rule so it beats the inline styles, and make sure the window resize
(Rust side, `update_notch_size`) uses the same 36px collapsed height.
