// FakeServerProcess (SPEC §10.4): resolves with a fixed URL pointing at the MSW mock API, never spawns anything.
import type { IServerProcess } from '../main/serverProcess.js';

/** The mock dev server (`npm run dev:mock -w web`), where MSW answers the API. */
export const FAKE_SERVER_URL = 'http://localhost:5173';

export class FakeServerProcess implements IServerProcess {
  startCalls = 0;
  stopCalls = 0;
  running = false;
  private nextFailure: Error | undefined;

  constructor(readonly baseUrl: string = FAKE_SERVER_URL) {}

  /** Makes the next `start()` reject with `error`. */
  failNextStart(error: Error = new Error('The MediaMan server stopped before it was ready (fake).')): void {
    this.nextFailure = error;
  }

  start(): Promise<{ baseUrl: string }> {
    this.startCalls++;
    const failure = this.nextFailure;
    if (failure) {
      this.nextFailure = undefined;
      return Promise.reject(failure);
    }
    this.running = true;
    return Promise.resolve({ baseUrl: this.baseUrl });
  }

  stop(): Promise<void> {
    this.stopCalls++;
    this.running = false;
    return Promise.resolve();
  }
}
