---
project: PoRedoImage
tier: 0
type: agent-context
last_updated: 2026-07-27
---

# PoRedoImage — AI Agent Context

**This file has moved.** Its content now lives in [`/AGENT.MD`](../AGENT.MD) at the repository root.

NET_RULES §6 requires a single living architectural source of truth. Two AGENT documents had begun
to drift — the copy here still described tests as never running in CI, and prescribed `IOptions<T>`
for configuration reads that had since standardised on `ConfigKeys` constants. Rather than keep two
in sync, the unique sections (system topology, strict tech stack, workflow loops, render model
rules, anti-patterns, command map) were merged into the root file under **Architectural Reference**.

Update `/AGENT.MD`. Do not re-add content here.
