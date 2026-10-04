---
paths:
  - "server/src/**"
---

# Server rules

- `MediaMan.Core` holds domain types and the SPEC §10.3 interfaces and depends on no other MediaMan project (SPEC §10.2).
- Modules depend on `MediaMan.Core` interfaces, not on each other's implementations; `MediaMan.Server` is the only place that wires implementations together in DI (SPEC §10.2).
- Endpoints are ASP.NET Core minimal APIs and match `contracts/openapi.yaml` exactly: route, status codes, and the shared error model (SPEC §10.1).
- Each endpoint declares its permission next to its route mapping (SPEC §5, §12).
- Long-running work (scans, downloads) runs as background services and reports progress over SSE; it never blocks a request (SPEC §10.1, §11).
- Search, grab and import logic lives in services, not in endpoint handlers, so automation can call it later (SPEC §4).
- Any path written to disk is checked against External library roots first; writes go only to managed storage, the database and the artwork/transcode caches (SPEC §5).
