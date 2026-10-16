import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MediamanDesktop } from '../../lib/desktop';
import { createFakeDesktopPlayer } from '../../test/fakeDesktopPlayer';
import { createFakePlayerController, playerView, type FakePlayerController } from '../../test/fakePlayerController';
import { renderApp } from '../../test/renderApp';
import { idleMs } from './controls';
import { PlayerOverlay } from './PlayerOverlay';

const episodes = { previous: 'The Office S01E01 · Pilot', next: 'The Office S01E03 · Health Care' };

function renderOverlay(controller: FakePlayerController = createFakePlayerController()) {
  const { container } = render(<PlayerOverlay controller={controller} />);
  const root = container.querySelector<HTMLElement>('[data-controls]');
  if (!root) throw new Error('No overlay root.');
  return { controller, root };
}

describe('PlayerOverlay', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the title, the time and the transport, and sends what each control asks for', async () => {
    const user = userEvent.setup();
    const { controller } = renderOverlay(createFakePlayerController(playerView({ title: 'The Office S01E02 · Diversity Day', episodes })));
    expect(screen.getByRole('heading', { name: 'The Office S01E02 · Diversity Day' })).toBeInTheDocument();
    expect(screen.getByText('12:00')).toBeInTheDocument();
    expect(screen.getByText('/ 1:30:00')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Back' }));
    await user.click(screen.getByRole('button', { name: 'Previous episode: The Office S01E01 · Pilot' }));
    await user.click(screen.getByRole('button', { name: 'Back 10 seconds' }));
    await user.click(screen.getByRole('button', { name: 'Pause' }));
    await user.click(screen.getByRole('button', { name: 'Forward 30 seconds' }));
    await user.click(screen.getByRole('button', { name: 'Next episode: The Office S01E03 · Health Care' }));
    await user.click(screen.getByRole('button', { name: 'Fullscreen' }));
    expect(controller.actions).toEqual([
      { type: 'back' },
      { type: 'episode', choice: 'previous' },
      { type: 'seekBy', seconds: -10 },
      { type: 'togglePause' },
      { type: 'seekBy', seconds: 30 },
      { type: 'episode', choice: 'next' },
      { type: 'toggleFullscreen' },
    ]);
  });

  it('follows the state: Play while paused, Exit fullscreen, buffering, and no episode buttons for a movie', () => {
    const { controller } = renderOverlay();
    expect(screen.queryByRole('button', { name: /episode/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();

    act(() => {
      controller.update({ paused: true, fullscreen: true, buffering: true });
    });
    expect(screen.getByRole('button', { name: 'Play' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Exit fullscreen' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Buffering…');
  });

  it('says playback is starting and still offers Back before the first state', async () => {
    const user = userEvent.setup();
    const { controller } = renderOverlay(createFakePlayerController(null));
    expect(screen.getByRole('status')).toHaveTextContent('Starting playback…');
    expect(screen.queryByRole('slider', { name: 'Seek' })).not.toBeInTheDocument();
    await user.keyboard(' ');
    await user.click(screen.getByRole('button', { name: 'Back' }));
    expect(controller.actions).toEqual([{ type: 'back' }]);
  });

  describe('seek bar', () => {
    function measured(slider: HTMLElement) {
      const track = slider.firstElementChild as HTMLElement;
      track.getBoundingClientRect = () => ({ left: 100, width: 400, top: 0, right: 500, bottom: 4, height: 4, x: 100, y: 0, toJSON: () => ({}) });
      return slider;
    }

    it('draws the played and buffered parts and a tick per chapter after the start', () => {
      renderOverlay();
      const slider = screen.getByRole('slider', { name: 'Seek' });
      expect(slider).toHaveAttribute('aria-valuenow', '720');
      expect(slider).toHaveAttribute('aria-valuetext', '12:00 of 1:30:00');
      expect(slider.querySelector<HTMLElement>('[data-part="played"]')?.style.width).toBe(`${String((720 / 5400) * 100)}%`);
      expect(slider.querySelector<HTMLElement>('[data-part="buffered"]')?.style.width).toBe(`${String((900 / 5400) * 100)}%`);
      const ticks = slider.querySelectorAll<HTMLElement>('[data-part="chapter"]');
      expect([...ticks].map((t) => t.style.left)).toEqual(['50%']);
    });

    it('shows the time and chapter under the pointer, and seeks where it is clicked or dropped', () => {
      const { controller } = renderOverlay();
      const slider = measured(screen.getByRole('slider', { name: 'Seek' }));

      fireEvent.mouseMove(slider, { clientX: 400 });
      expect(screen.getByRole('tooltip')).toHaveTextContent('Middle1:07:30');

      fireEvent.mouseDown(slider, { clientX: 300, button: 0 });
      expect(slider).toHaveAttribute('aria-valuenow', '2700');
      fireEvent.mouseMove(window, { clientX: 200 });
      expect(slider).toHaveAttribute('aria-valuenow', '1350');
      expect(controller.actions).toEqual([]);
      fireEvent.mouseUp(window, { clientX: 200 });
      expect(controller.actions).toEqual([{ type: 'seekTo', seconds: 1350 }]);
      // The drop holds until the player reports where it is now.
      expect(slider).toHaveAttribute('aria-valuenow', '1350');
      act(() => {
        controller.update({ positionSeconds: 1351 });
      });
      expect(slider).toHaveAttribute('aria-valuenow', '1351');
    });

    it('goes to the start and the end with Home and End, and does nothing until the duration is known', async () => {
      const user = userEvent.setup();
      const { controller } = renderOverlay();
      screen.getByRole('slider', { name: 'Seek' }).focus();
      await user.keyboard('{Home}{End}');
      expect(controller.actions).toEqual([
        { type: 'seekTo', seconds: 0 },
        { type: 'seekTo', seconds: 5400 },
      ]);

      act(() => {
        controller.update({ durationSeconds: 0 });
      });
      const slider = measured(screen.getByRole('slider', { name: 'Seek' }));
      fireEvent.mouseDown(slider, { clientX: 300, button: 0 });
      fireEvent.mouseUp(window, { clientX: 300 });
      await user.keyboard('{Home}');
      expect(controller.actions).toHaveLength(2);
    });
  });

  it('mutes from the volume button, sets the volume from its slider and adjusts it with the wheel', async () => {
    const user = userEvent.setup();
    const { controller } = renderOverlay(createFakePlayerController(playerView({ volume: 98 })));
    await user.click(screen.getByRole('button', { name: 'Mute' }));
    fireEvent.change(screen.getByRole('slider', { name: 'Volume' }), { target: { value: '30' } });
    fireEvent.wheel(screen.getByRole('button', { name: 'Mute' }), { deltaY: -100 });
    fireEvent.wheel(screen.getByRole('slider', { name: 'Volume' }), { deltaY: 100 });
    expect(controller.actions).toEqual([
      { type: 'toggleMute' },
      { type: 'setVolume', volume: 30 },
      { type: 'setVolume', volume: 100 },
      { type: 'setVolume', volume: 93 },
    ]);

    act(() => {
      controller.update({ muted: true });
    });
    expect(screen.getByRole('button', { name: 'Unmute' })).toBeInTheDocument();
    expect(screen.getByRole('slider', { name: 'Volume' })).toHaveAttribute('aria-valuetext', '98%, muted');
  });

  describe('keyboard', () => {
    it('plays, seeks, sets the volume, mutes, toggles fullscreen and changes episode', async () => {
      const user = userEvent.setup();
      const { controller } = renderOverlay(createFakePlayerController(playerView({ episodes, volume: 50 })));
      await user.keyboard(' k{ArrowLeft}{ArrowRight}{ArrowUp}{ArrowDown}mfnp');
      expect(controller.actions).toEqual([
        { type: 'togglePause' },
        { type: 'togglePause' },
        { type: 'seekBy', seconds: -10 },
        { type: 'seekBy', seconds: 30 },
        { type: 'setVolume', volume: 55 },
        { type: 'setVolume', volume: 45 },
        { type: 'toggleMute' },
        { type: 'toggleFullscreen' },
        { type: 'episode', choice: 'next' },
        { type: 'episode', choice: 'previous' },
      ]);
    });

    it('ignores N and P without those episodes, and keys with Ctrl', async () => {
      const user = userEvent.setup();
      const { controller } = renderOverlay();
      await user.keyboard('np{Control>}k{/Control}');
      expect(controller.actions).toEqual([]);
    });

    it('repeats seeking and volume while a key is held, but not toggles', () => {
      const { controller } = renderOverlay();
      fireEvent.keyDown(document.body, { key: 'f', repeat: true });
      fireEvent.keyDown(document.body, { key: 'm', repeat: true });
      fireEvent.keyDown(document.body, { key: 'ArrowRight', repeat: true });
      expect(controller.actions).toEqual([{ type: 'seekBy', seconds: 30 }]);
    });

    it('leaves fullscreen on Esc, then goes Back', async () => {
      const user = userEvent.setup();
      const { controller } = renderOverlay(createFakePlayerController(playerView({ fullscreen: true })));
      await user.keyboard('{Escape}');
      act(() => {
        controller.update({ fullscreen: false });
      });
      await user.keyboard('{Escape}');
      expect(controller.actions).toEqual([{ type: 'toggleFullscreen' }, { type: 'back' }]);
    });

    it('lets a focused button take Space and the volume slider take its arrows', async () => {
      const user = userEvent.setup();
      const { controller } = renderOverlay();
      screen.getByRole('button', { name: 'Forward 30 seconds' }).focus();
      await user.keyboard(' ');
      screen.getByRole('slider', { name: 'Volume' }).focus();
      await user.keyboard('{ArrowLeft}');
      expect(controller.actions.filter((a) => a.type === 'togglePause' || a.type === 'seekBy')).toEqual([{ type: 'seekBy', seconds: 30 }]);
    });

    it('toggles fullscreen on a double-click on the video, not on a control', async () => {
      const user = userEvent.setup();
      const { controller, root } = renderOverlay();
      await user.dblClick(screen.getByRole('button', { name: 'Pause' }));
      expect(controller.actions.filter((a) => a.type === 'toggleFullscreen')).toEqual([]);
      await user.dblClick(root);
      expect(controller.actions.filter((a) => a.type === 'toggleFullscreen')).toEqual([{ type: 'toggleFullscreen' }]);
    });

    it('reaches every control with Tab, in order', async () => {
      const user = userEvent.setup();
      renderOverlay(createFakePlayerController(playerView({ episodes })));
      const order = [
        screen.getByRole('button', { name: 'Back' }),
        screen.getByRole('slider', { name: 'Seek' }),
        screen.getByRole('button', { name: /^Previous episode/ }),
        screen.getByRole('button', { name: 'Back 10 seconds' }),
        screen.getByRole('button', { name: 'Pause' }),
        screen.getByRole('button', { name: 'Forward 30 seconds' }),
        screen.getByRole('button', { name: /^Next episode/ }),
        screen.getByRole('button', { name: 'Mute' }),
        screen.getByRole('slider', { name: 'Volume' }),
        screen.getByRole('button', { name: 'Fullscreen' }),
      ];
      for (const control of order) {
        await user.tab();
        expect(control).toHaveFocus();
      }
    });
  });

  describe('idle', () => {
    it('hides the controls and cursor after 3 s without input while playing, and shows them on a move or key', () => {
      vi.useFakeTimers();
      const { root } = renderOverlay();
      expect(root).toHaveAttribute('data-controls', 'shown');
      act(() => {
        vi.advanceTimersByTime(idleMs - 1);
      });
      expect(root).toHaveAttribute('data-controls', 'shown');
      act(() => {
        vi.advanceTimersByTime(1);
      });
      expect(root).toHaveAttribute('data-controls', 'hidden');
      expect(root).toHaveClass('cursor-none');

      fireEvent.mouseMove(root);
      expect(root).toHaveAttribute('data-controls', 'shown');
      expect(root).not.toHaveClass('cursor-none');

      act(() => {
        vi.advanceTimersByTime(idleMs);
      });
      expect(root).toHaveAttribute('data-controls', 'hidden');
      fireEvent.keyDown(window, { key: 'Shift' });
      expect(root).toHaveAttribute('data-controls', 'shown');
    });

    it('shows the controls on the Tab keydown, before focus moves, so focus never lands on a hidden control', () => {
      vi.useFakeTimers();
      const { root } = renderOverlay();
      act(() => {
        vi.advanceTimersByTime(idleMs);
      });
      expect(root).toHaveAttribute('data-controls', 'hidden');
      fireEvent.keyDown(document.body, { key: 'Tab' });
      expect(root).toHaveAttribute('data-controls', 'shown');
    });

    it('keeps the controls up while paused', () => {
      vi.useFakeTimers();
      const { controller, root } = renderOverlay(createFakePlayerController(playerView({ paused: true })));
      act(() => {
        vi.advanceTimersByTime(idleMs * 2);
      });
      expect(root).toHaveAttribute('data-controls', 'shown');
      act(() => {
        controller.update({ paused: false });
      });
      expect(root).toHaveAttribute('data-controls', 'hidden');
    });
  });
});

describe('/player', () => {
  afterEach(() => {
    delete window.mediamanDesktop;
    document.documentElement.classList.remove('player-page');
  });

  it('in a browser, says nothing plays here and links Home', async () => {
    renderApp('/player');
    expect(await screen.findByRole('heading', { name: 'Nothing is playing here' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to Home' })).toBeInTheDocument();
    expect(document.documentElement).not.toHaveClass('player-page');
  });

  it('in the desktop app, draws the player over a transparent page from the bridge and commands it', async () => {
    const user = userEvent.setup();
    const player = createFakeDesktopPlayer();
    const bridge: MediamanDesktop = {
      isDesktop: true,
      platform: 'win32',
      server: { baseUrl: () => Promise.resolve(null) },
      tokens: { get: () => Promise.resolve(null), set: () => Promise.resolve(), clear: () => Promise.resolve() },
      player,
    };
    window.mediamanDesktop = bridge;
    renderApp('/player');
    expect(await screen.findByText('Starting playback…')).toBeInTheDocument();
    expect(document.documentElement).toHaveClass('player-page');

    const { fullscreen: _fullscreen, ...state } = playerView();
    act(() => {
      player.emitState(state);
    });
    const header = await screen.findByRole('banner');
    expect(within(header).getByRole('heading', { name: 'Alien (1979)' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Pause' }));
    expect(player.commands).toEqual([{ type: 'togglePause' }]);
  });
});
