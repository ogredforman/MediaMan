# 0003. SQLite storage conventions and secrets at rest

- **Status:** Accepted
- **Date:** 2026-10-04
- **PLAN task:** 0.3

## Context

PLAN 0.3 freezes the Phase 0 schema for SPEC §6.1 in `MediaMan.Data`. SQLite has few native types,
so timestamps, enums and nested value objects need a fixed storage shape before the first migration.
`User.TotpSecret` and the `VpnConfig` SOCKS5 credentials are secrets that must be readable by the server
(unlike passwords, which are hashed), so they can't be hashed and shouldn't be stored as plain text.

## Options considered

1. **Timestamps as EF's default TEXT.** Readable, but SQLite can't compare or order `DateTimeOffset` text correctly across offsets.
2. **Timestamps as UTC unix milliseconds (`INTEGER`).** Sortable and indexable. Loses sub-millisecond precision, which nothing needs.
3. **Nested values (ProbeResult, ParsedRelease, QualityFilter, ReleaseResult, match candidates, indexer settings and health) as child tables.** Queryable, but most of them are only ever loaded whole.
4. **Nested values as JSON text columns.** One column each, round-trips whole records. A value can be promoted to columns later if a query needs it.
5. **Secrets as plain text**, relying on file permissions. Anyone holding a copied database or backup can read them.
6. **Secrets encrypted with AES-256-GCM under the per-install key.** A copied database alone reveals nothing. Backups must include the key to be restorable.

## Decision

- Every entity id is a `Guid` (ADR 0002). Enums are stored as their names. `DateTimeOffset` is stored as UTC unix milliseconds and `TimeSpan` as ticks.
- Nested value objects are JSON text columns (option 4), serialized with string enums and `ExternalId` as `provider:value`. `TitleExternalIds` are plain columns with an index each, since matching looks titles up by provider id.
- `ReleaseResult` and `QualityFilter` are not tables. They live as JSON inside `DownloadRequest` and `SavedQualityProfile`.
- `VpnConfig` is a single row with a shadow text key fixed to `'vpn'` by a check constraint.
- The database enforces at most one Superadmin with a filtered unique index on `IsSuperadmin = 1`.
- `TotpSecret`, `SocksUsername` and `SocksPassword` are encrypted at rest (option 6) by a value converter that calls `ISecretProtector`. `MediaManDbContext` requires a protector, so there is no code path that stores them in plain text. The stored form is `v1:` + base64(nonce | tag | ciphertext). The EF model is cached per protector instance, so one protector is registered per process.
- Connections set `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON` and `busy_timeout=5000` on every open.

## Consequences

- PLAN 2.3 creates the per-install key (`<DataDirectory>/mediaman.key`, generated on first run) and builds the `AesGcmSecretProtector` from its `secrets-at-rest` HKDF subkey (ADR 0008).
- The server applies pending migrations on startup (`DatabaseMigrator`), before any other hosted service runs.
- Backup and restore copy the key file along with the database and artwork cache. Without the key, encrypted secrets are unreadable and must be re-entered.
- Encrypted columns can't be queried by value; nothing needs to.
- JSON column shapes are part of the schema: renaming a property in those records needs a data migration.
