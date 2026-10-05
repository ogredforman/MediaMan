# 0004. Web stack versions and API codegen with Orval

- **Status:** Accepted
- **Date:** 2026-10-05
- **PLAN task:** 0.5

## Context

SPEC §10.1 requires the web UI's API client and its MSW mock handlers to be generated from
`contracts/openapi.yaml`, with server state going through TanStack Query. Component tests must never
need a running server (SPEC §10.4). The toolchain also has to pass `tsc` strict mode and type-aware ESLint.

## Options considered

1. **Orval:** one config generates a typed fetch client, TanStack Query hooks and MSW handlers (with
   faker defaults and per-handler overrides). Pros: one tool, hooks for free, handlers always match the
   contract. Cons: generated code is verbose; mocks pull in faker (dev only).
2. **openapi-typescript + openapi-fetch + msw-auto-mock:** types-only client plus a separate mock
   generator. Pros: tiny runtime. Cons: three tools to keep in step, hooks written by hand.

## Decision

Use Orval (`web/orval.config.ts`) to generate `web/src/api/`. Its output is committed and never
hand-edited. Requests go through the hand-written mutator `web/src/lib/http.ts`, which adds the
bearer token and turns non-2xx responses into an `ApiError` that carries the contract's `Problem`.

Version pins, both forced by ESLint plugin peer ranges at the time of writing:
- **TypeScript ~6.0:** typescript-eslint supports `<6.1`, so not TS 7.
- **ESLint 9:** eslint-plugin-jsx-a11y doesn't support ESLint 10 yet.

Lift both once the plugins catch up.

## Consequences

- A contract change means running `npm run generate -w web` and committing the result in the same commit
  (`.claude/rules/contracts.md`).
- Tests and mock mode (`npm run dev:mock -w web`) use the generated handlers, with deterministic overrides in
  `web/src/mocks/handlers.ts`. The production build leaves the mocks and faker out because they're loaded only behind `VITE_API_MOCK`.
- Orval mocks `GET /events` (SSE) as a single JSON body. That's harmless until the SSE client exists, but
  the SSE client will need a hand-written stream handler.
- `web/public/mockServiceWorker.js` (from `msw init`) is copied into `dist/`, but it does nothing unless
  registered, and only mock mode registers it.
