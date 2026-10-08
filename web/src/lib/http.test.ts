import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { mockTokens } from '../mocks/handlers';
import { server } from '../mocks/node';
import { apiFetch, ApiError } from './http';
import { storedTokens, storeTokens } from './session';

const unauthorized = (code: string) =>
  HttpResponse.json({ title: 'Sign in.', status: 401, code }, { status: 401, headers: { 'Content-Type': 'application/problem+json' } });

async function failure(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (error) {
    if (error instanceof ApiError) return error;
    throw error;
  }
  throw new Error('Expected the request to fail.');
}

describe('apiFetch', () => {
  it('sends the stored access token', async () => {
    let authorization: string | null = null;
    server.use(
      http.get('*/probe', ({ request }) => {
        authorization = request.headers.get('Authorization');
        return HttpResponse.json({ ok: true });
      }),
    );
    storeTokens(mockTokens('123'));

    await apiFetch('/probe');

    expect(authorization).toBe('Bearer access-123');
  });

  it('turns a non-JSON error page into an ApiError with a next action', async () => {
    server.use(http.get('*/probe', () => HttpResponse.text('<html>Bad gateway</html>', { status: 502 })));

    const error = await failure(apiFetch('/probe'));

    expect(error.problem.code).toBe('http.unexpected');
    expect(error.status).toBe(502);
    expect(error.problem.action).toBeTruthy();
  });

  it('turns an unreadable success body into an ApiError', async () => {
    server.use(http.get('*/probe', () => HttpResponse.text('<html>Not MediaMan</html>')));

    const error = await failure(apiFetch('/probe'));

    expect(error.problem.code).toBe('http.invalid_body');
    expect(error.problem.action).toBeTruthy();
  });

  it('sends no token when signed out', async () => {
    let authorization: string | null = 'unset';
    server.use(
      http.get('*/probe', ({ request }) => {
        authorization = request.headers.get('Authorization');
        return HttpResponse.json({ ok: true });
      }),
    );

    await apiFetch('/probe');

    expect(authorization).toBeNull();
  });

  it('refreshes once and retries after 401 auth.required', async () => {
    let refreshes = 0;
    server.use(
      http.post('*/auth/refresh', async ({ request }) => {
        refreshes += 1;
        expect(await request.json()).toEqual({ refreshToken: 'refresh-stale' });
        expect(request.headers.get('Authorization')).toBeNull();
        return HttpResponse.json(mockTokens('refreshed'));
      }),
      http.get('*/probe', ({ request }) =>
        request.headers.get('Authorization') === 'Bearer access-refreshed'
          ? HttpResponse.json({ ok: true })
          : unauthorized('auth.required'),
      ),
    );
    storeTokens(mockTokens('stale'));

    await expect(apiFetch('/probe')).resolves.toEqual({ ok: true });

    expect(refreshes).toBe(1);
    expect(storedTokens()?.refreshToken).toBe('refresh-refreshed');
  });

  it('refreshes before sending when the access token is about to expire', async () => {
    let authorization: string | null = null;
    server.use(
      http.get('*/probe', ({ request }) => {
        authorization = request.headers.get('Authorization');
        return HttpResponse.json({ ok: true });
      }),
    );
    storeTokens({ ...mockTokens('stale'), accessTokenExpiresAt: new Date(Date.now() + 5_000).toISOString() });

    await apiFetch('/probe');

    expect(authorization).toBe('Bearer access-refreshed');
  });

  it('signs out when the session was revoked', async () => {
    server.use(http.get('*/probe', () => unauthorized('auth.session_expired')));
    storeTokens(mockTokens('revoked'));

    const error = await failure(apiFetch('/probe'));

    expect(error.problem.code).toBe('auth.session_expired');
    expect(storedTokens()).toBeUndefined();
  });

  it('signs out when the refresh token is refused', async () => {
    server.use(
      http.post('*/auth/refresh', () => unauthorized('auth.required')),
      http.get('*/probe', () => unauthorized('auth.required')),
    );
    storeTokens(mockTokens('stale'));

    const error = await failure(apiFetch('/probe'));

    expect(error.status).toBe(401);
    expect(storedTokens()).toBeUndefined();
  });
});
