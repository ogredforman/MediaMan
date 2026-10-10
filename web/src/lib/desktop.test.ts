import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mockTokens } from '../mocks/handlers';
import { createFakeDesktopPlayer } from '../test/fakeDesktopPlayer';
import type { MediamanDesktop } from './desktop';

// Expiries come from the clock, so each test compares against one copy.
const tokensA = mockTokens('a');
const savedTokens = mockTokens('saved');

/** A stand-in for the desktop app's preload bridge, recording every token write in order. */
function installBridge(options: { tokens?: unknown; baseUrl?: string | null } = {}) {
  const writes: string[] = [];
  let saved: unknown = options.tokens ?? null;
  const bridge: MediamanDesktop = {
    isDesktop: true,
    platform: 'linux',
    server: { baseUrl: () => Promise.resolve(options.baseUrl ?? null) },
    tokens: {
      get: () => Promise.resolve(saved),
      set: async (tokens) => {
        // Slow enough that an unordered clear would overtake it.
        await new Promise((resolve) => setTimeout(resolve, 20));
        writes.push('set');
        saved = tokens;
      },
      clear: () => {
        writes.push('clear');
        saved = null;
        return Promise.resolve();
      },
    },
    player: createFakeDesktopPlayer(),
  };
  window.mediamanDesktop = bridge;
  return { bridge, writes, saved: () => saved };
}

// The session and API base are module state, so each test loads fresh copies.
async function load() {
  const desktop = await import('./desktop');
  const session = await import('./session');
  const http = await import('./http');
  return { ...desktop, ...session, ...http };
}

beforeEach(() => {
  vi.resetModules();
});
afterEach(() => {
  delete window.mediamanDesktop;
});

describe('connectDesktop', () => {
  it('does nothing in a browser', async () => {
    const { connectDesktop, storeTokens, storedTokens, apiUrl } = await load();
    await connectDesktop();

    storeTokens(tokensA);
    expect(storedTokens()).toEqual(tokensA);
    expect(localStorage.getItem('mediaman.auth')).not.toBeNull();
    expect(apiUrl('/titles')).toBe(`${window.location.origin}/titles`);
  });

  it('starts from the tokens the desktop app saved and calls its server', async () => {
    installBridge({ tokens: savedTokens, baseUrl: 'http://127.0.0.1:8420/' });
    const { connectDesktop, storedTokens, isSignedIn, apiUrl } = await load();

    await connectDesktop();

    expect(storedTokens()).toEqual(savedTokens);
    expect(isSignedIn()).toBe(true);
    expect(apiUrl('/titles')).toBe('http://127.0.0.1:8420/titles');
  });

  it('starts signed out when the saved tokens are unreadable', async () => {
    installBridge({ tokens: { accessToken: 'x' } });
    const { connectDesktop, isSignedIn } = await load();

    await connectDesktop();

    expect(isSignedIn()).toBe(false);
  });

  it('writes every change back to the desktop app, in order, and never to localStorage', async () => {
    const bridge = installBridge();
    const { connectDesktop, storeTokens, clearTokens, storedTokens, onSessionChange } = await load();
    await connectDesktop();
    const changes = vi.fn();
    onSessionChange(changes);

    storeTokens(tokensA);
    expect(storedTokens()).toEqual(tokensA);
    clearTokens();
    expect(storedTokens()).toBeUndefined();

    await vi.waitFor(() => {
      expect(bridge.writes).toEqual(['set', 'clear']);
    });
    expect(bridge.saved()).toBeNull();
    expect(changes).toHaveBeenCalledTimes(2);
    expect(localStorage.getItem('mediaman.auth')).toBeNull();
  });

  it('keeps the session in memory when the desktop app cannot save it', async () => {
    const bridge = installBridge();
    bridge.bridge.tokens.set = () => Promise.reject(new Error('These are not valid sign-in tokens.'));
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { connectDesktop, storeTokens, storedTokens } = await load();
    await connectDesktop();

    storeTokens(tokensA);

    await vi.waitFor(() => {
      expect(error).toHaveBeenCalledWith(expect.stringContaining('Could not save the sign-in'));
    });
    expect(error.mock.calls.flat().join(' ')).not.toContain(tokensA.refreshToken);
    expect(storedTokens()).toEqual(tokensA);
    expect(bridge.writes).toEqual([]);
    error.mockRestore();
  });
});
