# 0008. Per-install key, derived subkeys and Argon2id password hashing

- **Status:** Accepted
- **Date:** 2026-10-07
- **PLAN task:** 2.3, 2.3b

## Context

SPEC §9 requires Argon2id password hashes, tokens signed with a per-install key, and secrets encrypted at rest
with that key (ADR 0003). .NET has no built-in Argon2. One key serving several algorithms (AES-GCM now, HMAC token
signatures in PLAN 2.3b and signed artwork URLs in 2.3c) must not let one use's output be valid for another.

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
Every use takes its own HKDF subkey: `secrets-at-rest` for `AesGcmSecretProtector`, `access-tokens` for access tokens,
and its own subkey for signed artwork URLs in 2.3d.

The Superadmin is protected in the data layer: an EF `SaveChanges` interceptor on every context refuses to delete,
demote, disable or change the role of the stored Superadmin row (SPEC §5, §7.11).

## Sign-in tokens (PLAN 2.3b)

- **Access tokens:** JWTs, HS256 on the `access-tokens` subkey, issuer and audience `mediaman`, carrying `sub` (user)
  and `sid` (session), valid 15 minutes. Created and validated with **Microsoft.IdentityModel.JsonWebTokens** (MIT),
  the library ASP.NET Core's JwtBearer handler is built on, rather than hand-rolled signing; JwtBearer validates them
  with the same `TokenValidationParameters` in 2.3c. Lifetimes are checked against `IClock`, not the library's clock.
- **Refresh tokens:** opaque `<session id>.<32 random bytes, base64url>`, valid 30 days and sliding: each refresh
  replaces the token and restarts the 30 days. A `Session` row stores only its SHA-256 (the token is random, so a slow
  hash adds nothing). Rotation is a conditional update on the current hash, so of two concurrent refreshes one wins.
- **Reuse detection without history:** a token naming a live session but not matching its hash is a rotated one being
  replayed (or a guess, which needs the session id from the owner's own tokens), and revokes the session. Clients must
  therefore never refresh twice with one token, even concurrently (the web client single-flights it, PLAN 2.5).
- **Login failures** don't distinguish an unknown user, a wrong password or a disabled account; an unknown user is
  verified against a dummy Argon2 hash so timing doesn't either.

## Permission enforcement (PLAN 2.3c)

- **Bearer validation:** **Microsoft.AspNetCore.Authentication.JwtBearer** (MIT, part of ASP.NET Core) reads the
  `Authorization` header and validates the token with `AccessTokens.ValidationParameters`, so its lifetime check reads
  `IClock` too. No authority or metadata address is set, so it never fetches anything. Claims keep their JWT names.
- **One enforcement point:** a middleware after authentication reads each route's `PermissionMetadata` (the contract's
  `x-permission`). `none` passes; a route declaring nothing is refused. Otherwise each request loads its session and
  user: a revoked session or a disabled user is `401 auth.session_expired` even while the token is unexpired, a missing
  or invalid token is `401 auth.required`, and a missing permission is `403 auth.forbidden`. That's two small SQLite
  reads per request, cheap next to what any endpoint does, and it makes sign-out and disabling take effect at once.
- **Dev auto sign-in:** the dev configuration (Debug build, Development, `Dev:MediaPath` set) always has a Superadmin,
  the stored one or a seeded `dev` account with a random password. With `MediaMan:Dev:AutoSignIn`, a request with no
  `Authorization` header at all acts as it, without a session; an invalid token is still refused. Release builds don't
  contain this code.

## Consequences

- Backups need the key file with the database (ADR 0003); losing it means re-entering secrets and signing in again.
- Bulk `ExecuteUpdate`/`ExecuteDelete` skip interceptors and must not be used on `User`.
- Ownership transfer (later stage) needs a deliberate, tested path through the guard.
