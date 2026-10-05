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
