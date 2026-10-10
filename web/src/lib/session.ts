import type { AuthTokens } from '../api/mediaman.schemas';
import type { DesktopTokenBridge } from './desktop';

// The signed-in session, shared by every tab of this origin through localStorage. A refresh token works once and
// reusing one revokes the session (ADR 0008), so refreshes are single-flight across tabs: under a Web Lock where
// the browser has one, otherwise under a localStorage lease (Web Locks needs a secure context, and Server mode is
// usually plain http on the LAN). Inside the desktop app the tokens live in its encrypted store instead
// (keepTokensOnDesktop): read once before the first render, then kept in memory and written back on every change.

const storageKey = 'mediaman.auth';
const lockName = 'mediaman.refresh';
const leaseKey = 'mediaman.refresh-lease';

/** Refresh this long before the access token expires, so requests don't race its expiry. */
export const refreshMarginMs = 30_000;
// A lease outlives any one refresh request; a tab that dies holding it blocks others for at most this long.
const leaseMs = 15_000;
const leaseSettleMs = 20;
const leasePollMs = 50;

const listeners = new Set<() => void>();

function isTokens(value: unknown): value is AuthTokens {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v.accessToken === 'string' &&
    typeof v.accessTokenExpiresAt === 'string' &&
    typeof v.refreshToken === 'string' &&
    typeof v.refreshTokenExpiresAt === 'string'
  );
}

function readJson(key: string): unknown {
  const text = localStorage.getItem(key);
  if (text === null) return undefined;
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

interface DesktopSession {
  bridge: DesktopTokenBridge;
  tokens: AuthTokens | undefined;
}

// The desktop app has one window, so one in-memory copy is the whole truth.
let desktop: DesktopSession | undefined;
// Bridge writes run one at a time, so a sign-out can't land before the refresh that preceded it.
let desktopWrites: Promise<void> = Promise.resolve();

/**
 * Keeps the tokens in the desktop app's store from now on, starting from the ones it holds. Unreadable or missing
 * tokens start signed out; localStorage is never used for them on desktop.
 */
export async function keepTokensOnDesktop(bridge: DesktopTokenBridge): Promise<void> {
  const session: DesktopSession = { bridge, tokens: undefined };
  desktop = session;
  try {
    const value = await bridge.get();
    if (isTokens(value)) session.tokens = value;
  } catch (error) {
    console.error(`Could not read the saved sign-in from this device: ${errorMessage(error)}`);
  }
  notify();
}

function persistDesktop(write: (bridge: DesktopTokenBridge) => Promise<void>): void {
  const session = desktop;
  if (session === undefined) return;
  desktopWrites = desktopWrites
    .then(() => write(session.bridge))
    .catch((error: unknown) => {
      // The message never carries the tokens; the session still works until the app quits.
      console.error(`Could not save the sign-in on this device: ${errorMessage(error)}`);
    });
}

const errorMessage = (error: unknown) => (error instanceof Error ? error.message : String(error));

/** The stored tokens, or undefined when signed out (or the stored value is unreadable). */
export function storedTokens(): AuthTokens | undefined {
  if (desktop !== undefined) return desktop.tokens;
  const value = readJson(storageKey);
  return isTokens(value) ? value : undefined;
}

export function isSignedIn(): boolean {
  return storedTokens() !== undefined;
}

function notify(): void {
  for (const listener of listeners) listener();
}

export function storeTokens(tokens: AuthTokens): void {
  if (desktop !== undefined) {
    desktop.tokens = tokens;
    persistDesktop((bridge) => bridge.set(tokens));
    notify();
    return;
  }
  localStorage.setItem(storageKey, JSON.stringify(tokens));
  notify();
}

export function clearTokens(): void {
  if (desktop !== undefined) {
    if (desktop.tokens === undefined) return;
    desktop.tokens = undefined;
    persistDesktop((bridge) => bridge.clear());
    notify();
    return;
  }
  if (localStorage.getItem(storageKey) === null) return;
  localStorage.removeItem(storageKey);
  notify();
}

/** Calls `listener` when this tab or another one signs in, refreshes or signs out. Returns the unsubscribe. */
export function onSessionChange(listener: () => void): () => void {
  const onStorage = (event: StorageEvent) => {
    if (event.key === storageKey || event.key === null) listener();
  };
  listeners.add(listener);
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', onStorage);
  };
}

/** Thrown by an exchange when the server refuses the refresh token: the session is over. */
export class RefreshRejectedError extends Error {
  constructor() {
    super('The server refused the refresh token.');
    this.name = 'RefreshRejectedError';
  }
}

/**
 * Replaces `staleRefreshToken` with new tokens, at most once across every tab. Inside the lock the stored tokens are
 * read again: if another call or tab already rotated them, those are returned without a request. Returns undefined
 * (and signs out) when the session is over; rethrows anything else, such as a network error, keeping the tokens.
 */
export async function refreshSession(
  staleRefreshToken: string,
  exchange: (refreshToken: string) => Promise<AuthTokens>,
): Promise<AuthTokens | undefined> {
  return withRefreshLock(async () => {
    const current = storedTokens();
    if (current === undefined) return undefined;
    if (current.refreshToken !== staleRefreshToken) return current;
    try {
      const next = await exchange(staleRefreshToken);
      storeTokens(next);
      return next;
    } catch (error) {
      if (!(error instanceof RefreshRejectedError)) throw error;
      clearTokens();
      return undefined;
    }
  });
}

// Serialises callers within this tab; the Web Lock or lease covers other tabs.
let tail: Promise<unknown> = Promise.resolve();

function withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
  const run = async () => {
    const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;
    return locks === undefined ? withLease(work) : locks.request(lockName, work);
  };
  const result = tail.then(run, run);
  tail = result.catch(() => undefined);
  return result;
}

interface Lease {
  id: string;
  until: number;
}

function readLease(): Lease | undefined {
  const value = readJson(leaseKey);
  if (typeof value !== 'object' || value === null) return undefined;
  const v = value as Record<string, unknown>;
  return typeof v.id === 'string' && typeof v.until === 'number' ? { id: v.id, until: v.until } : undefined;
}

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

// Not atomic like a Web Lock, but two tabs racing for a free lease both write, wait, and only the last writer
// still finds its own id; the loser waits for the lease to be released or expire.
async function withLease<T>(work: () => Promise<T>): Promise<T> {
  // Math.random rather than crypto.randomUUID, which also needs a secure context; this only tells tabs apart.
  const id = `${String(Date.now())}-${Math.random().toString(36).slice(2)}`;
  for (;;) {
    const lease = readLease();
    if (lease === undefined || lease.until < Date.now()) {
      localStorage.setItem(leaseKey, JSON.stringify({ id, until: Date.now() + leaseMs }));
      await delay(leaseSettleMs);
      if (readLease()?.id === id) break;
    }
    await delay(leasePollMs);
  }
  try {
    return await work();
  } finally {
    if (readLease()?.id === id) localStorage.removeItem(leaseKey);
  }
}
