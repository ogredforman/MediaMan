import { describe, expect, it, vi } from 'vitest';
import { FakeServerProcess } from '../fakes/fakeServerProcess.js';
import { startLocalServer, stopServerOnQuit, type QuitEvent } from './localServer.js';
import type { ServerCommand } from './serverProcess.js';

const bundled: ServerCommand = { command: '/install/resources/server/MediaMan.Server', args: [] };

function quitEvent(): QuitEvent & { prevented: number } {
  const event = {
    prevented: 0,
    preventDefault() {
      event.prevented++;
    },
  };
  return event;
}

async function settled(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

describe('startLocalServer', () => {
  it('starts the server the command names and returns its URL', async () => {
    const fake = new FakeServerProcess('http://127.0.0.1:8420');
    const created: ServerCommand[] = [];

    const local = await startLocalServer(bundled, (command) => {
      created.push(command);
      return fake;
    });

    expect(created).toEqual([bundled]);
    expect(local).toEqual({ server: fake, baseUrl: 'http://127.0.0.1:8420' });
    expect(fake.running).toBe(true);
  });

  it('starts nothing without a command', async () => {
    const create = vi.fn(() => new FakeServerProcess());

    expect(await startLocalServer(undefined, create)).toBeUndefined();
    expect(create).not.toHaveBeenCalled();
  });

  it("rejects with the server's message when it can't start", async () => {
    const fake = new FakeServerProcess();
    fake.failNextStart(new Error('The MediaMan server stopped before it was ready (exit code 1). Restart MediaMan.'));

    await expect(startLocalServer(bundled, () => fake)).rejects.toThrow('Restart MediaMan.');
    expect(fake.running).toBe(false);
  });
});

describe('stopServerOnQuit', () => {
  it('holds the first quit until the server stopped, then quits once more and lets that through', async () => {
    const fake = new FakeServerProcess();
    await fake.start();
    const quit = vi.fn();
    const onQuit = stopServerOnQuit(() => fake, quit);

    const first = quitEvent();
    onQuit(first);
    expect(first.prevented).toBe(1);
    await settled();
    expect(fake.stopCalls).toBe(1);
    expect(fake.running).toBe(false);
    expect(quit).toHaveBeenCalledTimes(1);

    const second = quitEvent();
    onQuit(second);
    expect(second.prevented).toBe(0);
    expect(fake.stopCalls).toBe(1);
  });

  it('quits even when stopping fails, and says so', async () => {
    const failing = new FakeServerProcess();
    failing.stop = () => Promise.reject(new Error('access denied'));
    const quit = vi.fn();
    const log = vi.fn();

    stopServerOnQuit(() => failing, quit, log)(quitEvent());
    await settled();

    expect(log).toHaveBeenCalledWith('Could not stop the MediaMan server: access denied');
    expect(quit).toHaveBeenCalledTimes(1);
  });

  it('lets every quit through without a server', () => {
    const quit = vi.fn();
    const event = quitEvent();

    stopServerOnQuit(() => undefined, quit)(event);

    expect(event.prevented).toBe(0);
    expect(quit).not.toHaveBeenCalled();
  });
});
