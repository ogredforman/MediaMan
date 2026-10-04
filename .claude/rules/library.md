---
paths:
  - "server/src/MediaMan.Parsing/**"
  - "server/src/MediaMan.Library/**"
  - "server/tests/corpus/**"
---

# Parsing and library rules

- `ReleaseParser` is pure: no I/O, no clock, no DI. Same input, same output (SPEC §10.3).
- Parser changes run the corpus test; accuracy must not drop below the recorded baseline. Raise the baseline when accuracy improves; never lower it to make a change pass (SPEC §12).
- New parser cases go into `server/tests/corpus/release-names.json`, not one-off unit tests.
- The scanner only reads External libraries; it never writes sidecars, renames or moves files there (SPEC §5).
- Below the confidence threshold a file goes to MatchReview; never silently link a best guess (SPEC §7.2).
- Provider errors leave files `Pending` for retry; never mark them wrong or unmatched (SPEC §7.2 step 7).
- User match overrides win over automatic matching on every rescan (SPEC §7.3).
