# Code map

A short orientation to the code, so a task can start without reading whole packages. Read this
first, then open only the files you'll change or call. Keep it current: when a task adds a
package, a public entry point, or a test fixture, update the matching line here in the same commit.
Signatures here are summaries; `grep -n "public " <file>` (C#) or `grep -n "export " <file>` (TS) gives the exact ones.

<!-- Keep this a map, not documentation: one line per thing. If a line needs a paragraph, the
     detail belongs in the code's own doc comments or an ADR. -->

## Reading the docs cheaply

- SPEC sections by heading: `awk '/^### 7.1/,/^### 7.2/' docs/SPEC.md` (or `/^## 7\./,/^## 8\./` for a whole section).
  §5 principles, §6.1 entities, §7 core flows, §9 non-functional, §10.2 layout, §10.3 interfaces, §10.4 fakes, §11 UI, §12 testing.
- PLAN: `grep -n '\[ \]' docs/PLAN.md | head` for the next tasks; read only the current stage's section.
- ADRs: 0001 server test stack (xUnit v3, MTP, AwesomeAssertions) · 0002 UUIDv7 ids, Playable/Artwork owner FKs ·
  0003 SQLite conventions and secrets at rest · 0004 web stack and Orval codegen · 0005 desktop build with plain tsc.
- Area rules: `.claude/rules/<area>.md` (contracts, data, desktop, library, server, server-tests, web).

## Wiring

- **Server:** `server/src/MediaMan.Server/Program.cs` → `AddMediaManServer(config)` (binds `MediaManOptions` from the
  `MediaMan` section, `IClock`→`SystemClock`, `EventBroadcaster`, `MediaManDbContext` on `<DataDirectory>/mediaman.db`,
  ProblemDetails, `ISecretProtector`→`UnavailableSecretProtector` until PLAN 2.3, `DatabaseMigrator` hosted service) → `UseMediaManErrors()` → `MapMediaManEndpoints()` (`GET /health`, `GET /events` SSE). All in `Hosting/ServerSetup.cs`.
- **Dev configuration** (SPEC §8 setup [P], Debug builds only): `ASPNETCORE_ENVIRONMENT=Development` + `MediaMan:Dev:MediaPath`
  (user-secrets or `MediaMan__Dev__MediaPath`) → `Dev/DevConfiguration.AddMediaManDev` registers fakes per `MediaMan:Dev:Providers:*`
  (`Fake|Real`) and `DevLibrarySeeder` adds the folder as an External library once. The server csproj references `MediaMan.Fakes` in Debug only.
- **Contract:** `contracts/openapi.yaml` (OpenAPI 3.1, `x-permission` per operation) → `npm run generate -w web` (Orval) →
  `web/src/api/*` (client, schemas, MSW handlers, faker). Never edit generated files.
- **Web:** `web/src/main.tsx` → MSW in mock mode (`npm run dev:mock -w web`) → TanStack Router (`router.ts`, file routes in `routes/`) + Query → `AppShell`.
- **Desktop:** `desktop/src/main/index.ts` → one `BrowserWindow` loading `MEDIAMAN_UI_URL` (default `http://localhost:5173`), sandboxed, preload `src/preload/index.cts`.
- **Root scripts:** `package.json` runs server (dotnet), then `web` and `desktop` npm workspaces for `build`, `test`, `lint`.
- **CI:** `.github/workflows/ci.yml` runs `npm ci`, build, a generated-code drift check, lint and test on ubuntu-latest and windows-latest.

## Packages / modules

Server projects live in `server/src/`; most are empty shells until their stage.

- **MediaMan.Core**: domain entities (`Domain/*.cs`, SPEC §6.1), every §10.3 interface (`Modules/Metadata.cs`, `MediaTools.cs`,
  `Indexers.cs`, `Torrents.cs`), `IClock`/`SystemClock`, `ExternalId`, `InfoHash`, `Ids` (UUIDv7), `Permissions` constants.
- **MediaMan.Data**: `MediaManDbContext`, entity config in `Model/*Model.cs`, `MediaManDb.Configure/CreateOptions` (SQLite, WAL via
  `SqlitePragmaInterceptor`), `ISecretProtector`/`AesGcmSecretProtector`, migrations in `Migrations/`.
- **MediaMan.Server**: host and endpoints. `Endpoints/HealthEndpoints.cs`, `RequiresPermission(...)` in `Endpoints/PermissionMetadata.cs`,
  `Events/EventBroadcaster` (`Publish`, `Subscribe`) + `EventEnvelope`, `Hosting/Problems.cs` (error model).
- **MediaMan.Parsing**: `ReleaseParser.Parse(fileName, parentDirs)` (pure; `parentDirs` outermost first) → `ParsedRelease`, including quality tags with canonical values (`_sources`, `_codecs`, `_hdrFormats`, `_audio` tables) and anime absolute numbers.
- **MediaMan.Playback**: `Probing/FfprobeMediaProbe` (real `IMediaProbe`, runs ffprobe as a process; `FfprobeOptions` path + timeout) and
  `Probing/FfprobeOutputParser.Parse(json)` (pure; HDR names match `ReleaseParser`). Dev wiring reads `MediaMan:Tools:FfprobePath`.
- **MediaMan.Library**: `Scanning/LibraryScanner.ScanAsync(libraryId, ct)` → `ScanResult` (enumerate roots, probe + parse new/changed files as `Pending`, unreadable → `NeedsAttention`, missing → `Missing` with `MissingSince`, purge after 30 days); `Scanning/ScanRules` (pure: video extensions, samples, extras folders). Not yet registered in DI (PLAN 1.9).
  `Matching/LibraryMatcher.MatchPendingAsync(libraryId, ct)` → `MatchRunResult` (`MatchDecision`s for the importer at ≥ `MatcherOptions.Threshold` with a clear lead; otherwise MatchReview + `NeedsAttention` with `no-match`/`low-confidence`/`ambiguous`; provider errors retried then left `Pending`); `Matching/MatchScorer` and `TitleSimilarity` (pure scoring); `Matching/ProviderSession` (internal: per-run provider cache, retry/backoff, trip).
  `Importing/MetadataImporter.ImportAsync(decisions, ct)` → `ImportResult` (Title by provider id, Seasons holding linked files with full episode lists, one Artwork ref per type per owner, Playables for linked files only, FileLinks with `Part` from 1, file → `Matched`; absolute numbers resolved over seasons > 0; provider errors → stays `Pending`; unknown episode → `NeedsAttention` `episode-not-found`).
- **MediaMan.Auth, .Metadata, .Indexers, .Torrents**: empty, filled from Stage 1 on.

## Frontend

- `web/src/api/`: Orval output from the contract (generated, do not edit). `web/src/lib/http.ts`: `apiFetch` mutator, `ApiError`, `setAccessTokenProvider`.
- `web/src/mocks/`: `handlers.ts` (generated handlers + `mockHealth`), `browser.ts` (dev mock mode), `node.ts` (Vitest).
- `web/src/routes/`: file routes (`__root.tsx`, `index.tsx`); `routeTree.gen.ts` is generated.
- `web/src/components/AppShell.tsx`: themed shell. `web/src/theme/theme.css`: SPEC §11 tokens for Tailwind.
- `desktop/src/main/`: `window.ts` (`resolveUiUrl`, `mainWindowOptions`, navigation guards), `player.ts` (`IPlayer`), `serverProcess.ts` (`IServerProcess`).
- `desktop/mpv/`: `mpv.conf` and `osc.lua` placeholders.

## Tests and fixtures

- One test project per module in `server/tests/MediaMan.<Module>.Tests`; every one references `server/tests/MediaMan.Fakes`:
  `FakeClock` (`Advance`), `FakeMetadataProvider` (fixtures, `FailNext`, `FailNextSeason`, `SearchQueries`/`TitleRequests`/`SeasonRequests`),
  `FakeArtworkProvider` (`Add`), `FakeMediaProbe` (`Map`/`Fail` file-name globs, presets `Hd1080`/`Uhd2160Hdr10`/`DolbyVision`),
  `FakeFileWatcher` (`Raise`). Fake tests are in `MediaMan.Core.Tests/Fakes/`.
- `server/tests/fixtures/metadata/tmdb-<id>.json`: 33 provider titles (remakes, same-name shows, specials, anime absolute numbers), copied to `fixtures/metadata` in test output.
- `server/tests/corpus/release-names.json`: parser corpus (`path` with `/`, partial `expected` fields, `baseline` accuracy), copied to `corpus/` in `MediaMan.Parsing.Tests` output; `ReleaseParserCorpusTests` enforces the baseline and lists failing cases.
- `server/tests/fixtures/ffprobe/*.json`: saved ffprobe outputs (1080p MKV, HDR10, Dolby Vision, HLG, cover art), copied to `fixtures/ffprobe` in
  `MediaMan.Playback.Tests` output. `FfprobeMediaProbeIntegrationTests` generates samples with ffmpeg and skips when ffmpeg/ffprobe are absent.
- `MediaMan.Fakes/TempDatabase.cs` (shared by every test project): per-test SQLite file (`CreateMigratedAsync`, `CreateEmpty`, `CreateContext`, `OpenRawConnection`).
- `MediaMan.Server.Tests/MediaManFactory.cs`: in-process host on a per-test temp `DataDirectory`; optional environment and settings, `KeepDataDirectory` for restarts.
- `MediaMan.Server.Tests/Conformance/ContractDocument.cs`: loads the contract; `AssertConformsAsync(response, ct)` validates a response.
  `ContractCoverageTests` checks routes against contract operations.
- Web: Vitest + Testing Library with MSW (`src/test/setup.ts`); Playwright smoke in `web/e2e/` against the mock dev server.
- Desktop: Vitest next to the code (`*.test.ts`).
- Quick runs: `dotnet test --project server/tests/MediaMan.<Module>.Tests`, `npm run test:unit -w web`, `npm run test:e2e -w web`, `npm test -w desktop`.
