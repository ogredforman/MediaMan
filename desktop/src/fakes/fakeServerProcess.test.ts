import { describe, expect, it } from 'vitest';
import { FAKE_SERVER_URL, FakeServerProcess } from './fakeServerProcess.js';

describe('FakeServerProcess', () => {
  it('resolves with the mock API URL and records start and stop', async () => {
    const server = new FakeServerProcess();

    await expect(server.start()).resolves.toEqual({ baseUrl: FAKE_SERVER_URL });
    expect(server.running).toBe(true);

    await server.stop();
    expect(server).toMatchObject({ startCalls: 1, stopCalls: 1, running: false });
  });

  it('fails the next start only', async () => {
    const server = new FakeServerProcess('http://localhost:9999');
    server.failNextStart(new Error('boom'));

    await expect(server.start()).rejects.toThrow('boom');
    await expect(server.start()).resolves.toEqual({ baseUrl: 'http://localhost:9999' });
    expect(server.startCalls).toBe(2);
  });
});
