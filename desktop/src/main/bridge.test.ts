import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { FakePlayer } from '../fakes/fakePlayer.js';
import { bridgeChannels, bridgeEvents, parsePlayOptions, parseUpNextOffer, registerBridge, type BridgeEvent, type IpcMainLike, type MpvChoice } from './bridge.js';
import { PlayerHost } from './playerHost.js';
import type { MpvSetting } from './desktopSettings.js';
import type { StoredTokens } from './tokenStore.js';

const uiOrigin = 'http://localhost:5173';
const fromUi: BridgeEvent = { senderFrame: { url: `${uiOrigin}/titles/1` } };

const tokens: StoredTokens = {
  accessToken: 'access',
  accessTokenExpiresAt: '2026-10-10T12:15:00Z',
  refreshToken: 'refresh',
  refreshTokenExpiresAt: '2026-11-09T12:00:00Z',
};

const serverOrigin = 'http://127.0.0.1:8420';
const playRequest = {
  url: `${serverOrigin}/media-files/f1/stream?sid=s&exp=1&sig=x`,
  startSeconds: 300,
  title: 'Alien (1979)',
  subtitles: [{ url: `${serverOrigin}/media-files/f1/subtitles/ab?sid=s&exp=1&sig=x`, language: 'en', title: 'Alien.en.srt' }],
};

function setUp(serverUrl: string | undefined) {
  const handlers = new Map<string, (event: BridgeEvent, ...args: unknown[]) => unknown>();
  const ipc: IpcMainLike = { handle: (channel, listener) => handlers.set(channel, listener) };
  const stored = new Map<string, StoredTokens>();
  const player = new FakePlayer();
  let chosen: MpvChoice = { outcome: 'picked', path: '/picked/mpv' };
  let mpv: MpvSetting = { path: '/picked/mpv', source: 'picked' };
  registerBridge(ipc, {
    uiOrigin,
    serverUrl: () => serverUrl,
    tokens: {
      get: (baseUrl) => Promise.resolve(stored.get(baseUrl)),
      set: (baseUrl, value) => Promise.resolve(void stored.set(baseUrl, value)),
      clear: (baseUrl) => Promise.resolve(void stored.delete(baseUrl)),
    },
    player: new PlayerHost(
      () => player,
      () => Promise.resolve('mpv'),
    ),
    chooseMpv: () => Promise.resolve(chosen),
    mpvSetting: () => Promise.resolve(mpv),
    useDefaultMpv: () => {
      mpv = { path: 'mpv', source: 'default' };
      return Promise.resolve(mpv);
    },
  });
  const call = (channel: string, event: BridgeEvent, ...args: unknown[]) =>
    Promise.resolve().then(() => handlers.get(channel)?.(event, ...args));
  const cancelPicker = () => {
    chosen = { outcome: 'cancelled' };
  };
  return { call, stored, player, cancelPicker };
}

/** The UI's window, recording what the main process sends it. */
function uiWindow() {
  const sent: unknown[][] = [];
  const event: BridgeEvent = {
    ...fromUi,
    sender: {
      send: (...args) => {
        sent.push(args);
      },
      isDestroyed: () => false,
    },
  };
  return { event, sent };
}

describe('registerBridge', () => {
  it("stores the tokens under the current server's URL", async () => {
    const { call, stored } = setUp(serverOrigin);

    await call(bridgeChannels.tokensSet, fromUi, tokens);

    expect([...stored.keys()]).toEqual([serverOrigin]);
    await expect(call(bridgeChannels.tokensGet, fromUi)).resolves.toEqual(tokens);
    await expect(call(bridgeChannels.serverUrl, fromUi)).resolves.toBe(serverOrigin);

    await call(bridgeChannels.tokensClear, fromUi);
    await expect(call(bridgeChannels.tokensGet, fromUi)).resolves.toBeNull();
  });

  it("falls back to the UI's origin when no server was started", async () => {
    const { call, stored } = setUp(undefined);

    await call(bridgeChannels.tokensSet, fromUi, tokens);

    expect([...stored.keys()]).toEqual([uiOrigin]);
    await expect(call(bridgeChannels.serverUrl, fromUi)).resolves.toBeNull();
  });

  it('rejects anything that is not the AuthTokens shape', async () => {
    const { call, stored } = setUp(undefined);

    await expect(call(bridgeChannels.tokensSet, fromUi, { ...tokens, accessToken: 42 })).rejects.toThrow(/not valid/);
    expect(stored.size).toBe(0);
  });

  it.each([
    ['another origin', { senderFrame: { url: 'https://evil.example/' } }],
    ['no frame', { senderFrame: null }],
    ['a non-URL frame', { senderFrame: { url: 'about:blank' } }],
  ])('refuses calls from %s', async (_, event: BridgeEvent) => {
    const { call, player } = setUp(serverOrigin);

    for (const channel of Object.values(bridgeChannels)) {
      await expect(call(channel, event, tokens)).rejects.toThrow(/MediaMan UI/);
    }
    expect(player.plays).toEqual([]);
  });

  it('plays a stream from the current server and sends its events to the window that asked', async () => {
    const { call, player } = setUp(serverOrigin);
    const { event, sent } = uiWindow();

    await expect(call(bridgeChannels.playerPlay, event, 'launch-1', playRequest)).resolves.toEqual({ ok: true });
    player.emitProgress({ positionSeconds: 12, durationSeconds: 7000, paused: true });
    await call(bridgeChannels.playerStop, event);

    expect(player.plays).toEqual([playRequest]);
    expect(sent).toEqual([
      [bridgeEvents.player, { launchId: 'launch-1', type: 'progress', positionSeconds: 12, durationSeconds: 7000, paused: true }],
      [bridgeEvents.player, { launchId: 'launch-1', type: 'ended', reason: 'quit' }],
    ]);
  });

  it('plays streams from the UI origin when no server was started', async () => {
    const { call, player } = setUp(undefined);
    const { event } = uiWindow();
    const viaUi = { ...playRequest, url: `${uiOrigin}/api/media-files/f1/stream?sig=x`, subtitles: [] };

    await expect(call(bridgeChannels.playerPlay, event, 'launch-1', viaUi)).resolves.toEqual({ ok: true });
    expect(player.plays).toEqual([viaUi]);
  });

  it('refuses to play anything that is not on the current server', async () => {
    const { call, player } = setUp(serverOrigin);
    const { event } = uiWindow();

    await expect(call(bridgeChannels.playerPlay, event, 'launch-1', { ...playRequest, url: 'file:///movie.mkv' })).rejects.toThrow(
      /current server/,
    );
    await expect(call(bridgeChannels.playerPlay, event, '', playRequest)).rejects.toThrow(/launch id/);
    expect(player.plays).toEqual([]);
  });

  it('shows Up next on the current launch and refuses an invalid offer', async () => {
    const { call, player } = setUp(serverOrigin);
    const { event } = uiWindow();
    const offer = { title: 'Show S01E02 · Two', seconds: 10 };
    await call(bridgeChannels.playerPlay, event, 'launch-1', playRequest);

    await call(bridgeChannels.playerUpNext, event, 'launch-1', offer);
    await expect(call(bridgeChannels.playerUpNext, event, 'launch-1', { ...offer, seconds: 0 })).rejects.toThrow(/Up next/);
    await expect(call(bridgeChannels.playerUpNext, event, 7, offer)).rejects.toThrow(/Up next/);

    expect(player.upNexts).toEqual([offer]);
  });

  it('answers the picked MPV, or cancelled when the picker is cancelled', async () => {
    const { call, cancelPicker } = setUp(undefined);

    await expect(call(bridgeChannels.playerChooseMpv, fromUi)).resolves.toEqual({ outcome: 'picked', path: '/picked/mpv' });
    cancelPicker();
    await expect(call(bridgeChannels.playerChooseMpv, fromUi)).resolves.toEqual({ outcome: 'cancelled' });
  });

  it('answers the MPV in use and goes back to the default', async () => {
    const { call } = setUp(undefined);

    await expect(call(bridgeChannels.playerMpvSetting, fromUi)).resolves.toEqual({ path: '/picked/mpv', source: 'picked' });
    await expect(call(bridgeChannels.playerUseDefaultMpv, fromUi)).resolves.toEqual({ path: 'mpv', source: 'default' });
    await expect(call(bridgeChannels.playerMpvSetting, fromUi)).resolves.toEqual({ path: 'mpv', source: 'default' });
    await expect(call(bridgeChannels.playerUseDefaultMpv, { senderFrame: { url: 'https://evil.example/' } })).rejects.toThrow();
  });

  it('uses the same channel names as the preload', () => {
    const preload = readFileSync(path.join(import.meta.dirname, '..', 'preload', 'index.cts'), 'utf8');

    for (const channel of [...Object.values(bridgeChannels), ...Object.values(bridgeEvents)]) {
      expect(preload).toContain(`'${channel}'`);
    }
  });
});

describe('parseUpNextOffer', () => {
  it('keeps a valid offer and refuses the rest', () => {
    expect(parseUpNextOffer({ title: 'Next', seconds: 10 })).toEqual({ title: 'Next', seconds: 10 });
    for (const value of [{ title: '', seconds: 10 }, { title: 'x'.repeat(501), seconds: 10 }, { title: 'Next', seconds: 1.5 }, { title: 'Next', seconds: 61 }, { seconds: 10 }, null]) {
      expect(parseUpNextOffer(value)).toBeUndefined();
    }
  });
});

describe('parsePlayOptions', () => {
  it('keeps a valid request', () => {
    expect(parsePlayOptions(playRequest, serverOrigin)).toEqual(playRequest);
  });

  it('keeps the language preferences', () => {
    const withLanguages = { ...playRequest, audioLanguages: ['de', 'ger', 'deu'], subtitleLanguages: [] };
    expect(parsePlayOptions(withLanguages, serverOrigin)).toEqual(withLanguages);
    // Five preferences with the most tags the server sends (`no` has six).
    const most = { ...playRequest, audioLanguages: Array.from({ length: 30 }, () => 'nno') };
    expect(parsePlayOptions(most, serverOrigin)).toEqual(most);
  });

  it("keeps an episode's neighbours", () => {
    const episode = { ...playRequest, episodes: { previous: 'Show S01E01 · One', next: 'Show S01E03 · Three' } };
    expect(parsePlayOptions(episode, serverOrigin)).toEqual(episode);
    const first = { ...playRequest, episodes: { next: 'Show S01E02 · Two' } };
    expect(parsePlayOptions(first, serverOrigin)).toEqual(first);
  });

  it.each([
    ['a stream on another server', { ...playRequest, url: 'https://elsewhere.example/stream' }],
    ['a local file', { ...playRequest, url: 'file:///movie.mkv' }],
    ['a subtitle on another server', { ...playRequest, subtitles: [{ url: 'https://elsewhere.example/a.srt' }] }],
    ['a negative start', { ...playRequest, startSeconds: -1 }],
    ['no title', { ...playRequest, title: undefined }],
    ['a non-boolean default', { ...playRequest, subtitles: [{ url: `${serverOrigin}/a.srt`, isDefault: 'yes' }] }],
    ["audio languages that aren't a list", { ...playRequest, audioLanguages: 'de' }],
    ['a subtitle language carrying a comma', { ...playRequest, subtitleLanguages: ['de,--script=x'] }],
    ["a language tag that isn't a string", { ...playRequest, audioLanguages: [7] }],
    ['too many language tags', { ...playRequest, audioLanguages: Array.from({ length: 31 }, () => 'en') }],
    ["episodes that aren't an object", { ...playRequest, episodes: 'next' }],
    ['an empty episode title', { ...playRequest, episodes: { next: ' ' } }],
    ["an episode title that isn't a string", { ...playRequest, episodes: { previous: 3 } }],
    ['no object', 'play'],
  ])('refuses %s', (_, value) => {
    expect(parsePlayOptions(value, serverOrigin)).toBeUndefined();
  });
});
