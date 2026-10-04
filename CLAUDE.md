@AGENTS.md

## Claude Code

- Use plan mode for any task that touches the data model (`MediaMan.Data`, SPEC §6), `contracts/openapi.yaml`, the module interfaces (SPEC §10.3), auth and permissions, or the VPN gate.
- Orient with `docs/CODEMAP.md` before reading code.
- Workflows (devflow plugin): `/devflow:next-task` (do the next PLAN task), `/devflow:park <idea>`
  (capture an idea without doing it), `/devflow:progress` (where things stand).
- The `devflow:reviewer` subagent checks diffs against the hard rules above. Use it before finishing any task.
