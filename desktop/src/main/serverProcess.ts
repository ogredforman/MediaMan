// The local MediaMan server the desktop app spawns in Local mode (SPEC §10.1, §10.3).
// Declaration only; the implementation and FakeServerProcess arrive with the Local mode workstream.

export interface IServerProcess {
  start(): Promise<{ baseUrl: string }>;
  stop(): Promise<void>;
}
