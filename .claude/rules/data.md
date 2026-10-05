---
paths:
  - "server/src/MediaMan.Data/**"
---

# Database rules

- Migrations in `server/src/MediaMan.Data/Migrations/` are append-only: add a new migration, never edit or delete an applied one.
- SQLite runs in WAL mode; don't change journal settings (SPEC §9 resilience).
- Entity shapes follow SPEC §6.1. A schema change that differs from the SPEC updates SPEC §6 in the same commit.
- Store only hashes of passwords, refresh tokens, invite tokens and PINs, never the raw values (SPEC §6.1, §9).
- All data lives in the single SQLite file, the per-install key file and the artwork cache folder; don't add other stores (SPEC §9 data portability).
- TOTP secrets and SOCKS5 credentials are encrypted at rest through `ISecretProtector` (ADR 0003); any new secret the server must read back uses it too.
