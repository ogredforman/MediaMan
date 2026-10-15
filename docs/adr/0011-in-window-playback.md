# 0011. Play inside the app window: MPV embedded with --wid under a React overlay window

- **Status:** Accepted
- **Date:** 2026-10-15
- **PLAN task:** 2.5.11

## Context

Since Stage 1, playback opens MPV in a window of its own, with the purple Lua OSC (`desktop/mpv/osc.lua`) drawn by MPV.
Real use showed the cost: two windows in Alt-Tab and the taskbar, a player that looks nothing like the rest of the app,
and every control written twice (Lua now, React for the browser player in Stage 5). The goal is one window: the video
in the main window, with the controls drawn in React from the shared web UI.

Constraints: MPV stays a separate process where it can (an MPV crash must not take the app down, and per-platform native
builds are expensive; SPEC §10.1), the code stays cross-platform (SPEC §9), and HDR and subtitles must render as they
do in MPV's own window.

## Options considered

1. **Keep the separate MPV window.** No work, but none of the problems above go away.
2. **MPV as a separate process embedded with `--wid`, controls in a transparent overlay window.** MPV draws into a
   child of the main window's native handle; a frameless, transparent, `skipTaskbar` `BrowserWindow` owned by the main
   window (`parent: mainWindow`) sits on its content bounds and draws the React controls. MPV keeps its own process,
   build and crash domain. Works on Windows (HWND) and X11 (XID); macOS can't host another process's view.
3. **libmpv in-process everywhere** (an N-API addon rendering into the window). One mechanism on every platform, but a
   native addon to build per platform and Electron version, and an MPV crash kills the app.
4. **Option 2 on Windows and Linux, libmpv only on macOS.** The addon is confined to the one platform that needs it.

## The spike (Windows 11, Electron 44.5.1, a local mpv 0.41 with d3d11/gpu-next, RTX 3080)

Only Windows was spiked; the Linux (`--wid` on X11/XWayland) and macOS (libmpv addon) paths are checked in 8.3 and 8.6.

A throwaway Electron script (deleted after this ADR) spawned MPV with `--wid=<getNativeWindowHandle() as a decimal>`,
`--input-default-bindings=no`, `--input-vo-keyboard=no`, `--osc=no` and an IPC pipe, and opened the overlay window
described in option 2, kept on `mainWindow.getContentBounds()` on `move`, `resize`, `maximize`, `unmaximize`,
`enter-full-screen`, `leave-full-screen` and `restore`, shown with `showInactive()`. Results:

- **Chromium covers MPV by default.** With DirectComposition on (Chromium's default on Windows) the main window gets
  a child `Intermediate D3D Window` above MPV's `mpv` child, so the page's HTML hides the video; MPV is playing
  underneath (IPC, progress and subtitles all work). Hiding the page (a `BaseWindow` whose `WebContentsView` is
  hidden) doesn't help: the root view's compositor surface still covers MPV. Raising MPV's child with `SetWindowPos`
  fixes it (Chromium didn't push it back on resize), but needs native code in the main process.
- **`app.commandLine.appendSwitch('disable-direct-composition')` fixes it without native code.** Chromium then
  creates no intermediate window; the only other child, `Chrome_RenderWidgetHostHWND`, is input-only and transparent
  (`WS_EX_TRANSPARENT`), and MPV's video covers the page as intended. The transparent overlay window still renders
  with per-pixel transparency over the video.
- **Bounds:** the overlay matched the content bounds after move+resize, maximize, fullscreen (1920×1080 on the
  display), leaving fullscreen, and minimize/restore (hidden with its owner, shown again on restore).
- **Input:** real OS clicks (`SendInput` at the button's screen point) and key presses reach the overlay's page and,
  through IPC to MPV, pause and seek; a double-click toggled fullscreen.
- **One window:** the overlay is an owned window without `WS_EX_APPWINDOW`, so Windows lists only the main window in
  Alt-Tab and, with `skipTaskbar`, the taskbar.
- **Subtitles:** an external `.srt` rendered by MPV in the embedded surface, under the overlay.
- **HDR:** a PQ/BT.2020 clip is decoded as PQ and the swapchain follows the display MPV's child is on (flip model,
  `Queried output … colorspace` in MPV's log), the same code path as MPV's own window. Only an SDR display was
  available, where MPV tone-mapped it correctly; passthrough on an HDR display is checked in 2.5.14.

## Decision

Option 4. On Windows and Linux the desktop app embeds MPV, still a separate process over JSON IPC, with `--wid` into
the main window, and draws the player controls in React in a transparent overlay window owned by the main window and
kept on its content bounds. On Windows the app starts with Chromium's `disable-direct-composition` switch so MPV's
surface isn't covered. On Linux Electron is forced onto X11 (`ozone-platform=x11`), which XWayland covers on Wayland
desktops, because `--wid` needs an X11 window id. macOS, which can't host another process's view, loads libmpv
in-process through a small N-API addon under the same overlay (Stage 8). libmpv isn't used everywhere because of the
per-platform native builds and because an MPV crash would take down the app. ffmpeg and ffprobe always stay separate
processes.

## Consequences

- One window and one look: the Lua OSC is retired (2.5.14) for a React OSD (2.5.13) that Stage 5's browser player reuses.
- `IPlayer` grows state and commands, since the overlay draws what MPV used to (2.5.12).
- The overlay's bounds must follow every main-window bounds change, and focus must return to the main window when
  playback ends; both live in one main-process overlay controller (2.5.14).
- `disable-direct-composition` applies to the whole app: Chromium composites without DirectComposition on Windows (no
  hardware video overlays for `<video>` in the desktop app). If Chromium drops the switch, the fallback is raising
  MPV's child window with `SetWindowPos` from the main process, which needs a native call (an FFI dependency, its own ADR).
- Linux needs an X11 session or XWayland; checked when the AppImage is built (8.3). macOS needs the libmpv addon (Stage 8).
- SPEC §4, §7.4, §10.1, §10.3 and §11, the AGENTS.md hard rule on separate processes and `.claude/rules/desktop.md`
  describe this; keep them in step.
