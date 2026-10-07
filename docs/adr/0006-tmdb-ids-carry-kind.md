# 0006. TMDB ids carry the title kind

- **Status:** Accepted
- **Date:** 2026-10-07
- **PLAN task:** 2.1

## Context

TMDB numbers movies and TV shows in separate id spaces: movie 1396 and show 1396 are different titles.
`ExternalId` was written `tmdb:603` with no kind. `IMetadataProvider.GetTitleAsync(ExternalId)` couldn't tell
which endpoint to call, and the importer finds an existing Title by `ExternalIds.Tmdb` alone, so a movie and a
show with the same number would merge.

## Options considered

1. **Kind in the value:** `tmdb:movie/603` and `tmdb:tv/1396`, the same form as TMDB's own URLs. No interface
   or schema change, and lookups by `ExternalIds.Tmdb` stay unique.
2. **Kind as a parameter:** `GetTitleAsync(id, kind, ct)` and lookups on Tmdb plus Kind. Changes a SPEC §10.3
   interface and every caller and fake, and the stored id still isn't unique on its own.

## Decision

Option 1. A TMDB `ExternalId` value is `movie/<n>` or `tv/<n>` (`MediaMan.Metadata.Tmdb.TmdbIds`), and
`Title.ExternalIds.Tmdb` stores that value. The contract's `ExternalIds.tmdb` and `MatchCandidate.titleRef`
carry it as is. Fixtures and fakes use the same form. There was no released data to migrate.

The TMDB read-access token ships in source (`TmdbOptions.ShippedAccessToken`), as Kodi and Jellyfin ship theirs:
a key in a distributed build can always be extracted, and TMDB rate-limits by IP, not by key. It can be
overridden with `MediaMan:Metadata:Tmdb:AccessToken`, and it's sent only in the `Authorization` header and never
logged.

## Consequences

- Other providers' ids are unchanged (`imdb:tt0133093`, `tvdb:81189`).
- Catalog search (PLAN 4.8) and Fix match (PLAN 2.9) pass these ids through unchanged.
