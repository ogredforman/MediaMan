# 0008. Per-install key, derived subkeys and Argon2id password hashing

- **Status:** Accepted
- **Date:** 2026-10-07
- **PLAN task:** 2.3

## Context

SPEC §9 requires Argon2id password hashes, tokens signed with a per-install key, and secrets encrypted at rest
with that key (ADR 0003). .NET has no built-in Argon2. One key serving several algorithms (AES-GCM now, HMAC token
signatures and signed artwork URLs in PLAN 2.3b) must not let one use's output be valid for another.

## Options considered

Argon2id:

1. **Konscious.Security.Cryptography.Argon2** (MIT): managed C#, no native binaries, so it runs unchanged on every
   platform (SPEC §9). Slower than native, which doesn't matter at login rates.
2. **libsodium via NSec or Sodium.Core**: fast, but ships a native library per platform.
3. **Isopoh.Cryptography.Argon2**: managed too, less widely used.

Key use:

1. **Use the raw key everywhere.** Simple, but ties the algorithms together.
2. **HKDF-SHA256 subkeys per purpose** (`MediaMan/<purpose>` as the info). Built into .NET, one key file still.

## Decision

Konscious Argon2id at OWASP's minimum (19 MiB, 2 iterations, 1 lane), stored as PHC strings
(`$argon2id$v=19$m=…,t=…,p=…$salt$hash`) so the parameters can be raised later and old hashes still verify.
The per-install key is 32 random bytes in `<DataDirectory>/mediaman.key`, created on first run (temp file, then
an atomic move; owner-only on Unix) and never replaced if damaged, since a new key orphans every stored secret.
Every use takes its own HKDF subkey: `secrets-at-rest` for `AesGcmSecretProtector` now, others for tokens in 2.3b.

The Superadmin is protected in the data layer: an EF `SaveChanges` interceptor on every context refuses to delete,
demote, disable or change the role of the stored Superadmin row (SPEC §5, §7.11).

## Consequences

- Backups need the key file with the database (ADR 0003); losing it means re-entering secrets and signing in again.
- Bulk `ExecuteUpdate`/`ExecuteDelete` skip interceptors and must not be used on `User`.
- Ownership transfer (later stage) needs a deliberate, tested path through the guard.
