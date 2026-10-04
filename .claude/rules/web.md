---
paths:
  - "web/**"
---

# Web UI rules

- Call the server only through the generated client in `web/src/api/`; never hand-edit it, regenerate it from `contracts/openapi.yaml` (SPEC §10.1).
- Server state goes through TanStack Query; routes through TanStack Router (SPEC §10.1).
- Component tests use MSW handlers generated from the contract; they never need a running server (SPEC §10.4).
- Colours come from the theme tokens (`#0E0B14`, `#17121F`, `#8B5CF6`, `#C4B5FD`), not ad-hoc hex values; motion stays within 150–250 ms (SPEC §11).
- Artwork-first: posters and stills, not raw tables, wherever titles are listed (SPEC §5).
- Every interactive element is keyboard reachable with a visible focus state; text meets WCAG AA contrast (SPEC §9, §11).
- Hiding UI for missing permissions is cosmetic only; the server still enforces it (SPEC §5).
- Same UI for desktop and browser: branch on platform capabilities (e.g. MPV available), never on "is Electron" for layout (SPEC §5 One UI everywhere).
