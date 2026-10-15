# Architecture Decision Records

One file per significant decision: `NNNN-short-title.md`, numbered in order. Copy `0000-template.md`.

Write an ADR when a choice is hard to reverse, adds a significant dependency, or changes something
the SPEC states. If the decision changes the SPEC, update `docs/SPEC.md` in the same commit.

ADRs are never deleted. A reversed decision gets a new ADR that supersedes the old one, and the
old one's status is changed to `Superseded by NNNN`.

| # | Title | Status |
|---|---|---|
| 0001 | [Server test stack: xUnit v3 on MTP, AwesomeAssertions, Verify deferred](0001-server-test-stack.md) | Accepted |
| 0002 | [Entity ids are UUIDv7; Playable and Artwork owners use explicit FKs](0002-entity-ids-and-playable.md) | Accepted |
| 0003 | [SQLite storage conventions and secrets at rest](0003-sqlite-storage-conventions.md) | Accepted |
| 0004 | [Web stack versions and API codegen with Orval](0004-web-stack-and-api-codegen.md) | Accepted |
| 0006 | [TMDB ids carry the title kind](0006-tmdb-ids-carry-kind.md) | Accepted |
| 0007 | [Artwork cache, image fetching and decoding](0007-artwork-cache-and-image-decoding.md) | Accepted |
| 0008 | [Per-install key, derived subkeys and Argon2id password hashing](0008-install-key-and-password-hashing.md) | Accepted |
| 0009 | [The server serves the web UI under /app/; the API stays at the root](0009-web-ui-served-under-app.md) | Accepted |
| 0010 | [Windows installer: electron-builder NSIS, pinned official MPV, source archives with each release](0010-windows-installer.md) | Accepted |
| 0011 | [Play inside the app window: MPV embedded with --wid under a React overlay window](0011-in-window-playback.md) | Accepted |
