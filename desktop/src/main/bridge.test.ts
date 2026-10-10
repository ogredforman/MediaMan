import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { bridgeChannels, registerBridge, type BridgeEvent, type IpcMainLike } from './bridge.js';
import type { StoredTokens } from './tokenStore.js';

const uiOrigin = 'http://localhost:5173';
const fromUi: BridgeEvent = { senderFrame: { url: `${uiOrigin}/titles/1` } };

const tokens: StoredTokens = {
  accessToken: 'access',
  accessTokenExpiresAt: '2026-10-10T12:15:00Z',
  refreshToken: 'refresh',
  refreshTokenExpiresAt: '2026-11-09T12:00:00Z',
};

function setUp(serverUrl: string | undefined) {
  const handlers = new Map<string, (event: BridgeEvent, ...args: unknown[]) => unknown>();
  const ipc: IpcMainLike = { handle: (channel, listener) => handlers.set(channel, listener) };
  const stored = new Map<string, StoredTokens>();
  registerBridge(ipc, {
    uiOrigin,
    serverUrl: () => serverUrl,
    tokens: {
      get: (baseUrl) => Promise.resolve(stored.get(baseUrl)),
      set: (baseUrl, value) => Promise.resolve(void stored.set(baseUrl, value)),
      clear: (baseUrl) => Promise.resolve(void stored.delete(baseUrl)),
    },
  });
  const call = (channel: string, event: BridgeEvent, ...args: unknown[]) =>
    Promise.resolve().then(() => handlers.get(channel)?.(event, ...args));
  return { call, stored };
}

describe('registerBridge', () => {
  it("stores the tokens under the current server's URL", async () => {
    const { call, stored } = setUp('http://127.0.0.1:8420');

    await call(bridgeChannels.tokensSet, fromUi, tokens);

    expect([...stored.keys()]).toEqual(['http://127.0.0.1:8420']);
    await expect(call(bridgeChannels.tokensGet, fromUi)).resolves.toEqual(tokens);
    await expect(call(bridgeChannels.serverUrl, fromUi)).resolves.toBe('http://127.0.0.1:8420');

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
    const { call } = setUp('http://127.0.0.1:8420');

    for (const channel of Object.values(bridgeChannels)) {
      await expect(call(channel, event, tokens)).rejects.toThrow(/MediaMan UI/);
    }
  });

  it('uses the same channel names as the preload', () => {
    const preload = readFileSync(path.join(import.meta.dirname, '..', 'preload', 'index.cts'), 'utf8');

    for (const channel of Object.values(bridgeChannels)) expect(preload).toContain(`'${channel}'`);
  });
});
