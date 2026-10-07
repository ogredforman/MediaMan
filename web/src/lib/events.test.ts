import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { EventEnvelope } from '../api/mediaman.schemas';
import { createEventStream } from '../mocks/handlers';
import { server } from '../mocks/node';
import { connectEvents, createSseParser } from './events';

const envelope = (id: string, data: unknown = { n: id }) => ({
  id,
  type: 'scan.progress',
  at: '2026-10-06T12:00:00Z',
  data,
});
const frame = (e: object) => `event: scan.progress\nid: 1\ndata: ${JSON.stringify(e)}\n\n`;

describe('createSseParser', () => {
  it('returns complete frames and keeps a partial frame for the next chunk', () => {
    const parse = createSseParser();
    const text = frame(envelope('1')) + frame(envelope('2'));
    const cut = text.length - 10;

    expect(parse(text.slice(0, cut))).toEqual([envelope('1')]);
    expect(parse(text.slice(cut))).toEqual([envelope('2')]);
  });

  it('skips comments, keep-alives, malformed JSON and data that is not an envelope', () => {
    const parse = createSseParser();
    const text = ': connected\n\n: keep-alive\n\ndata: {not json\n\ndata: {"id":"x"}\n\n' + frame(envelope('3'));

    expect(parse(text)).toEqual([envelope('3')]);
  });

  it('joins multi-line data and accepts CRLF line endings split across chunks', () => {
    const parse = createSseParser();
    const json = JSON.stringify(envelope('4'), null, 1).split('\n');
    const text = json.map((line) => `data: ${line}`).join('\r\n') + '\r\n\r\n';
    const cut = text.length - 3; // Between a CR and its LF.

    expect(parse(text.slice(0, cut))).toEqual([]);
    expect(parse(text.slice(cut))).toEqual([envelope('4')]);
  });
});

describe('connectEvents', () => {
  it('delivers events and reconnects after the stream fails', async () => {
    let failures = 1;
    const stream = createEventStream();
    server.use(
      http.get('*/events', () => {
        if (failures-- > 0) return HttpResponse.error();
        return undefined; // Fall through to the stream handler.
      }),
      stream.handler,
    );
    const controller = new AbortController();
    const received: EventEnvelope[] = [];
    let opens = 0;

    const done = connectEvents({
      signal: controller.signal,
      retryDelay: () => 0,
      onOpen: () => (opens += 1),
      onEvent: (e) => received.push(e),
    });
    await expect.poll(() => stream.connections).toBe(1);
    stream.send('scan.progress', { n: 1 });
    await expect.poll(() => received.map((e) => e.data)).toEqual([{ n: 1 }]);
    expect(opens).toBe(1);
    controller.abort();
    await done;
  });
});
