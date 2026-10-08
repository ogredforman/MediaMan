import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockTokens } from '../mocks/handlers';
import {
  clearTokens,
  onSessionChange,
  refreshSession,
  RefreshRejectedError,
  storedTokens,
  storeTokens,
} from './session';

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/** A Web Locks stand-in: one holder per name at a time, in request order. */
function installFakeLocks() {
  const tails = new Map<string, Promise<unknown>>();
  const requests: string[] = [];
  const locks = {
    request: (name: string, callback: () => Promise<unknown>) => {
      requests.push(name);
      const previous = tails.get(name) ?? Promise.resolve();
      const result = previous.then(callback, callback);
      tails.set(
        name,
        result.catch(() => undefined),
      );
      return result;
    },
  };
  Object.defineProperty(navigator, 'locks', { value: locks, configurable: true });
  return requests;
}

describe('session storage', () => {
  it('stores, reads and clears the tokens', () => {
    const tokens = mockTokens('a');
    storeTokens(tokens);
    expect(storedTokens()).toEqual(tokens);

    clearTokens();
    expect(storedTokens()).toBeUndefined();
  });

  it('treats an unreadable stored value as signed out', () => {
    localStorage.setItem('mediaman.auth', '{"accessToken":');
    expect(storedTokens()).toBeUndefined();
    localStorage.setItem('mediaman.auth', '{"accessToken":"x"}');
    expect(storedTokens()).toBeUndefined();
  });

  it('tells listeners about changes here and in other tabs', () => {
    const listener = vi.fn();
    const unsubscribe = onSessionChange(listener);

    storeTokens(mockTokens());
    window.dispatchEvent(new StorageEvent('storage', { key: 'mediaman.auth' }));
    window.dispatchEvent(new StorageEvent('storage', { key: 'something-else' }));
    expect(listener).toHaveBeenCalledTimes(2);

    unsubscribe();
    clearTokens();
    expect(listener).toHaveBeenCalledTimes(2);
  });
});

describe.each([
  ['under a Web Lock', true],
  ['under the localStorage lease', false],
])('refreshSession %s', (_name, withLocks) => {
  afterEach(() => {
    Reflect.deleteProperty(navigator, 'locks');
  });

  const setUp = () => (withLocks ? installFakeLocks() : []);

  it('sends one refresh for concurrent callers holding the same token', async () => {
    const lockRequests = setUp();
    storeTokens(mockTokens('stale'));
    const exchange = vi.fn(async () => {
      await delay(10);
      return mockTokens('fresh');
    });

    const results = await Promise.all([
      refreshSession('refresh-stale', exchange),
      refreshSession('refresh-stale', exchange),
      refreshSession('refresh-stale', exchange),
    ]);

    expect(exchange).toHaveBeenCalledTimes(1);
    expect(results.map((r) => r?.accessToken)).toEqual(['access-fresh', 'access-fresh', 'access-fresh']);
    expect(storedTokens()?.refreshToken).toBe('refresh-fresh');
    if (withLocks) expect(lockRequests).toEqual(['mediaman.refresh', 'mediaman.refresh', 'mediaman.refresh']);
  });

  it('adopts tokens another tab already rotated instead of reusing the old one', async () => {
    setUp();
    storeTokens(mockTokens('rotated-elsewhere'));
    const exchange = vi.fn(() => Promise.resolve(mockTokens('never')));

    const result = await refreshSession('refresh-stale', exchange);

    expect(exchange).not.toHaveBeenCalled();
    expect(result?.accessToken).toBe('access-rotated-elsewhere');
  });

  it('signs out when the server refuses the refresh token', async () => {
    setUp();
    storeTokens(mockTokens('stale'));

    const result = await refreshSession('refresh-stale', () => Promise.reject(new RefreshRejectedError()));

    expect(result).toBeUndefined();
    expect(storedTokens()).toBeUndefined();
  });

  it('keeps the tokens when the refresh fails for another reason', async () => {
    setUp();
    storeTokens(mockTokens('stale'));

    await expect(refreshSession('refresh-stale', () => Promise.reject(new Error('offline')))).rejects.toThrow(
      'offline',
    );

    expect(storedTokens()?.refreshToken).toBe('refresh-stale');
  });
});

describe('the localStorage lease', () => {
  it('waits while another tab holds it, then uses the tokens that tab stored', async () => {
    storeTokens(mockTokens('stale'));
    localStorage.setItem('mediaman.refresh-lease', JSON.stringify({ id: 'other-tab', until: Date.now() + 10_000 }));
    const exchange = vi.fn(() => Promise.resolve(mockTokens('never')));

    const pending = refreshSession('refresh-stale', exchange);
    await delay(120);
    expect(exchange).not.toHaveBeenCalled();

    // The other tab finishes its refresh and releases the lease.
    storeTokens(mockTokens('other-tab'));
    localStorage.removeItem('mediaman.refresh-lease');

    expect((await pending)?.accessToken).toBe('access-other-tab');
    expect(exchange).not.toHaveBeenCalled();
    expect(localStorage.getItem('mediaman.refresh-lease')).toBeNull();
  });

  it('takes over a lease whose holder went away', async () => {
    storeTokens(mockTokens('stale'));
    localStorage.setItem('mediaman.refresh-lease', JSON.stringify({ id: 'dead-tab', until: Date.now() - 1 }));

    const result = await refreshSession('refresh-stale', () => Promise.resolve(mockTokens('fresh')));

    expect(result?.accessToken).toBe('access-fresh');
  });
});
