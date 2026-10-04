# 0002. Entity ids are UUIDv7; Playable and Artwork owners use explicit FKs

- **Status:** Accepted
- **Date:** 2026-10-04
- **PLAN task:** 0.2

## Context

SPEC §6.1 lists entities without fixing an id type. It also describes Playable as polymorphic (a movie
Title or an Episode) and Artwork as having an `ownerRef` (Title, Season or Episode). Both need a
concrete shape before the Phase 0 schema freezes in PLAN 0.3.

## Options considered

1. **Ids: `long` autoincrement.** Small and fast, but an id exists only after insert. Ids are guessable and differ across installs.
2. **Ids: `Guid` v7.** Can be created before saving (useful for SSE events and request bodies) and still sorts by time, so B-tree inserts stay sequential. Costs 16 bytes per key.
3. **Playable as a `(Kind, Id)` value** stored as two columns on FileLink and WatchState. Avoids a join, but the database can't check the reference.
4. **Playable as a table** with nullable `TitleId` / `EpisodeId` FKs, exactly one set. FileLink and WatchState reference `Playable.Id`. The database enforces the reference, at the cost of one join.

## Decision

Every entity id is a `Guid` v7, created only through `Ids.New(IClock)` so ids stay deterministic in
tests. Playable is a real table (option 4). Artwork uses the same pattern, with nullable `TitleId`,
`SeasonId` and `EpisodeId` FKs where exactly one is set. A season poster or an episode still is an
Artwork row of that type, not a column.

## Consequences

- 0.3 must add a check constraint for "exactly one FK set, matching Kind" on Playable, and "exactly one owner FK" on Artwork.
- Creating a movie Title or an Episode also creates its Playable row (use `Playable.ForMovie` / `ForEpisode`).
- Queries from WatchState or FileLink to a title need one extra join through Playable.
- Download references its target as `TargetTitleId` (a nullable FK to Title, per §6.2), not a free-form ref. DownloadRequest keeps an `ExternalId` because its title may not be in the library yet.
