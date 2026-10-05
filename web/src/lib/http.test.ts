import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import { server } from '../mocks/node';
import { apiFetch, ApiError, setAccessTokenProvider } from './http';

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
  afterEach(() => {
    setAccessTokenProvider(() => undefined);
  });

  it('sends the bearer token from the provider', async () => {
    let authorization: string | null = null;
    server.use(
      http.get('*/probe', ({ request }) => {
        authorization = request.headers.get('Authorization');
        return HttpResponse.json({ ok: true });
      }),
    );
    setAccessTokenProvider(() => 'token-123');

    await apiFetch('/probe');

    expect(authorization).toBe('Bearer token-123');
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
});
