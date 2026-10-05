import type { Problem } from '../api/mediaman.schemas';

const baseUrl: string = import.meta.env.VITE_API_BASE_URL ?? '';

let getAccessToken: () => string | undefined = () => undefined;

/** Sign-in (Stage 1) supplies the bearer access token through this hook. */
export function setAccessTokenProvider(provider: () => string | undefined): void {
  getAccessToken = provider;
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

/** The fetcher every generated client function calls (Orval mutator). */
export async function apiFetch<T>(url: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  const token = getAccessToken();
  if (token !== undefined) headers.set('Authorization', `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(`${baseUrl || window.location.origin}${url}`, { ...init, headers });
  } catch {
    throw new ApiError({
      title: "Can't reach the MediaMan server.",
      status: 503,
      code: 'network.unreachable',
      action: 'Check that the server is running and reachable, then try again.',
    });
  }

  const text = await response.text();
  const body = parseJson(text);

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
