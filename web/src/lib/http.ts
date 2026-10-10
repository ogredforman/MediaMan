// mediaman.ts imports apiFetch from here; the cycle is safe because each side only calls the other at run time.
import { refreshTokens } from '../api/mediaman';
import type { AuthTokens, Problem } from '../api/mediaman.schemas';
import { clearTokens, refreshMarginMs, refreshSession, RefreshRejectedError, storedTokens } from './session';

let baseUrl: string = import.meta.env.VITE_API_BASE_URL ?? '';

/** Points the API at another server origin (the desktop app's server); overrides `VITE_API_BASE_URL`. */
export function setApiBaseUrl(url: string): void {
  baseUrl = url.replace(/\/+$/, '');
}

/** The absolute URL of a contract path, e.g. `/titles`. */
export function apiUrl(path: string): string {
  return `${baseUrl || window.location.origin}${path}`;
}

/** `RequestInit` plus `anonymous`: send no token and never refresh (signing in and refreshing itself). */
export type ApiRequestInit = RequestInit & { anonymous?: boolean };

/**
 * The stored session's access token, refreshed first when it's about to expire; undefined when signed out. A
 * failed refresh (e.g. the network) leaves the old token in place, so the request still goes out and reports it.
 */
async function currentTokens(): Promise<AuthTokens | undefined> {
  const tokens = storedTokens();
  if (tokens === undefined) return undefined;
  if (Date.parse(tokens.accessTokenExpiresAt) - Date.now() > refreshMarginMs) return tokens;
  try {
    return await refreshSession(tokens.refreshToken, exchangeRefreshToken);
  } catch {
    return tokens;
  }
}

async function exchangeRefreshToken(refreshToken: string): Promise<AuthTokens> {
  try {
    return await refreshTokens({ refreshToken }, { anonymous: true });
  } catch (error) {
    // 400 (malformed) or 401 (expired, revoked or reused): the session is over. Anything else may pass.
    if (error instanceof ApiError && (error.status === 400 || error.status === 401)) throw new RefreshRejectedError();
    throw error;
  }
}

/** Request headers with the bearer access token added when signed in, refreshing it first if it's about to expire. */
export async function withAuth(init?: HeadersInit): Promise<Headers> {
  const headers = new Headers(init);
  const tokens = await currentTokens();
  if (tokens !== undefined) headers.set('Authorization', `Bearer ${tokens.accessToken}`);
  return headers;
}

/** A non-2xx response; `problem` is the contract's error body (SPEC §11: what happened, what to do next). */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: Problem;

  constructor(problem: Problem) {
    super(problem.title);
    this.name = 'ApiError';
    this.status = problem.status;
    this.problem = problem;
  }
}

function isProblem(value: unknown): value is Problem {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.title === 'string' && typeof v.status === 'number' && typeof v.code === 'string';
}

async function send(url: string, init: RequestInit, tokens: AuthTokens | undefined): Promise<Response> {
  const headers = new Headers(init.headers);
  if (tokens !== undefined) headers.set('Authorization', `Bearer ${tokens.accessToken}`);
  try {
    return await fetch(apiUrl(url), { ...init, headers });
  } catch {
    throw new ApiError({
      title: "Can't reach the MediaMan server.",
      status: 503,
      code: 'network.unreachable',
      action: 'Check that the server is running and reachable, then try again.',
    });
  }
}

/**
 * The fetcher every generated client function calls (Orval mutator). Signed in, it sends the access token; a
 * `401 auth.required` (the token expired early, or another tab rotated it) refreshes once and retries, and a
 * `401` that a refresh can't fix signs out (SPEC §7.11).
 */
export async function apiFetch<T>(url: string, init: ApiRequestInit = {}): Promise<T> {
  const { anonymous = false, ...request } = init;
  const tokens = anonymous ? undefined : await currentTokens();

  let response = await send(url, request, tokens);
  let text = await response.text();
  let body = parseJson(text);

  if (response.status === 401 && tokens !== undefined) {
    const code = isProblem(body) ? body.code : undefined;
    const next =
      code === 'auth.required' ? await refreshSession(tokens.refreshToken, exchangeRefreshToken) : undefined;
    if (next === undefined) {
      clearTokens();
    } else {
      response = await send(url, request, next);
      text = await response.text();
      body = parseJson(text);
      if (response.status === 401) clearTokens();
    }
  }

  if (!response.ok) {
    throw new ApiError(
      isProblem(body)
        ? body
        : {
            title: `The server returned an unexpected error (${String(response.status)}).`,
            status: response.status,
            code: 'http.unexpected',
            action: 'Try again. If it keeps happening, check the server logs.',
          },
    );
  }
  if (body === invalidJson) {
    throw new ApiError({
      title: 'The server sent a response MediaMan could not read.',
      status: response.status,
      code: 'http.invalid_body',
      action: 'Check that the server address points at MediaMan, then try again.',
    });
  }
  return body as T;
}

const invalidJson = Symbol('invalidJson');

// A proxy in front of the server can answer with an HTML error page; that must still become an ApiError.
function parseJson(text: string): unknown {
  if (text === '') return undefined;
  try {
    return JSON.parse(text);
  } catch {
    return invalidJson;
  }
}

/** Error type for the generated hooks (Orval reads this export from the mutator file). */
export type ErrorType<_E> = ApiError;

/** Where an `ArtworkRef` loads from: its `url` is opaque and resolved against the API base as-is. */
export function artworkSrc(artwork: { url: string }): string {
  return apiUrl(artwork.url);
}
