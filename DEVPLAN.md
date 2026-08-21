# Multiplayer Mod Devplan

## Authority and Source

- Planning baseline: research synthesis completed 2026-08-19.
- Repository baseline: `dc212f1` on `main`.
- Product and current behavior authority: `README.md`.
- Outcome and release-sequence authority: `ROADMAP.md`.
- Execution order, status, evidence, and blockers authority: this document.
- Freshness at creation: worktree clean and synchronized with `origin/main`.
- Planning provenance: repository research plus external engineering references; no planning-board state or approval digest exists.

## Intent Brief

- Problem: the mod has demonstrated difficult low-level co-op primitives, but validation is partly manual, support is bounded, shared-world policy is incomplete, and internal state is not ready to become a network protocol.
- Users: local co-op players, mod developers, runtime maintainers, and future host/client operators.
- Success signals: reproducible tests, no native-state corruption, an honestly supported local route, explicit authority contracts, and host-authoritative online play that converges under ordinary network faults.
- Constraints: preserve the product contract in `ROADMAP.md`; require a legally owned US game copy for live gameplay tests; fail closed on ownership or lifecycle uncertainty.
- Non-goals: rollback, lockstep, split-screen, independent rooms, a second native Alucard, host migration, and additional players.
- Delivery horizon: incremental `v0.5` through `v1.0`, gated by evidence rather than dates.

## Operating Invariants

```text
native observation
  -> validated immutable snapshot
  -> managed Player 2 simulation
  -> bounded gameplay intent
  -> exact-owned native lease
  -> native engine outcome
  -> observed evidence
```

- Gameplay code should consume snapshots and commands rather than retain raw guest addresses.
- Every transient observation, event, and native lease must carry a room epoch.
- Native publication must remain inactive until ownership and payload are valid.
- Cleanup must revalidate exact ownership; ambiguity stops mutation rather than clearing a possibly reused slot.
- Processed input is sampled once per simulation update and is the replay/network input authority.
- Simulation correction and visual smoothing remain separate.
- A broad final test never replaces focused milestone tests.

## Progress Summary

Update this table whenever work starts, becomes blocked, or completes. Only one milestone should normally be `In progress`.

| ID | Milestone | Status | Evidence | Blocker/next action |
| --- | --- | --- | --- | --- |
| M0 | Planning baseline | Complete | `ROADMAP.md`, `DEVPLAN.md`, `AGENT_PROMPT.md` | Begin M1 |
| M1 | Repository-owned compile and compatibility gate | Complete | Current and `v0.4.3b` static/runtime compiles plus manifest, hook, symbol, version, constant, warning, and direct-damage contracts pass | None |
| M2 | Structured diagnostics contract | Complete | 20 codec tests, 31 parent transport/lifecycle tests, protocol `1.1`, and both compatibility gates pass | None |
| M3 | Data-driven live scenario runner | Complete | Cataloged `sotn-scenario/1` runner, bounded private artifacts, verified cleanup, and passing live `coop-locomotion-jump` smoke | Begin M5 |
| M4 | Pure state machines, replay, and fault injection | Complete | 196 focused tests; 339-frame/308-snapshot replay; prepared reconstruction/reset; protected fake memory; guarded direct dispatch; both APIs pass | None |
| M5 | Bounded playable local release | In progress | Three required independent 25/25 route campaigns, live telemetry/damage/revive, native attack kill/reward advancement, and bounded post-retry recovery observed | Accepted contact/projectile/drop artifacts, save/reload, and soak qualification |
| M6 | Broader traversal, hazard, and awareness coverage | Not started | CEN-only and static-terrain baseline | Depends on M5 |
| M7 | Shared-world event ledger and keyframes | Not started | Native outcome observation baseline | Depends on M5; informed by M6 |
| M8 | Host-authoritative LAN movement | Not started | No transport implementation | Depends on M4 and M7 contracts |
| M9 | Networked combat, progression, and reconnect | Not started | No transport implementation | Depends on M8 |

## Decisions and Assumptions

| ID | Decision | Rationale | Revisit gate |
| --- | --- | --- | --- |
| D1 | Stabilize local co-op before networking | Avoid making private mutable fields into an accidental protocol | M5 complete |
| D2 | Use host-authoritative input and snapshots | Native world is not suitable for cross-platform deterministic lockstep | M8 design review |
| D3 | Keep Player 1 as sole camera and transition owner initially | Preserves singleton native lifecycle | Post-M6 product review |
| D4 | Keep Player 2 session-only initially | Avoid undocumented native save mutation | M7 persistence review |
| D5 | Expand support through compatibility manifests | Prevent silent assumptions of castle-wide support | Never; manifest evolves |
| D6 | Extract narrow testable units, not rewrite the mod wholesale | Minimize regression risk in a safety-sensitive prototype | Revisit only with test coverage |

## Dependency Order

```text
M1 -> M2 -> M3 -> M5 -> M6
M1 -> M4 -> M5
M5 -> M7 -> M8 -> M9
M6 --------^ informs world policy and supported-route scope
```

Cycle check: passed.

## M1: Repository-Owned Compile and Compatibility Gate

- Outcome: co-op source and manifest are validated without temporary absolute-path harnesses.
- Discovery gate: resolved. The co-op repository owns the validation project and workflow; SymphonyRecomp and RecompOne checkouts are read-only build inputs.
- Scope: current-runtime compile, pinned `v0.4.3b` compile, manifest validation, selected Roslyn warnings, hook/symbol checks, and CI integration where repository ownership permits.
- Exclusions: gameplay behavior changes and broad source refactoring.
- Confirmed change map: add a co-op-owned validation project, orchestration script, compatibility workflow, and ignored dependency/build directories; read `source/CoopFeasibilityMod.cs`, `mod.json`, public SymphonyRecomp wrappers/events, and the real runtime `ModCompiler` without editing parent repositories.
- Supported baselines: current-tested SymphonyRecomp `a2694df874e2346384a5afa9e1734d2dde6a9d27` with RecompOne `f6e47c4c515c57f7c523cdbaa68b8e9bf8994500`; pinned `v0.4.3b` SymphonyRecomp `c1037ded877f60588a162675fa558415bf6c1995` with RecompOne `9c5d7fced450549b6a8874e4fa9a4accae1eb138`.
- Unknown change map: none for M1. Parent-owned structured diagnostics and scenario APIs remain explicit M2/M3 follow-up work.
- Acceptance criteria: no dependency on temporary out-of-repository harnesses; one documented command runs the checks; current and pinned compatibility pass; malformed manifest and missing required symbols fail clearly.
- Focused tests: `bash tools/validate.sh` passed locally with .NET 10 on `PATH`; CI runs the same script with explicit exact-revision roots.
- Validation evidence: current-tested `a2694df8`/`f6e47c4c` and supported `v0.4.3b` `c1037ded`/`9c5d7fce` each produced a 103424-byte dynamic mod assembly through the real `ModCompiler`. Built-in negative contracts rejected missing manifest fields, missing source hooks, changed compatibility constants, direct `DealDamage`, and missing host symbols. `bash -n tools/validate.sh` passed.
- Expected warnings: existing host/runtime nullable and unused-field warnings remain visible; selected mod-validation warnings `CS0168`, `CS0219`, and `CS1998` are errors.
- Documentation: update this document and relevant development instructions.
- Failure rule: stop on any failed required check; do not stage, commit, or claim completion.
- Rollback/recovery: additive tooling only; remove the new gate without changing gameplay source if the approach is invalid.
- Proposed commit boundary: one coherent compile/compatibility validation change, only if commit authorization is later given.

## M2: Structured Diagnostics Contract

- Outcome: `P2D4` becomes a typed, versioned assertion contract available through a read-only automation boundary.
- Scope: report model/parser, unique keys, legal states, numeric bounds, cross-field invariants, diagnostic generation/frame identity, and safe reset/read operations.
- Exclusions: arbitrary mod mutation through MCP and log scraping as the long-term interface.
- Selected nested slice: add a bounded co-op-owned parser/canonical formatter and structured `p2d4/1` envelope under `source/`; capture load-session, diagnostic-generation, mod-frame, and automation-frame identity; expose exact public capture and generation-checked reset convention methods; validate the legacy line before returning it; add game-free malformed/golden/invariant tests under `.validation/`.
- Confirmed ownership: the co-op repository owns report semantics and provider output. RecompOne must own unload-safe provider registration, and the parent automation repository must own bridge/client/MCP transport.
- Cross-root integration: completed additively while preserving the existing headless-save and startup-timeout changes. RecompOne owns convention discovery and collectible-lifecycle cleanup; the parent owns protocol `1.1`, strict request validation, serialized execution, bounded response parsing, typed client transport, and MCP tools.
- Acceptance criteria: parser tests cover valid and malformed reports; duplicate keys fail; impossible counter relationships fail; MCP or equivalent read-only access returns structured diagnostics; report schema changes require explicit version updates.
- Focused tests: `dotnet run --project .validation/CoopDiagnostics/CoopDiagnostics.csproj --configuration Release` passed 20/20. `bash tools/validate.sh` passed the diagnostics tests, real default report/envelope generation, static compile, and runtime `ModCompiler` on current-tested and `v0.4.3b` surfaces; both produced 119808-byte dynamic assemblies.
- Nested-slice evidence: exact/unique keys, typed predicate states, canonical printable-ASCII decimal parsing, overflow and envelope-generation rejection, selected render/contact/transition/attack/awareness/HUD/health invariants, valid failure-report preservation, 16 KiB legacy bound, 64 KiB envelope bound, per-load session ID, mod/automation frame stamps, and generation-checked reset convention.
- Parent evidence: `dotnet test tools/SymphonyRecomp.Automation.Tests/SymphonyRecomp.Automation.Tests.csproj --configuration Release` passed 31/31. Tests cover exact convention discovery, incomplete/duplicate providers, protocol versioning, typed named-pipe capture/reset identity, MCP argument/annotation boundaries, final 64 KiB serialization, and cancellation versus executing-command outcomes. `dotnet build RecompOne/RecompOne.Runtime/RecompOne.Runtime.csproj --configuration Release` passed with three existing warnings. The full `RecompOne.SoTN.csproj` Release build passed after excluding dynamically loaded `mods/**` source from host default compile items, with two existing warnings. Independent lifecycle/security review found no blocker.
- Integration semantics: providers publish only after successful `OnLoad`; partial load failure removes hooks, calls `OnUnload` only through the failing initialized instance, drops delegates, and unloads the collectible context. Normal unload drops provider delegates before mod teardown. Capture/reset enter the runtime main-thread queue; bridge shutdown cancels only commands whose execution has not started. Capture requires a JSON object with a 32-hex session and nonnegative generation and enforces the final DTO bound. Reset requires exact identity and confirmation and returns a distinct applied result without a fallible post-reset capture.
- Rollback/recovery: preserve the existing human-readable report until structured access is proven.
- Proposed commit boundary: typed diagnostics plus focused tests and documentation.

## M3: Data-Driven Live Scenario Runner

- Outcome: scenarios drive both controller ports and collect a reproducible failure bundle.
- Scope: JSON or YAML scenario schema, start predicates, input timelines, checkpoints, timeouts, diagnostics, state, entities, logs, screenshots, and neutral-input cleanup.
- Initial scenarios: locomotion/jump, crouch clearance, transition reconstruction, incoming damage, down/revive, contact attack, projectile attack, CEN awareness, and HUD/render restoration.
- Acceptance criteria: one command runs a canonical scenario; cleanup clears input in `finally`; failure output identifies the first failed checkpoint; artifacts contain build identity and scenario version; live scenarios remain private where legal game data is required.
- Focused tests: schema tests, runner tests with a fake client, then a controlled live smoke.
- Completion evidence: the parent owns a strict embedded-only `sotn-scenario/1` catalog, fakeable runner, exact diagnostic reset, bounded checkpoints/outcomes, private artifact bundles, and verified neutral-input cleanup. `coop-locomotion-jump` version `1` passed against loaded mod `coop-feasibility` `v0.4.0` from Play/Alucard with loading/menu/map false and `K=-`, `H=P`, `E=0` after an exact generation `0` to `1` reset.
- Live sequence/result: Port 2 received Right 12, neutral 4, Left 12, neutral 4, Cross 1, neutral 31; Port 0 neutral was also exercised. At automation frame 1912 diagnostics reported `M=P:18/18/1`, `H=P`, `E=0`, and `J=W:N1/C0/B0/R0,0`, proving the intended normal jump without claiming coyote/buffer completion. Manual inter-tool latency means this smoke does not prove same-frame or atomic two-port starts.
- Artifact/cleanup evidence: state, diagnostics, at most 32 entities, 100 log lines, and a validated PNG were captured privately; no live bundle, image, or save was committed. Explicit clear was applied at frame 2072, and frame 2160 telemetry reported zero masks and zero remaining frames on both ports.
- Validation evidence: the new processed-Pad-2 latch suite passed 7/7 and the complete co-op gate passed 203/203; M4 remains its distinct 196-test subset. Current and pinned dynamic compilers produced 192512-byte assemblies. After final mutation-gate, cancellation-budget, and frame-accounting hardening, the parent Release suite passed 88/88 and the full host Release build completed with zero warnings.
- Rollback/recovery: scenario execution is observational except bounded controller input and existing safe mod diagnostic reset.
- Proposed commit boundary: runner, one smoke scenario, tests, and usage documentation.

## M4: Pure State Machines, Replay, and Fault Injection

- Outcome: high-risk policy can be tested without launching the game.
- Scope: explicit session, room, locomotion, combat, revive, and native-lease machines; `InputFrame`; `ProxySnapshot`; room epoch; deterministic managed hashes; generated event sequences; Nth-operation fault injection.
- Exclusions: a whole-file architecture rewrite or networking transport.
- Selected first slice: movement-scoped replay identity. Add a session-local nonzero room epoch distinct from the native attack room hash; sample one immutable processed `InputFrame` inside `UpdateProxy`; capture a movement-only `ProxySnapshot` after a successful update; encode fixed canonical little-endian bytes and FNV-1a 64-bit hash; test epoch, byte, hash, replay, and validation behavior game-free. This is an M4 test/replay schema, not an M8 packet or keyframe.
- First-slice evidence: `dotnet run --project .validation/CoopManagedState/CoopManagedState.csproj --configuration Release` passed 13/13, covering initial/repeated/reload/reset/layer-event epochs, reset-transition reconciliation, canonical 115-byte layout, golden hash `7dc73566664f9589`, culture independence, perturbation, default/enum/identity rejection, and deterministic replay. `bash tools/validate.sh` passed this suite plus current-tested and `v0.4.3b` static/runtime compilation, producing 125952-byte dynamic assemblies. Historical schema-v1 evidence; superseded by final schema v2 below.
- Next slice: pure managed health ownership for HP, incoming damage, invulnerability, hurt lock, downing, reset, reconstruction protection, and diagnostic projection. Preserve contact winner selection and native revive eligibility reads in the adapter; add revive transitions only after defining one immutable eligibility input and preserving same-update timer ordering.
- Managed-health evidence: one `ManagedHealthState` and pure reducer now own fixed 100 HP policy, consumed/suppressed/applied damage, 60/18 timers, downing, 120-update revive to 50 HP, cancellation/recovery counters, reconstruction protection, checked overflow, and invariant projection. One immutable revive observation owns inclusive distance/button/control/alive/room eligibility while memory reads remain in the adapter. `dotnet run --project .validation/CoopManagedHealth/CoopManagedHealth.csproj --configuration Release` passed 13/13 boundary and deterministic tests; the full gate passed current-tested and `v0.4.3b` dynamic compilation. Independent review found no blocker.
- Historical attack-lease slice boundary: memory reads/writes, guest calls, target observations, projectile timing, and publication order initially remained in `CoopFeasibility`; only reserve/exact/fault/mismatch/clear/retry/quarantine metadata moved first. Final publication and adapter safety are superseding evidence below.
- Historical attack-lease evidence: the first four-phase reducer owned exact tuple, generation seed, retryable quarantine, terminal mutation stop, and reset carry, passing 11/11 focused tests. Final owner/nonwrapping-revision, prepared-reset, exhaustion, and ABA coverage expands this suite to 16 tests as recorded below.
- Next slice: incoming contact opportunity/arbitration reducer. Convert scanned slot data into immutable observations; own identity/phase/repeat, entry/stay/exit, baseline/resume grace, consumed opportunities, strongest winner, and deterministic slot-order ties. Keep RAM scanning, guard hashes, geometry, health effects, knockback, and animation in adapters.
- Contact-arbitration evidence: one allocation-free 128-slot machine owns eligibility generations, identity/phase history, baseline, suspension and one-scan resume grace, 60-scan repeats, entries/stays/exits, opportunity count, and strongest clamped winner with lower-slot ties. Runtime scanning, guards, geometry, health and effects remain adapters. `CoopContactOpportunity` passed 15/15, including 64 generated seeds and zero bytes across 10,000 warmed scan/suspend cycles; independent review found no blocker.
- Jump-forgiveness evidence: a two-phase machine preserves tap, grounded/coyote priority, walk-off refresh, landing-buffer consumption, decay, crouch edge, counters, and all lifecycle clears. Continuations carry a machine owner and nonwrapping `ulong` revision; stale, duplicate, wrong-owner, default, out-of-order, and exhaustion cases fail closed. `CoopJumpForgiveness` passed 15/15. Replay canonical writing now supports spans, preserving the then-current 115-byte schema while eliminating the live per-update byte-array allocation. Historical schema-v1 evidence; superseded by final schema v2 below.
- Managed-stance evidence: a reducer now owns crouch/stand and blocked-stand policy while the adapter retains hull queries, collision aborts, movement, animation, and diagnostics. Owner- and nonwrapping-revision-bound probe commands make clear/blocked outcomes atomic and reject stale or cross-owner completion. `dotnet run --project .validation/CoopManagedStance/CoopManagedStance.csproj --configuration Release` passed 12/12 entry, hold, clear, blocked, fault, stale, owner, lifecycle, generated, exhaustion, and allocation tests; `bash tools/validate.sh` passed all focused suites and both current and `v0.4.3b` runtime compilers with 145408-byte assemblies.
- Historical reconstruction-policy evidence: the initial exact 80-candidate order and shared orchestration seam passed 15/15 tests. Final probe-exception and prepared production-commit coverage expands this suite to 16 tests, with transactional adapter evidence recorded below.
- Final subsystem evidence: `CoopAttackPublication` passes 29 publication-order, partial-rollback, projectile, unload, target-observation, reset-preflight, Nth-fault, generated-lifecycle, protected-memory, and allocation tests. `CoopAttackLease` passes 16 authorization/quarantine/reset-exhaustion tests. `CoopMovementSession` passes 23 lifecycle, epoch, transition, recovery, prepared reconstruction/reset, fault, exhaustion, hook-order, and allocation tests. `CoopManagedLocomotion` passes 16 exact 43-pose timing, selection, attack-boundary, lifecycle, generated-replay, allocation, and replay-projection tests.
- Adapter safety evidence: production reconstruction uses one shared prepare/validate/nonthrowing-commit seam for scalar, stance, jump, locomotion, pose, health, session, and diagnostic projections. Diagnostic reset prepares every fallible reducer and checked generation before native cleanup or outer mutation. Collision, attacker-ID, and `CalcAttack` calls use direct dispatch under a stack-backed 39-word context guard; collision and equipment scratch restoration attempt every byte before reporting failure. Current and pinned validators execute registered callback mutation/throw restoration, every 260-byte collision and 144-byte equipment scratch fault ordinal, and warmed zero-allocation calls.
- Final replay-closure evidence: canonical schema v2 is 116 bytes and stores stable movement-session phase at offset 57 with explicit wire values `1..9`; zero is invalid. The fixture golden is `bb05d22920f8b29f`, and the recorded integrated final hash is `9566dd47e98e97d3`. Two 339-frame runs compare participating movement-session, jump, stance, locomotion, health, and reconstruction state plus 308 eligible snapshots, canonical bytes, and hashes. One crouch-input perturbation diverges at frame 4; 50,000 warmed integrated cycles allocate zero bytes. Attack lease/publication/target observation and contact arbitration use their standalone generated/fault suites. The full gate passes 196 focused tests; current and `v0.4.3b` runtime compilation each produce a 192000-byte assembly.
- Target-selection tie scope: M4 proves that equal clamped incoming damage retains the lower native slot. A reusable enemy target resolver and cross-helper consistency remain M6 scope.
- Acceptance criteria: tests cover coyote/buffer timing, crouch/stand, reconstruction order, contact identity, damage/invulnerability, revive cancellation, attack publication/cleanup/quarantine, and target-selection ties; identical snapshots and inputs produce identical managed hashes; injected failures preserve protected state.
- Focused tests: unit, property/generated-sequence, fake-memory, and hook-adapter tests.
- Rollback/recovery: extract one seam at a time while preserving existing runtime behavior and diagnostic semantics.
- Proposed commit boundary: use small subsystem-scoped commits only if later authorized.

## M5: Bounded Playable Local Release

- Outcome: a declared route is honestly playable in same-room local co-op.
- Scope: attack lifecycle hardening, natural incoming damage/revive, transition reliability, camera/tether/exit policy, compatibility ledger, save integrity, and player-visible suspension.
- Exclusions: game-wide support, bosses with unique progression, networking, and independent Player 2 inventory.
- Release-evidence foundation: implemented `p2d4/2` as a closed flat scalar metric envelope while preserving the exact legacy line/fields; added kind-specific contact/projectile allocation, window, and native-hit instrumentation; exact-owned bounded marker census; protocol `1.2` atomic one-or-two-port input batches; exact scenario v1/v2 handling with typed metrics, area/room predicates, and reset `before`/`none`; a bounded five-entry embedded catalog; and a public data-only candidate NO0 route manifest plus strict validator.
- Candidate route v2: alternating `NO0 140 -> 220` across the flat clock junction/save-room doorway. Verdict remains `candidate-untested`. Exclusions are the upper clock cell (index 9), the red-door west corridor (52/12/20) pending stair and hazard qualification, the east lower room 148 passage, the south statue passage, CEN elevator/Maria branching, bosses, water, shaped/moving terrain, and projectile-through-wall claims.
- Automated probes: canonical locomotion deliberately migrated to scenario v2 and atomic dual-port start. Setup-gated transition-west, contact-hit, projectile-hit, and damage-revive probes use strict room/metric preconditions and never claim an unperformed pass. Damage/revive preserves cumulative identity with reset `none`; all probes remain at most 60 seconds and cleanup-neutral.
- Release-policy slice: one allocation-free reducer owns Active/Warning/Resistance/Reconstructing/Suspended phases at `160x112`, `224x160`, and strict `>256x192` bounds. It blocks only outward P2 horizontal intent, latches hard reconstruction to avoid churn, leaves P1 as sole camera/exit owner, and exposes nonwrapping entry/frame/max/status/hard-recovery evidence. A transient GP0 pip remains eligible in valid stage display during unsafe, transition, uninitialized, and suspended states while ordinary avatar/HUD mutation remains gated.
- Corrected route/scenario facts: telemetry predicates use `MarbleGallery`, while `NO0` is descriptive overlay code. Live room identity discovery (2026-08-20): telemetry `room`/`area` are the raw bytes of the global room-table offset at `0x801375BC`, not per-stage room indexes; NO0 index i maps to global `0x27E4 + 8*i`. Live-verified identities: lower clock junction index 21 = area 40 room 140 cell `32,27`, save room index 31 = area 40 room 220 cell `31,27`, corridor index 10 = room 52 cells `29..31,26`, room 5 index 5 = room 12 cells `26..28,26`, east lower room index 22 = room 148 cell `33,27`. The upper clock cell (index 9) was never observed as a current room live. Transition v4 uses area 40 room 140/cell `32,27` with a west crossing into room 220; combat probes use the corridor room 52.
- Foundation validation: parent Release tests passed 101/101; MCP Release build passed with 0 warnings/errors; full host Release build passed with 2 unchanged warnings (`wrapers/Game.cs` nullable return and unused `StatsCheatPanel._revealMap`); complete co-op gate passed 204 tests (203 prior plus route manifest), all fault/allocation probes, and current/pinned 213504-byte runtime compiles. Changed parent projects and new validator are warning-free; compatibility dependency builds retain previously documented warnings.
- Slice-2 validation: 10/10 tether/aggregate tests, 24/24 movement-session tests, and full co-op 215/215 passed; current and pinned dynamic compilers produced 243712-byte assemblies. Parent Release passed 102/102, MCP Release built with zero warnings, and a nonincremental full host Release build passed with five existing warnings (three RecompOne.Runtime debug/audio warnings plus `wrapers/Game.cs` nullable return and unused `StatsCheatPanel._revealMap`).
- Slice-3 implementation: fatal rendering now hard-stops later direct calls while nonfatal suspension status remains eligible; attack marker publication/cleanup paths refresh synchronously; v2 has identity-bound checked integer deltas; all probes use corrected setup/timing/per-run predicates; a fixed 32-slot NO0 observer classifies exact drops, associations, ambiguity, no-drop, lifecycle, and optional observed native EXP without writes; and a parent-owned game-free executor consumes the manifest-derived ordered 25-transition campaign input.
- Slice-3 validation: 227/227 co-op tests passed, including 9 drop, 11 tether/render/aggregate, and 2 synchronous marker projection tests; current and pinned dynamic compilers produced 271872-byte assemblies. Parent Release passed 114/114, MCP Release built with zero warnings, and the nonincremental full host Release build passed with five existing warnings.
- Slice-4 implementation: virtual-keyboard mode remains the missing-config default and only an explicit checkbox toggle persists the mod-scoped boolean. The parent now has an exact two-entry embedded `sotn-campaign/1` catalog, quick-return start/status/confirmed-cancel tools, one lifetime execution lease, monotonic route and 60-minute observers, fresh-token neutral cleanup, bounded private periodic manifests, aggregate samples, sparse validated screenshots, and failure-only logs/entities. `release/m5-release-matrix.json` is a strict data-only objective/human-approval contract and performs no save access.
- Slice-4 validation: 123/123 parent Release tests passed, including catalog, exact route, wrong route, metric/identity/no-progress failures, one-run gate, cancellation, shutdown, cleanup-token, privacy/bounds, and fake-clock 13-sample soak coverage. The complete co-op gate passed 228 contracts and current/pinned 271872-byte dynamic compiles. MCP Release built with zero warnings; full nonincremental host Release built with five existing warnings. One attempted parallel parent-test invocation raced the host no-incremental cleanup and was rerun successfully after the host build.
- Final review hardening: campaigns now require neutral bridge and telemetry input both before and under the lifetime lease, permit only frame-bounded legitimate owned attacks during soak, persist success artifacts before publishing `Passed`, share the scenario runner's strict complete `p2d4/2` parser, await shutdown/lease completion, and create Unix artifacts at `0700`/`0600`. Target capture exposes 16-record overflow, blocks unique causality, and reports ambiguity/overflow; contact/projectile starts require exactly one truthful current compatible target. Route/campaign order is coupled by a checked SHA-256 fingerprint, stale manifest limitations are corrected, and the persisted keyboard preference has a pure behavioral adapter test. Final validation passed 135 parent Release tests, 229 co-op contracts, current/pinned 273920-byte dynamic compiles, zero-warning MCP Release, and the nonincremental host Release build with five existing warnings.
- Live evidence and fixes: three distinct pre-liveness cold boots/canonical v2 passes plus one post-liveness canonical pass succeeded and `K=-` persisted. A sample memory card (crissaegrim `SampleSave.mcr`, US `BASLUS-00067`, 80.9%) was patched via the decomp `SaveData`/`SaveInfo` layout (stage 0x228, cell 0x22C/0x22E) to spawn in Marble Gallery; a wrong-stage wrong-cell variant crashed the room layer load and was corrected before acceptance. Live room identity discovery corrected route v1's impossible constants; route v2 (140/220 ping-pong) is live-room-verified. Castle Entrance P1 telemetry populated control/HP/level/EXP. Natural P2 damage recorded six events and one down at 0 HP with zero invariant/fatal/guard/restoration/orphan/quarantine failures. After moving P1 to safe flat terrain, manual overlapping P1 Down/P2 Circle produced starts/revives/recoveries `1/1/1` and HP 50. The observer soak reached sample 1/13 before explicit cancellation finalized private evidence with cleanup succeeded/verified; it is not a 60-minute pass. Wrong-stage route preflight rejected and stayed Idle. The managed process later exited without a logged bridge/runtime error, retained as risk rather than crash attribution.
- Retry hardening and validation: unsupported Castle Entrance stairs had alternated Suspended/Reconstructing with 206 suspension entries, maximum consecutive 1, 219 attempts, and repeated failures before flat terrain recovered. A pure nonwrapping policy now holds stable visible suspension, suppresses updates 1..29, retries once on safe update 30, rearms after failure, clears on success/lifecycle/reset, and keeps collision/fatal terminal. Metrics expose cooldown/retries/suppressed/reason. Full validation passed 238 co-op contracts and current/pinned 282112-byte compiles, 145 parent Release tests, zero-warning MCP, and the host with five existing warnings. A later unsupported-terrain live trace observed one failed reconstruction, 29 suppressions, one retry, then `Selected` recovery with cooldown clear and no fatal; this accepts the bounded retry policy observation, not general unsupported terrain.
- Final sequential safety validation: exact-owned lifetime now has nonwrapping current/cumulative-maximum metrics and an exact 48-window campaign ceiling; minute 60 is a fresh validated sample at/after 3600 seconds and exactly 13 samples remain mandatory before durable success. Full validation passed 244 co-op contracts and current/pinned 284672-byte compiles, 149 parent Release tests, zero-warning MCP, and the host with the same five existing warnings. M5 remains In progress; no live restart or acceptance is claimed.
- Native-load stale-identity correction: live trace showed a `PlayerLoaded` bootstrap selecting retained room 44 and closing before engine room 140 published. Bootstrap now retains the pre-load stage/area/room identity, treats a selected reconstruction of it as provisional, rebaselines armed identity churn without a semantic transition, and closes only on the reducer-stabilized selected reconstruction of a different engine-observed identity. The bounded reconstruction trace labels that closure `Selected:bootstrap-closed-post-load-identity`. The .NET 10 containerized full gate passed 258 contracts and current/pinned 291328-byte compiles; the same four existing dependency warnings remained.
- VSync native-load authorization (2026-08-21): live evidence established that neither save event nor direct `ApplySaveData` post-hook fires reliably. A pure bounded adapter-local transaction now latches only MainMenu/game-step 6/engine-step `0x33`, arms only on `NowLoading`/loading or the documented occupied-save `0x100/0x101/0x104` progression, rejects idle/back/New Game/unsupported/incompatible paths, and times out after 1,800 observations. Armed bootstrap survives player reload, loading, retained-room provisional reconstruction, stale identity, suppressed layer callbacks, and retries until stable destination closure or timeout. Trace sources expose observed/loading/arm/cancel reasons without save data. The isolated .NET 10 gate at `/tmp/opencode/coop-validation-work-20260820-2` passed 267 contracts and current/pinned 301056-byte dynamic compiles with the same four dependency warnings.
- Native-load live verification through `56df1c6` (2026-08-21): file select was observed and selected-save progression armed bootstrap. Stale room 44 selected provisionally; room 140 remained nonsemantic with `transitionPending=false` and `transitionReconstructionFailures=0` during retries; stable room 140 selected and closed bootstrap as `CHANGED_IDENTITY`. The subsequent `140 -> 220` crossing was a normal exact completed +1/pass +1 with 9 pixels and zero abandonment or transition reconstruction failure. This supersedes the prior `ce961a9` pending-live-verification claim.
- Live route campaign investigation (2026-08-20, second session): Pad 2 keyboard via host `Keys2` bindings plus XTest key delivery drove real non-automation input. Six `coop-route-25` attempts failed at the first transition with three distinct metric signatures. Root cause is a mod-side transition-accounting defect, not input cadence: one physical 140/220 door crossing produced `transitionCompleted` deltas of +1 (rare, only with frame-exact automation timing), +2, and even +3 (continuous P2 hold), with `postTransitionAbandonments` +0..+2 varying with session state; an identical scripted crossing was clean early in a session and double-counting later. The door sequence fires multiple room-change/layer-load engine events and the movement-session reducer counts each as a completed transition depending on event timing. This blocks the campaign's exact `deltaEq 1` per-transition contract and makes scenario checkpoint counters flaky. RESOLVED (2026-08-20): root cause was bounds-inclusive room identity — `ManagedRoomKey` equality includes camera bounds, and door-scroll bounds churn made each stabilized bounds variant count as a fresh room change (new origin, abandoned window, extra completion). The reducer now detects room changes by stage/area/room identity (`ManagedRoomKey.SameRoomAs`) while still refreshing bounds for placement; completion guards compare identity. Regression tests cover bounds-only churn (no extra completion, no abandonment, epoch unchanged, bounds refreshed, pass preserved) and alternating bounds during one crossing. Live verification after `reload_mod`: east walk crossing and west hop crossing each produced exactly completed+1/passed+1, nine commanded pixels, moved, settled, zero abandonments — including the continuous-P2-hold case that previously produced +3.
- Route evidence accepted (2026-08-20 through 2026-08-21): official `coop-transition-west` v6 passed (walk-first threshold sequence, exact checkpoint contract, neutral cleanup verified). Three independent physical-keyboard `coop-route-25` runs passed all 25 alternating 140/220 transitions in 373, 330, and 321.5981476 seconds with exact completion/pass/reconstruction/current-window pixel evidence, zero abandonment/reconstruction failures, and verified cleanup, completing the required three-route evidence. The third artifact is `campaign-20260821T014103613Z-3b409ee856d06450`, from process 985721, session `62bd6e2a0422451f81ae8bf03ff07cec`, diagnostic generation 0, parent build `0.2.0+2206c03ca0e46280e244ed5c2815d99376ce402a`, and co-op `0.4.0` including native-load bootstrap through `56df1c6`; cleanup was attempted, succeeded, and verified. A campaign-side contract defect discovered during the earlier runs was also fixed: `postTransitionCommandedPixels` is a per-window counter that resets at each completion, so the observer requires current pixels >=8 instead of an invalid monotonic delta; a regression test covers reset-before-pass behavior.
- Native EXP observation contract: production now gates on available Play-state Alucard memory in Marble Gallery, reads status offset `0x288` as unsigned 32-bit and safely promotes it to `long`, returns no sample on unavailability or faults, performs no writes, and closes or cancels every drop window. Focused fake-source tests cover every gate, read faults, and `uint.MaxValue`; live EXP advancement was observed, but an accepted associated-drop artifact is still required.
- Combat publication/observer correction (2026-08-21): native attacks now publish camera-locked screen-space coordinates; contact geometry uses standing-body offset Y 1 and half-height 20; generation ordering plus deferred cleanup keeps the exact-owned entity alive through the next `HitDetection`. One-shot attribution requires exactly one pre-captured target, its slot becoming empty, positive EXP, and exactly one kill increment. Contact/projectile scenario v4 uses reset `none` because diagnostic reset invalidates the curated P2 placement.
- Combat live evidence (2026-08-21): attacks killed the curated target and native EXP/kills advanced while the session exposed and corrected observer defects. After the final correction, room 52 scene instability and exact-one-target activation prevented an accepted contact, projectile, or drop artifact. The deterministic private setup card was not committed. Full validation is deferred to a separate run.
- Remaining blockers: accepted contact/projectile/drop artifacts, mod-enabled native save, mod-disabled restart/load, and a human-played 60-minute soak remain. M5 therefore stays In progress; native reward advancement and post-retry policy observation do not complete those gates.
- Acceptance criteria: three cold-launch end-to-end runs; 25 consecutive representative transitions; native contact/projectile hit and drop evidence; zero leaked attacks, quarantine, restoration failure, or protected-state mutation; 15-to-20-minute route; 60-minute soak; restart without mod preserves save validity.
- Focused tests: M3 scenarios plus release matrix and soak.
- Rollback/recovery: unsupported environments suspend Player 2 and leave Player 1 gameplay intact.
- Proposed commit boundary: stabilization changes grouped by subsystem, followed by a release documentation update.

## M6: Broader Traversal, Hazard, and Awareness Coverage

- Outcome: reusable support replaces CEN-only and static-terrain assumptions.
- Scope: projectile terrain collision, terrain classifications, canonical incoming-hit events, deterministic arbitration, target resolver, overlay/helper catalog, hazard manifest, and independent render gating.
- Exclusions: silently approximating unknown hazards and advertising Player 2 to enemies whose attacks cannot affect Player 2 correctly.
- Acceptance criteria: published compatibility matrix; supported projectiles obey world collision; one target is used consistently across helper queries; downed Player 2 is excluded; unsupported hazards suspend visibly; representative overlays pass scenarios.
- Proposed commit boundary: one terrain/hazard/awareness family at a time.

## M7: Shared-World Event Ledger and Keyframes

- Outcome: native side effects have explicit host-ready authority and idempotency.
- Scope: versioned events for attacks, hits, defeat, pickup, reward, boss, progression, revive, and transitions; event IDs; room epochs; application rules; keyframe serialization.
- Exclusions: transport and undocumented native save writes.
- Acceptance criteria: duplicate and reordered events do not duplicate consequences; simultaneous interactions resolve deterministically; boss and unique-reward policy is explicit; a keyframe recreates co-op state without stale native slot identities.
- Proposed commit boundary: contracts and tests before integrations.

## M8: Host-Authoritative LAN Movement

- Outcome: a remote Player 2 can move under host authority and converge after network faults.
- Scope: version handshake, bounded input frames, snapshots, sequence/ack handling, jitter buffer, host correction, visual smoothing, disconnect policy, and network impairment tests.
- Exclusions: rollback, lockstep, combat authority, host migration, and independent rooms.
- Acceptance criteria: protocol/build incompatibility fails clearly; stale or malformed input is rejected; movement remains bounded under the agreed latency/loss envelope; transitions and disconnect clean all transient state; host state always wins.
- Proposed commit boundary: protocol, transport probe, then integration in separate reviewable changes.

## M9: Networked Combat, Progression, and Reconnect

- Outcome: the supported route synchronizes authoritative world events and reconnects safely.
- Scope: host-generated combat/world events, event deduplication, state hashes, keyframe repair, reconnect, and supported-route integration tests.
- Acceptance criteria: clients cannot directly mutate target/progression state; duplicate/reordered packets cannot duplicate consequences; packet loss converges; reconnect during ordinary play, transition, downed state, attack, and supported boss encounters has a defined result.
- Proposed commit boundary: combat, progression, and reconnect as separate coherent integrations.

## Parallel Execution Map

| Wave | Owner | Work | Exclusive boundary | Serialize with |
| --- | --- | --- | --- | --- |
| 1 | Tooling subagent | M1 repository/build discovery | Test projects and workflows | Coordinator owns plan/docs |
| 1 | Runtime subagent | M2 diagnostic-boundary research only | Runtime diagnostic APIs | No edits until M1 boundary is known |
| 1 | Test subagent | M3 scenario design research only | MCP contracts and test fixtures | No edits until M2 contract is selected |
| 2 | Coordinator | Integrate M1, then M2 | Shared projects, manifests, central registration | All agents |
| 3 | Separate subsystem owners | M3 and pure parts of M4 | Runner versus isolated state-machine files | Coordinator for shared contracts |
| 4+ | Coordinator | Integrate in dependency order | `README.md`, `ROADMAP.md`, `DEVPLAN.md`, manifests | All agents |

## Progress Update Protocol

Every implementation agent must:

1. Read `README.md`, `ROADMAP.md`, and this document before editing.
2. Inspect branch, HEAD, status, and relevant repository boundaries.
3. Mark exactly one milestone `In progress` before implementation.
4. Add a dated entry to the execution log after material research, implementation, verification, blockage, or scope change.
5. Record exact validation commands and concise results in the milestone evidence.
6. Mark a milestone `Complete` only after all required acceptance criteria and documentation pass.
7. Keep a blocked milestone `In progress` and state the blocker and next safe action.
8. Update `ROADMAP.md` only for outcome, scope, or release-sequence changes.
9. Update `README.md` when current behavior, controls, diagnostics, compatibility, or user-facing limitations change.
10. Inspect the full diff and status before reporting completion; keep unrelated work untouched.
11. Never commit or push unless the user explicitly requests it.

## Execution Log

| Date | Milestone | State | Evidence/change | Next action |
| --- | --- | --- | --- | --- |
| 2026-08-19 | M0 | Complete | Research synthesized into roadmap, devplan, and fresh-agent prompt | Start M1 discovery and implementation |
| 2026-08-19 | M1 | In progress | Three read-only lanes confirmed a nested-repository source gate using exact public host revisions, real `ModCompiler`, strict manifest/version/hook checks, and no generated game code | Implement and run the gate; record exact evidence |
| 2026-08-19 | M1 | Complete | Added isolated `.validation` projects, exact-revision CI, recursive runtime-source checks, emitted-hook validation, bounded public-source materialization, strict contracts, and co-op-owned runtime outputs; current and `v0.4.3b` passed | Begin M2 with separate parent/nested ownership discovery |
| 2026-08-19 | M2 | In progress | Started structured `P2D4` model/parser, runtime provider, and automation-boundary research in three read-only lanes | Resolve ownership and implement the smallest complete typed-diagnostics slice |
| 2026-08-19 | M2 | In progress | Research selected a nested typed codec/envelope/provider slice; full MCP retrieval requires coordinated dirty RecompOne and parent roots | Implement and validate nested slice, then stop at the cross-root gate if coordination remains unavailable |
| 2026-08-19 | M2 | In progress | Nested slice completed after correcting valid failure invariants and cold dynamic-compile dependencies; 20/20 contracts and both API surfaces pass; independent review found no nested blocker | Obtain coordination for dirty RecompOne/parent roots, then implement registry and MCP retrieval |
| 2026-08-19 | M4 | In progress | Started three-lane research for the first pure state-machine/replay/fault-injection slice while M2 waits on cross-root coordination | Select and implement the smallest behavior-preserving extraction |
| 2026-08-19 | M4 | In progress | Selected movement replay identity over health or attack extraction: room epoch, immutable input/snapshot, canonical bytes, deterministic hash, and game-free replay tests | Implement narrow live adapter and validate both runtime surfaces |
| 2026-08-19 | M4 | In progress | Replay-identity slice completed after fixing reset/update identity and reset-layer ordering; 13/13 tests, golden hash, both APIs, and independent review pass | Begin pure managed health/damage/timer extraction |
| 2026-08-19 | M2 | Blocked | User chose not to edit dirty RecompOne/parent roots; nested structured contract remains complete and preserved | Continue co-op-only M4 slices until cross-root coordination is reopened |
| 2026-08-19 | M4 | In progress | Managed health and immutable revive-observation slices completed: 13/13 tests, full-state deterministic replay, reachable timer/revive boundaries, both API compilers, and review pass | Begin exact-owned attack lease/quarantine reducer without moving native memory operations |
| 2026-08-19 | M4 | In progress | Exact-owned attack lease reducer completed after command hardening: 11/11 fault tests, revision/tuple authorization, partial-clear retries, terminal no-mutation, both API compilers, and review pass | Begin incoming contact opportunity/arbitration reducer |
| 2026-08-19 | M2 | Complete | Dirty overlap resolved additively: unload-safe convention provider, protocol `1.1`, serialized bounded capture/reset, 31/31 parent tests, 57/57 co-op tests, both compatibility compilers, and final review pass | M3 is unblocked; continue selected M4 slice unless reprioritized |
| 2026-08-19 | M4 | In progress | Extracted managed stance policy with atomic standing-hull commands; 12/12 explicit-source tests, shell syntax, full focused gate, and current/pinned dynamic compilation pass | Continue the next requested pure-state slice |
| 2026-08-19 | M4 | In progress | Contact arbitration completed after removing hot-path clones: 15/15 tests, generated sequences, zero-byte warmed scan/suspend loop, both APIs, and review pass | Extract jump-forgiveness timing |
| 2026-08-19 | M4 | In progress | Jump timing completed with machine-bound nonwrapping continuations; replay hashing moved to allocation-free span writing without changing its 115-byte golden schema | Extract managed stance policy |
| 2026-08-19 | M4 | In progress | Reconstruction policy completed with exact 80-candidate order and shared fake/runtime orchestration; 15/15 tests and review pass | Run integrated gate and select locomotion/animation or session lifecycle next |
| 2026-08-19 | M4 | Complete | Final schema-v2 replay closure compares 339 deterministic frames and 308 snapshots twice, proves frame-4 input divergence, and allocates zero bytes over 50,000 warmed cycles | Harden production adapter/reset fault boundaries and run final gate |
| 2026-08-19 | M4 | Complete | Prepared reconstruction and diagnostic reset close partial-commit/ownership loss; publication fake memory, direct guest dispatch, 39-word context guards, and exhaustive scratch restoration close adapter faults; 196 focused tests and both 192000-byte runtime compilers pass | Begin M3 bounded scenario schema and fake-client runner |
| 2026-08-19 | M4 | Complete | README, DEVPLAN, and handoff reconciled to schema v2, final reducer boundaries, validation evidence, and residual M5/M6 live risks | Begin M3 |
| 2026-08-19 | M3 | In progress | Selected a parent-owned strict `sotn-scenario/1` JSON parser with closed typed game/diagnostic predicates, validated input masks, aggregate bounds, and closed artifact policy; schema tests cover canonical and malformed contracts | Add the fake-client runner, cleanup, failure bundles, and cataloged MCP tool |
| 2026-08-19 | M3 | In progress | Added the fakeable execution core with exact diagnostic reset identity, frame-based checkpoints, ordered dual-port input, bounded structured outcomes, indeterminate interruption handling, and independent verified cleanup; focused Release runner tests passed 14/14 and the full parent Release suite passed 66/66 with no warnings | Add persisted artifact bundles and the cataloged MCP tool in later slices |
| 2026-08-19 | M3 | In progress | Added the server-rooted atomic artifact orchestrator, sanitized build/process/mod/diagnostic identity manifest, shared PNG validation, bounded state/diagnostic/entity/log/screenshot capture, production scenario facade, and failure-preserving capture/write errors; focused runner/artifact tests passed 19/19 and the full parent Release suite passed 71/71 with no warnings | Add the cataloged MCP scenario tool in slice 4 |
| 2026-08-19 | M3 | In progress | Added embedded-only `coop-locomotion-jump` version `1`, strict catalog loading, confirmed closed-world `sotn_run_scenario`, nonblocking single-run/direct-mutation gate, exact 64-frame dual-port policy, boundary tests, and artifact/privacy docs; local verification is blocked before compile because this host has only .NET SDK 8/9 for the net10.0 projects | Coordinator reruns focused/full Release tests and MCP Release build with .NET 10, then performs the controlled live smoke before marking M3 complete |
| 2026-08-19 | M3 | In progress | Live smoke reached processed Port 2 `PadRead`/`Pressed2` with no controller but movement stayed gated by `Connected2`; added a configured-mode processed-input availability latch with lifecycle resets and game-free policy coverage, and replaced unavailable `PlayerHasControl` startup telemetry with robust `H=P` hook diagnostics | Run focused/full validation and repeat the controlled live scenario |
| 2026-08-19 | M3 | In progress | Processed Pad 2 latch policy passed 7/7, parent catalog tests passed 4/4, and the full co-op gate passed 203/203 plus current and pinned runtime compilation; dependency builds retained their existing nullable/unused-field warnings | Repeat the controlled live scenario to confirm `M=P` without a physical controller |
| 2026-08-19 | M3 | Complete | Managed launch loaded `coop-feasibility` `v0.4.0`; exact generation reset and the canonical Port 2 sequence produced `M=P:18/18/1`, `H=P`, `E=0`, and normal-jump-only `J=W:N1/C0/B0/R0,0`; private bounded captures completed and explicit clear left both ports neutral | Run final parent, host, and co-op integration gates |
| 2026-08-19 | M3 | Complete | Final review made scenario reset destructive, replaced check-then-act mutation races with full-operation leases, bounded cancellation evidence, and accounted for later diagnostic frames; final integration passed 88/88 parent Release tests, zero-warning full host Release build, 203/203 co-op tests, and current/pinned 192512-byte dynamic compilation | Begin M5 bounded playable local release |
| 2026-08-19 | M5 | In progress | Selected one coherent release-evidence architecture: closed scalar `p2d4/2` metrics over the unchanged legacy line/fields, exact-owned bounded attack census and kind-specific evidence, protocol `1.2` atomic input batches, exact `sotn-scenario/2` typed predicates/reset policy, a bounded embedded descriptor catalog, and a public data-only candidate route manifest | Implement contracts and instrumentation across co-op and parent, then run all Release gates; keep live-only acceptance blockers explicit |
| 2026-08-19 | M5 | In progress | Implemented and documented the release-evidence foundation. Parent Release passed 101/101; MCP Release built with zero warnings; full host Release built with two unchanged warnings; co-op passed 204 tests and current/pinned 213504-byte dynamic compiles. No live pass was invented and drop/reward observation remains absent. | Use live MCP to rerun canonical v2, qualify NO0 9->10, then execute curated kind-specific combat/revive probes before route/transition/cold-launch/save/soak work |
| 2026-08-19 | M5 | In progress | Corrected Marble Gallery scenario/route facts and implemented deterministic warning/resistance/reconstruction/suspension policy, visible unsafe-state GP0 status, transition duration/abandonment evidence, and strict game-free 25-transition aggregation; all focused/full Release gates pass | Live-run canonical v2, exact room-9 near-exit transition v2, curated combat probes, then collect consecutive route transition and longer release evidence |
| 2026-08-20 | M5 | In progress | Closed slice-3 review blockers, added integer metric deltas, corrected every catalog probe, integrated a read-only fixed-bound NO0 drop observer, and added parent-owned 25-transition ingestion; 227 co-op and 114 parent tests pass with both 271872-byte compatibility compiles, zero-warning MCP, and five-warning host build | Live-qualify in the documented MCP order; do not treat no-drop RNG attempts as failures or evidence completion |
| 2026-08-20 | M5 | In progress | Added persisted safe input preference, exact embedded route/soak background observers, lifetime mutation gate, private bounded resilient artifacts, strict release matrix, docs, and fake-clock/failure/cancellation/shutdown tests; 228 co-op and 123 parent tests pass, both compatibility compiles remain 271872 bytes, MCP has zero warnings, and host retains five existing warnings | Restart MCP to discover tools, then execute the documented human-approved cold-launch/route/combat/save/soak sequence; do not claim live gates from unit evidence |
| 2026-08-20 | M5 | In progress | Closed final review blockers for campaign neutrality, transient combat, durable pass finalization, strict diagnostics, shutdown completion, target-overflow causality, Unix permissions, route fingerprinting, truthful target probes, manifest accuracy, and preference behavior; 135 parent and 229 co-op contracts pass with current/pinned 273920-byte compiles | Perform one OpenCode restart, then execute only the documented human-approved live sequence; M5 remains blocked on live evidence |
| 2026-08-20 | M5 | In progress | Three pre-fix cold launches/canonical v2 passes and natural damage/downing exposed the shared zero-update player-liveness bug; telemetry and revive now use typed lifecycle plus HP/status/control/death-step gates. Parent 145, co-op 232, both 274432-byte compiles, MCP zero warnings, and host five existing warnings pass | Repeat fixed revive live, and do not accept the empty-card session as cold-save evidence |
| 2026-08-20 | M5 | In progress | Post-liveness canonical/telemetry/damage/revive passed live; cancelled soak and wrong-stage route remained non-passes. Stair churn motivated a 30-safe-update bounded retry reducer. Parent 145, co-op 238, current/pinned 282112-byte compiles, MCP zero warnings, and host five existing warnings pass | Live-check retry pacing later; continue route/combat/drop/save/soak gates without claiming cold-save or hour acceptance |
| 2026-08-20 | M5 | In progress | Added cumulative exact-owned attack lifetime enforcement and guaranteed fresh minute-60 sampling; parent 149, co-op 244, current/pinned 284672-byte compiles, MCP zero warnings, and host five existing warnings pass | Continue the open live route/combat/drop/save/retry/soak gates; no restart was requested for this game-free slice |
| 2026-08-21 | M5 | In progress | Third independent `coop-route-25` artifact `campaign-20260821T014103613Z-3b409ee856d06450` passed 25/25 in 321.5981476 seconds with cleanup attempted/succeeded/verified, completing the required 373s/330s/321.6s route set on parent `0.2.0+2206c03ca0e46280e244ed5c2815d99376ce402a` and co-op `0.4.0` through `56df1c6` | Curated combat/drop, save/reload, and soak; do not claim other M5 gates |
| 2026-08-21 | M5 | In progress | Corrected screen-space attack publication, standing contact geometry, next-`HitDetection` lifetime, and exact one-shot reward attribution; live attacks killed a target and advanced EXP/kills, but room instability prevented a final accepted combat/drop artifact | Run full validation separately, then repeat curated contact/projectile/drop; save/reload and soak remain |

## Final Integration Gates

- Integrate in dependency order under one coordinator.
- Run focused tests and owning typechecks for every milestone.
- Verify documentation and compatibility manifests.
- Inspect full and staged diffs separately if a commit is authorized.
- Run justified repository-wide checks without replacing focused gates.
- Confirm rollback/recovery behavior and residual risks.
- Confirm pre-existing or unrelated dirty paths remain untouched.
