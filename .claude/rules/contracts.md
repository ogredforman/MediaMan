---
paths:
  - "contracts/**"
---

# API contract rules

- `contracts/openapi.yaml` (OpenAPI 3.1) is the source of truth; the server conforms to it and the web client and MSW handlers are generated from it (SPEC §10.1).
- A contract change is deliberate: regenerate the TS client and MSW handlers and update the server conformance tests in the same commit (SPEC §5 Contracts first).
- Every operation lists its required permission and uses the shared error model. Long-running work is reported through the SSE event envelope, not polling endpoints (SPEC §10.1).
- Playback goes through the client-capability negotiation (`POST /playback/sessions`); don't add client-specific playback endpoints (SPEC §4, §7.4, §7.5).
