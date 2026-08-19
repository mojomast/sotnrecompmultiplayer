# Multiplayer Mod Roadmap

## Purpose

Turn the current bounded Player 2 feasibility implementation into a trustworthy same-room local co-op mode, then add host-authoritative LAN play without weakening native-state safety.

## Current Baseline

Version `0.4.0` experimentally demonstrates:

- Player 2 input on controller port 2 and a virtual keyboard.
- A persistent mod-managed Player 2 proxy with 43 poses.
- Dense static-terrain movement, crouching, jump forgiveness, tether recovery, and room reconstruction.
- Managed Player 2 health, incoming contact damage, invulnerability, knockback, downing, and revive.
- One exact-owned transient native outgoing attack entity.
- Equipment-derived contact and projectile profiles.
- Native enemy damage, death, breakable, reward, and drop semantics.
- Read-only enemy diagnostics and limited Center Cube target awareness.
- Direct-GP0 avatar, projectile, and Player 2 HUD rendering.
- Fail-closed lifecycle, guest-stack, CPU-context, entity-ownership, protected-memory, and VRAM checks.

This is a bounded same-room proxy, not complete game-wide or online co-op.

## Product Contract

- Player 1 remains the only native Alucard.
- Player 1 owns the camera, menus, dialogue, saves, progression, cutscenes, and room transitions.
- Player 2 movement, health, animation, incoming damage, downed state, and revive remain mod-managed.
- Native targets remain authoritative for HP, death, breakables, rewards, and drops.
- The mod never writes enemy HP or target death/reward state directly.
- Every transient native resource is bounded and validated by exact ownership identity.
- Unsupported or uncertain states suspend Player 2 instead of guessing.
- Online play will be host-authoritative and snapshot-based, without rollback or deterministic lockstep initially.

## Release Sequence

| Release | Outcome | Exit gate | Status |
| --- | --- | --- | --- |
| `v0.4` | Bounded feasibility implementation | Existing compile, automation, and live diagnostic evidence | Complete |
| `v0.5` | Reproducible engineering baseline | Repository-owned compile/tests and automated diagnostic scenarios | Complete |
| `v0.6` | Bounded playable local co-op | Curated route, transition matrix, save-integrity check, and 60-minute soak | Planned |
| `v0.7` | Broader traversal and combat coverage | Published room, enemy, hazard, and renderer compatibility matrix | Planned |
| `v0.8` | Explicit shared-world authority | Idempotent combat, pickup, reward, progression, and keyframe events | Planned |
| `v0.9` | Host-authoritative LAN movement | Stable movement under latency, jitter, loss, disconnect, and transitions | Planned |
| `v1.0` | Networked supported route | Combat/world synchronization and reconnect keyframes | Planned |

## v0.5: Engineering Baseline

Outcomes:

- Replace temporary out-of-repository compile harnesses with repository-owned validation.
- Compile against the current runtime and the supported `v0.4.3b` API surface.
- Parse and validate the `P2D4` diagnostic report as a typed contract.
- Expose structured, read-only mod diagnostics to automation.
- Run data-driven scenarios through the existing dual-controller MCP tooling.
- Record processed input and per-update managed-state hashes.
- Separate testable state and policy from runtime memory/GPU adapters without a broad rewrite.
- Add subsystem-specific failure domains and performance telemetry.

Completion evidence: repository-owned current/pinned compilation, typed `p2d4/1` diagnostics, the 196-test M4 replay/fault subset, the 203-test current co-op gate, and a private passing `sotn-scenario/1` locomotion/normal-jump smoke. Detailed execution evidence remains authoritative in `DEVPLAN.md`.

## v0.6: Bounded Playable Local Co-op

Outcomes:

- Prove attack ownership under transition, unload, pool exhaustion, slot reuse, and exceptions.
- Exercise natural incoming damage, downing, and revive.
- Define camera, tether, transition, pickup, and shared-progression policies.
- Support a curated 15-to-20-minute route.
- Complete at least 25 consecutive representative transitions.
- Complete a 60-minute soak and restart-without-mod save-integrity test.

Recommended initial policies:

| Policy | Initial rule |
| --- | --- |
| Camera | Player 1 remains sole owner |
| Tether | Comfort, warning, resistance, then validated reconstruction |
| Room exits | Player 1 triggers transitions |
| Transition | Suspend Player 2, retire exact-owned attacks, increment room epoch, reconstruct |
| Inventory | Shared Player 1 inventory and a documented designated-hand profile |
| Pickups | Player 1 interacts; native shared consequences remain authoritative |
| Player 2 persistence | Session-only |
| Unsupported terrain | Visible suspension, never speculative correction |

## v0.7: Broader Local Coverage

Outcomes:

- Add projectile terrain collision.
- Support or explicitly classify slopes, platforms, elevators, water, quicksand, and scripted terrain.
- Introduce a canonical incoming-hit identity and deterministic arbitration.
- Replace isolated awareness hooks with a reusable target resolver and overlay/helper catalog.
- Classify hazards as supported, approximate, diagnostic-only, or unsupported.
- Make normal Player 2 rendering independent of a conventional Player 1 body packet.

## v0.8: Shared-World Authority

Outcomes:

- Define versioned events for attacks, hits, enemy defeat, pickup, reward, boss defeat, progression, revive, and transitions.
- Give every event an ID, room epoch, authority owner, bounds, and idempotency rule.
- Define boss and one-time reward policy explicitly.
- Serialize a full co-op keyframe without native transient slot identities.

## v0.9: LAN Movement

Outcomes:

- Client sends bounded, sequence-numbered Player 2 input frames.
- Host simulates authoritative Player 2 state and sends snapshots.
- Client smooths presentation while host corrections apply to simulation state.
- Handshake validates protocol, mod, runtime, region, and compatibility versions.
- Network tests inject latency, jitter, loss, duplication, and reordering.

## v1.0: World Synchronization and Reconnect

Outcomes:

- Host alone generates authoritative combat and progression events.
- Duplicate or reordered packets cannot duplicate kills, drops, pickups, or rewards.
- Mid-attack, mid-revive, and mid-transition loss converges to host state.
- Reconnect safely applies a complete host keyframe.
- State hashes detect divergence and keyframes repair it.

## Deferred Scope

- Rollback networking.
- Deterministic lockstep.
- Two complete native player contexts.
- Split-screen or independent rooms.
- Player 2-owned native menus, saves, or progression.
- Richter or Maria co-op.
- Host migration.
- Player 3 or Player 4.

## Research Basis

- [Game Programming Patterns: State](https://gameprogrammingpatterns.com/state.html)
- [W3C SCXML](https://www.w3.org/TR/scxml/)
- [Linux sequence counters](https://docs.kernel.org/locking/seqlock.html)
- [Gaffer: Deterministic Lockstep](https://gafferongames.com/post/deterministic_lockstep/)
- [Gaffer: State Synchronization](https://gafferongames.com/post/state_synchronization/)
- [OpenTelemetry Logs Data Model](https://opentelemetry.io/docs/specs/otel/logs/data-model/)
- [sm64coopdx](https://github.com/coop-deluxe/sm64coopdx)
- [DevilutionX](https://github.com/diasurgical/DevilutionX)
- [BizHawk](https://github.com/TASEmulators/BizHawk)
- [GGPO](https://github.com/pond3r/ggpo)

Implementation sequencing and live progress are authoritative in [DEVPLAN.md](DEVPLAN.md).
