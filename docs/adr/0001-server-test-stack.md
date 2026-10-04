# 0001. Server test stack: xUnit v3 on Microsoft.Testing.Platform, AwesomeAssertions, Verify deferred

- **Status:** Accepted
- **Date:** 2026-10-04
- **PLAN task:** 0.1

## Context

SPEC §10.1 named xUnit, FluentAssertions and Verify for server tests. When scaffolding (PLAN 0.1):

- FluentAssertions 8.x moved to a commercial licence (Xceed). 7.x stays Apache-2.0 but only gets critical fixes.
- Verify v33+ participates in the Open Source Maintenance Fee and fails the build (SponsorCheck SC021)
  unless a fee status is declared in MSBuild. The individual/open-source exemptions are time-bounded
  (renewed at most 12 months ahead), so a declaration must be maintained or the build, including CI, breaks.
- xUnit v3 (4.x) runs on Microsoft.Testing.Platform (MTP); the .NET 10 SDK no longer runs MTP projects
  through VSTest.

## Options considered

1. **FluentAssertions 7.x pinned:** free, but a dead-end branch that someone will eventually "upgrade" by mistake.
2. **AwesomeAssertions:** Apache-2.0 community fork of FluentAssertions 7, actively maintained, same `.Should()` API.
3. **Plain xUnit `Assert`:** no dependency, weaker failure messages.
4. **Verify now with an exemption declared, or pinned to v32:** a licence declaration made on the owner's
   behalf, or a pin to an old major; neither is needed until snapshot tests exist.

## Decision

Use xUnit v3 on Microsoft.Testing.Platform (opted in via the root `global.json`) and AwesomeAssertions.
Leave Verify out until the first snapshot test needs it; the fee-status choice is parked in PLAN.md.

## Consequences

- `dotnet test` runs in MTP mode; `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are not used.
- Test files `using AwesomeAssertions;` (a global using in `server/tests/Directory.Build.props`).
- Before adding Verify, decide its fee status (exemption with renewal, sponsorship, or an alternative snapshot library).
