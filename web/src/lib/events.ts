import { getGetEventsUrl } from '../api/mediaman';
import type { EventEnvelope } from '../api/mediaman.schemas';
import { apiUrl, withAuth } from './http';

function isEnvelope(value: unknown): value is EventEnvelope {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.id === 'string' && typeof v.type === 'string' && typeof v.at === 'string' && 'data' in v;
}

function parseFrame(frame: string): EventEnvelope | undefined {
  const data: string[] = [];
  for (const line of frame.split('\n')) {
    if (line.startsWith('data:')) data.push(line.slice(line.startsWith('data: ') ? 6 : 5));
  }
  if (data.length === 0) return undefined; // A comment such as `: keep-alive`.
  try {
    const value: unknown = JSON.parse(data.join('\n'));
    return isEnvelope(value) ? value : undefined;
  } catch {
    return undefined;
  }
}

/**
 * An incremental Server-Sent Events parser: feed it text chunks as they arrive and it returns the complete
 * frames' envelopes, keeping a partial frame for the next chunk. Frames that aren't an `EventEnvelope` are skipped.
 */
export function createSseParser(): (chunk: string) => EventEnvelope[] {
  let buffer = '';
  return (chunk) => {
    // A trailing CR may be the first half of a CRLF split across chunks, so it waits for the next chunk.
    buffer = (buffer + chunk).replace(/\r\n|\r(?!$)/g, '\n');
    const frames = buffer.split('\n\n');
    buffer = frames.pop() ?? '';
    return frames.map(parseFrame).filter((e): e is EventEnvelope => e !== undefined);
  };
}

export interface EventStreamOptions {
  onEvent: (event: EventEnvelope) => void;
  /** Called on every (re)connect; events sent while disconnected are lost, so refetch what they would have changed. */
  onOpen?: () => void;
  signal: AbortSignal;
  /** Delay before reconnect attempt `attempt` (from 1). */
  retryDelay?: (attempt: number) => number;
}

const defaultRetryDelay = (attempt: number) => Math.min(1000 * 2 ** (attempt - 1), 30_000);

function wait(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener(
      'abort',
      () => {
        clearTimeout(timer);
        resolve();
      },
      { once: true },
    );
  });
}

/**
 * Follows `GET /events` until `signal` aborts, reconnecting with backoff when the stream drops. This streams with
 * `fetch` rather than the generated client (which reads whole bodies) or `EventSource` (which can't send the
 * bearer token).
 */
export async function connectEvents({
  onEvent,
  onOpen,
  signal,
  retryDelay = defaultRetryDelay,
}: EventStreamOptions): Promise<void> {
  // A function, not `signal.aborted` inline: the flag changes across awaits, which TypeScript's narrowing ignores.
  const stopped = () => signal.aborted;
  let attempt = 0;
  while (!stopped()) {
    try {
      const response = await fetch(apiUrl(getGetEventsUrl()), {
        headers: await withAuth({ Accept: 'text/event-stream' }),
        signal,
      });
      if (!response.ok || !response.body) {
        void response.body?.cancel().catch(() => undefined);
      } else {
        attempt = 0;
        onOpen?.();
        const parse = createSseParser();
        const decoder = new TextDecoder();
        const reader = response.body.getReader();
        // Not every fetch implementation ends a pending read on abort, so cancel the reader too.
        const cancel = () => void reader.cancel().catch(() => undefined);
        signal.addEventListener('abort', cancel, { once: true });
        try {
          for (;;) {
            const { done, value } = await reader.read();
            if (done) break;
            for (const event of parse(decoder.decode(value, { stream: true }))) onEvent(event);
          }
        } finally {
          signal.removeEventListener('abort', cancel);
        }
      }
    } catch {
      // Network error or abort; the loop condition decides whether to retry.
    }
    if (stopped()) return;
    attempt += 1;
    await wait(retryDelay(attempt), signal);
  }
}
