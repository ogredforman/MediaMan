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
  ProblemDetails, JSON omits nulls, `LibraryQueries` (scoped), the scan pipeline (`LibraryScanner`/`LibraryMatcher`/`MetadataImporter` scoped via factories, `ScanCoordinator`, `ScanWorker` hosted service), `ISecretProtector`→`UnavailableSecretProtector` until PLAN 2.3, `DatabaseMigrator` hosted service) → `UseMediaManErrors()` → `MapMediaManEndpoints()` (`GET /health`, `GET /events` SSE, library browse and scanning). All in `Hosting/ServerSetup.cs`.
- **Dev configuration** (SPEC §8 setup [P], Debug builds only): `ASPNETCORE_ENVIRONMENT=Development` + `MediaMan:Dev:MediaPath`
  (user-secrets or `MediaMan__Dev__MediaPath`) → `Dev/DevConfiguration.AddMediaManDev` registers fakes per `MediaMan:Dev:Providers:*`
  (`Fake|Real`) and `DevLibrarySeeder` adds the folder as an External library once. Dev-only play (SPEC §13): `Dev/DevPlayEndpoints` (`POST /dev/playables/{playableId}/play`, `PickVersion`, `x-dev-only` in the contract) is mapped only when the dev configuration registers `IDevPlayer` (`MpvDevPlayer` via `MediaMan:Tools:MpvPath`, or `FakeDevPlayer` with `Providers:Player=Fake`). The server csproj references `MediaMan.Fakes` in Debug only.
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
  `Events/EventBroadcaster` (`Publish`, `Subscribe`) + `EventEnvelope`, `Hosting/Problems.cs` (error model; `Problems.Result(status, code, title, detail, action)` for specific codes).
  `LibraryApi/`: `LibraryEndpoints` (`GET /libraries`, `POST /libraries/{libraryId}/scan`, `GET /libraries/{libraryId}/attention`, `/titles`, `/titles/{titleId}`), `LibraryQueries` (read-only EF queries → DTOs in `LibraryDtos.cs`),
  `Paging.TryRead/Page` (opaque offset cursor, `pagination.invalid`), `Wire.Name/TryParse` (domain enums ↔ contract camelCase strings).
  `Scanning/`: `ScanCoordinator` (singleton: in-memory scan state per library, `Request` queues once, publishes `scan.progress`, `IsAvailable` needs `IMetadataProvider`+`IMediaProbe`) and `ScanWorker` (runs scan → match → import one library at a time, a DI scope per phase).
- **MediaMan.Parsing**: `ReleaseParser.Parse(fileName, parentDirs)` (pure; `parentDirs` outermost first) → `ParsedRelease`, including quality tags with canonical values (`_sources`, `_codecs`, `_hdrFormats`, `_audio` tables) and anime absolute numbers.
- **MediaMan.Playback**: `Probing/FfprobeMediaProbe` (real `IMediaProbe`, runs ffprobe as a process; `FfprobeOptions` path + timeout) and
  `Probing/FfprobeOutputParser.Parse(json)` (pure; HDR names match `ReleaseParser`). Dev wiring reads `MediaMan:Tools:FfprobePath`.
- **MediaMan.Library**: `Scanning/LibraryScanner.ScanAsync(libraryId, ct)` → `ScanResult` (enumerate roots, probe + parse new/changed files as `Pending`, unreadable → `NeedsAttention`, missing → `Missing` with `MissingSince`, purge after 30 days); `Scanning/ScanRules` (pure: video extensions, samples, extras folders).
  `Matching/LibraryMatcher.MatchPendingAsync(libraryId, ct)` → `MatchRunResult` (`MatchDecision`s for the importer at ≥ `MatcherOptions.Threshold` with a clear lead; otherwise MatchReview + `NeedsAttention` with `no-match`/`low-confidence`/`ambiguous`; provider errors retried then left `Pending`); `Matching/MatchScorer` and `TitleSimilarity` (pure scoring); `Matching/ProviderSession` (internal: per-run provider cache, retry/backoff, trip).
  `Importing/MetadataImporter.ImportAsync(decisions, ct)` → `ImportResult` (Title by provider id, Seasons holding linked files with full episode lists, one Artwork ref per type per owner, Playables for linked files only, FileLinks with `Part` from 1, file → `Matched`; absolute numbers resolved over seasons > 0; provider errors → stays `Pending`; unknown episode → `NeedsAttention` `episode-not-found`).
- **MediaMan.Auth, .Metadata, .Indexers, .Torrents**: empty, filled from Stage 1 on.

## Frontend

- `web/src/api/`: Orval output from the contract (generated, do not edit). `web/src/lib/http.ts`: `apiFetch` mutator, `ApiError`, `setAccessTokenProvider`, `apiUrl`, `withAuth`.
- `web/src/lib/events.ts`: `createSseParser` (pure, chunked SSE → `EventEnvelope`s) and `connectEvents({ onEvent, onOpen, signal })` (streams `GET /events` with fetch, reconnects with backoff).
  `web/src/lib/useLiveScanProgress.ts`: root-layout hook applying `scan.progress` to the libraries cache (`applyLibraryUpdate`); a finished scan invalidates `/titles`.
- `web/src/mocks/`: `handlers.ts` (deterministic `mockHealth`, `mockLibrary`, `mockTitles`, `mockTitleDetails` (a 404 for unknown ids), `createEventStream()` SSE handler with `send`, then generated handlers), `browser.ts` (dev mock mode; a Scan click plays a fake scan over SSE), `node.ts` (Vitest).
- `web/src/routes/`: file routes (`__root.tsx`, `index.tsx`, `movies.tsx`, `shows.tsx`, `titles.$titleId.tsx`); `routeTree.gen.ts` is generated.
- `web/src/components/AppShell.tsx`: themed shell (nav, `ScanStatus`, server status). `ScanStatus.tsx`: per-library scan state + `ScanLibraryButton`. `PosterGrid.tsx`: paged Movies/Shows grid (`useInfiniteQuery` over `listTitles`), cards link to the detail page. `TitleDetail.tsx`: detail page (tinted hero, versions, season tabs, episode list) over `useGetTitle`, with a prototype `PlayButton` over `useDevPlay`. `web/src/lib/mediaFormat.ts`: `formatDuration`, `formatSize`, `resolutionLabel`, `versionTags`. `web/src/theme/theme.css`: SPEC §11 tokens for Tailwind.
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
- `MediaMan.Server.Tests/MediaManFactory.cs`: in-process host on a per-test temp `DataDirectory`; optional environment and settings, `ConfigureServices` (e.g. inject fakes), `KeepDataDirectory` for restarts.
- `MediaMan.Server.Tests/Conformance/ContractDocument.cs`: loads the contract; `AssertConformsAsync(response, ct)` validates a response.
  `ContractCoverageTests` checks routes against contract operations. `DevPlayTests` covers the dev-only play endpoint (Debug only), and `ContractOperation.DevOnly` marks `x-dev-only` operations. `LibraryApiTests` seeds a browse fixture (movies with versions, a multi-episode show). `ScanApiTests` scans a temp placeholder folder end to end on the fakes and follows SSE.
- Web: Vitest + Testing Library with MSW (`src/test/setup.ts`; `src/test/renderApp.tsx` renders the whole app at a path); Playwright smoke in `web/e2e/` against the mock dev server; `web/e2e/server/` (`playwright.server.config.ts`) runs browse → detail → Play against the Debug server on fakes, started by `e2e/server/start-server.mjs` on fresh placeholder media in `web/.e2e-server/`, behind Vite proxying `/api/*` (`MEDIAMAN_SERVER_URL` + `VITE_API_BASE_URL=/api`).
- Desktop: Vitest next to the code (`*.test.ts`).
- Quick runs: `dotnet test --project server/tests/MediaMan.<Module>.Tests`, `npm run test:unit -w web`, `npm run test:e2e -w web` (both Playwright configs), `npm test -w desktop`.
