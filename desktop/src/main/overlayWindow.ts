// The player overlay window (ADR 0011): a transparent window owned by the main window, kept on its content bounds,
// drawing the React OSD over the MPV that plays inside the main window. Shown while something plays; hidden, with the
// keyboard back on the main window, however playback ends. Kept free of runtime Electron imports so it can be tested.
import type { BrowserWindow, Rectangle, Screen } from 'electron';
import { bridgeEvents, type BridgeSender, type OverlayLink } from './bridge.js';
import type { HostState } from './playerHost.js';

/** The parts of the main window the overlay follows. */
export interface MainWindowLike {
  getContentBounds(): Rectangle;
  /** The bounds of the display the window is on. */
  getDisplayBounds(): Rectangle;
  isFullScreen(): boolean;
  setFullScreen(on: boolean): void;
  isMaximized(): boolean;
  minimize(): void;
  maximize(): void;
  unmaximize(): void;
  close(): void;
  focus(): void;
  isDestroyed(): boolean;
  /** Calls back whenever the content bounds may have changed: move, resize, maximize, fullscreen, restore. */
  onBoundsChange(listener: () => void): void;
  /** Calls back once the window has entered or left fullscreen. */
  onFullscreenChange(listener: () => void): void;
}

/** The parts of the overlay window it drives. */
export interface OverlayWindowLike {
  readonly webContents: BridgeSender;
  setBounds(bounds: Rectangle): void;
  /** Shows the window and gives it the keyboard. */
  show(): void;
  hide(): void;
  isDestroyed(): boolean;
}

/** What the overlay page draws (the web UI's `DesktopOverlayState`). */
export interface OverlayState extends HostState {
  fullscreen: boolean;
  /** For the window controls the overlay draws: Restore instead of Maximize. */
  maximized: boolean;
}

export class OverlayWindow implements OverlayLink {
  private overlay: OverlayWindowLike | undefined;
  private state: HostState | undefined;
  private maximized: boolean | undefined;

  /** @param create makes the overlay window, owned by the main window and loading the `/player` page; called on first show. */
  constructor(
    private readonly main: MainWindowLike,
    private readonly create: () => OverlayWindowLike,
  ) {
    main.onBoundsChange(() => {
      this.fit();
      // Only a change of maximized is news to the overlay, not every move and resize.
      if (!main.isDestroyed() && main.isMaximized() !== this.maximized) this.send();
    });
    main.onFullscreenChange(() => {
      this.send();
    });
  }

  contents(): BridgeSender | undefined {
    return this.live()?.webContents;
  }

  /** Shows the overlay over the main window's content and gives it the keyboard. */
  show(): void {
    if (this.main.isDestroyed()) return;
    let overlay = this.live();
    if (!overlay) {
      overlay = this.create();
      this.overlay = overlay;
    }
    this.fit();
    overlay.show();
  }

  /** Hides the overlay, leaves fullscreen and gives the keyboard back to the main window, on the page it played from. */
  hide(): void {
    this.state = undefined;
    this.live()?.hide();
    if (this.main.isDestroyed()) return;
    if (this.main.isFullScreen()) this.main.setFullScreen(false);
    this.main.focus();
  }

  toggleFullscreen(): void {
    if (this.main.isDestroyed()) return;
    this.main.setFullScreen(!this.main.isFullScreen());
  }

  minimize(): void {
    if (this.main.isDestroyed()) return;
    this.main.minimize();
  }

  toggleMaximize(): void {
    if (this.main.isDestroyed()) return;
    if (this.main.isMaximized()) this.main.unmaximize();
    else this.main.maximize();
  }

  close(): void {
    if (this.main.isDestroyed()) return;
    this.main.close();
  }

  /** The current file's state, sent on with the main window's fullscreen. */
  setState(state: HostState): void {
    this.state = state;
    this.send();
  }

  resend(): void {
    this.send();
  }

  private send(): void {
    const contents = this.contents();
    if (!this.state || !contents || contents.isDestroyed() || this.main.isDestroyed()) return;
    this.maximized = this.main.isMaximized();
    const state: OverlayState = { ...this.state, fullscreen: this.main.isFullScreen(), maximized: this.maximized };
    contents.send(bridgeEvents.playerState, state);
  }

  /**
   * In fullscreen the overlay covers the whole display, not the content bounds: on Windows those can come back a pixel
   * short after the app is refocused, and the overlay, the foreground window, then no longer counts as fullscreen, so
   * the taskbar stays over the video on the next refocus.
   */
  private fit(): void {
    const overlay = this.live();
    if (!overlay || this.main.isDestroyed()) return;
    overlay.setBounds(this.main.isFullScreen() ? this.main.getDisplayBounds() : this.main.getContentBounds());
  }

  private live(): OverlayWindowLike | undefined {
    return this.overlay && !this.overlay.isDestroyed() ? this.overlay : undefined;
  }
}

/** The main window as the overlay follows it. */
export function followedWindow(win: BrowserWindow, screen: Screen): MainWindowLike {
  return {
    getContentBounds: () => win.getContentBounds(),
    getDisplayBounds: () => screen.getDisplayMatching(win.getBounds()).bounds,
    isFullScreen: () => win.isFullScreen(),
    setFullScreen: (on) => {
      win.setFullScreen(on);
    },
    isMaximized: () => win.isMaximized(),
    minimize: () => {
      win.minimize();
    },
    maximize: () => {
      win.maximize();
    },
    unmaximize: () => {
      win.unmaximize();
    },
    close: () => {
      win.close();
    },
    focus: () => {
      win.focus();
    },
    isDestroyed: () => win.isDestroyed(),
    onBoundsChange(listener) {
      win.on('move', listener);
      win.on('resize', listener);
      win.on('maximize', listener);
      win.on('unmaximize', listener);
      win.on('restore', listener);
      win.on('enter-full-screen', listener);
      win.on('leave-full-screen', listener);
    },
    onFullscreenChange(listener) {
      win.on('enter-full-screen', listener);
      win.on('leave-full-screen', listener);
    },
  };
}
