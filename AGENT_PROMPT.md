# Fresh Agent M5 Handoff

Use the following prompt to start a fresh agent:

```text
Work in the multiplayer mod repository on M5, the bounded playable local release. M1 through M4 are complete; do not reopen them without concrete contradictory evidence. Read README.md, ROADMAP.md, DEVPLAN.md, handoff.md, mod.json, and the relevant source and validation projects before editing. Inspect git status, branch, HEAD, repository roots, and parent-repository boundaries. Preserve all unrelated and pre-existing changes.

Select the smallest complete M5 slice from the declared route, attack lifecycle, natural damage/revive, transitions, camera/tether/exit policy, compatibility ledger, save integrity, and soak gates. Use no more than three focused read-only research lanes when parallel research materially helps. Resolve recommendations against repository evidence and keep cross-repository ownership explicit.

Before implementation, mark only M5 In progress in DEVPLAN.md and add a dated execution-log entry. Record blockers instead of inventing policy. Update ROADMAP.md only if outcomes, scope, or release sequence change; update README.md when current behavior, controls, diagnostics, compatibility, or limitations change.

Safety constraints:
- Preserve the authority and fail-closed invariants in ROADMAP.md and DEVPLAN.md.
- Prefer the smallest behavior-preserving slice; do not broadly rewrite gameplay code or add speculative backward compatibility.
- Never use destructive git commands, revert unrelated work, or commit/push without explicit authorization.
- Do not use, copy, distribute, or commit game images, generated copyrighted assets, saves, credentials, secrets, screenshots, logs, or live scenario bundles.
- Keep legally owned US game data and live evidence private and separate from public disc-free CI.
- Do not edit outside the approved Git root without explicit coordination; parent SymphonyRecomp and RecompOne ownership must remain clear.

Run focused tests and the owning build/typecheck. The current co-op total is 203 tests, including the 196-test M4 subset and 7 processed-Pad-2 latch tests; current and pinned dynamic assemblies are 192512 bytes. The final parent Release automation suite passed 88/88 and the full host Release build passed without warnings. For live workflows, use bounded input, preserve artifact privacy, clear both ports after interruption, and verify neutral telemetry. The completed M3 smoke proves only bounded locomotion and a normal jump, not atomic two-port starts, broad route support, coyote/buffer behavior, natural combat/revive, transition endurance, save integrity, or soak completion.

After material work, update DEVPLAN.md with exact commands and concise results. Keep M5 In progress until its acceptance criteria pass. Before finishing, inspect both full and staged diffs, run diff checks, and report changed files, validation, blockers, and residual risks.
```
