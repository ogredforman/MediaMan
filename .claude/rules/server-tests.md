---
paths:
  - "server/tests/**"
---

# Server test rules

- Use the shared fakes in `MediaMan.Fakes`; don't write ad-hoc mocks of SPEC §10.3 interfaces in individual test projects (SPEC §10.4).
- The filesystem is not faked: use a per-test temp directory and clean it up (SPEC §10.4).
- Time-dependent logic (expiry, lockout, scan intervals, seeding time) is tested by advancing `FakeClock`, never by sleeping (SPEC §10.4).
- Tests that need real ffprobe or MPV skip themselves when the binary is absent (SPEC §10.4).
- Fixtures live under `server/tests/fixtures/` (metadata, definitions) and `server/tests/corpus/`.
- Safety-critical tests from SPEC §12 are never skipped, weakened or deleted to make a change pass.
