# SotN Recomp Multiplayer

An experimental [SymphonyRecomp](https://github.com/BlackLabelHQ/SymphonyRecomp) mod project for adding cooperative multiplayer to Castlevania: Symphony of the Night.

## Project Status

Version `0.4.0` is an experimental, bounded same-room local co-op proxy. It is still a feasibility probe, not complete co-op. Player 1 remains the one native Alucard entity; Player 2 is mod-managed and shares Player 1's room and camera.

The implemented work is divided into six workstreams:

1. **Managed animation and body:** an immutable 43-pose map covering idle, walk, jump rise, fall, landing, crouch enter/hold/exit, standing hurt, compact crouched hurt, attack startup/active/recovery, and downed states. Each pose binds a validated native visual frame to managed timing and a managed hurtbox.
2. **Persistent static-terrain movement:** dense floor, ceiling, wall, and hull sensors; standing and crouched stances; crouch-to-stand clearance; one-way floors; four-update coyote time and landing jump buffering; validated standing/crouched X/Y reconstruction after ordinary room transitions; and bounded tether recovery beside Player 1.
3. **Profile-aware outgoing combat:** Circle creates one equipment-derived contact window; Up+Circle creates one bounded managed straight projectile. Both reuse at most one exact-owned native attack/effect entity in slot `17..47` and let the normal engine perform target collision, damage, death, and reward handling.
4. **Managed incoming combat:** 100 HP, contact damage, 60-update hit invulnerability, 18-update hurt lock, knockback, downed state, and a cooperative revive.
5. **Enemy diagnostics and CEN awareness:** a read-only stage-slot target scan reports candidate/nearest-target data and combat evidence. Only normal Center Cube (`CEN`) distance/side helper return values may experimentally choose a closer, stable Player 2.
6. **Combat presentation:** a deterministic direct-GP0 projectile marker and fixed-screen Player 2 HP/downed/revive/profile HUD render after the avatar without persistent primitives or textures.

The outgoing path deliberately allocates one transient native attack/effect entity. It does **not** allocate a second player entity, spoof or mutate Player 1, call `DealDamage`, or directly write target HP/dead/reward state. Native target damage/death/reward semantics remain active during normal collision, so shared native rewards or progression may change when Player 2 kills an enemy. Incoming damage and enemy diagnostics are read-only with respect to native entities and apply only to managed Player 2 state.

This release has no networking and does not provide a second complete native Alucard context, full equipment combat, independent rooms, or complete game-wide co-op behavior.

## Screenshots

| Independent locomotion | Managed crouch | Player 2 jump |
| --- | --- | --- |
| ![Player 1 and cyan Player 2 standing independently in the Castle Entrance](docs/images/player-two-locomotion.png) | ![Cyan Player 2 using the compact managed crouch pose](docs/images/player-two-crouch.png) | ![Cyan Player 2 jumping beside native Alucard](docs/images/player-two-jump.png) |

## Compatibility

The build targets the US SymphonyRecomp `v0.4.3b` release. It validates collision, the equipment-profile implementation (`GetEquipProperties` at `0x800FE728`), pure attack calculation (`CalcAttack` at `0x800F4D38`), attacker-ID, damage, and enemy-definition addresses plus runtime sprite-table entries, and otherwise fails closed. Experimental awareness is deliberately limited to generated `cen` helper symbols and normal `Stage.CenterCube`.

Use a legally owned US PlayStation copy of Castlevania: Symphony of the Night. Other regions, modified executables, Richter mode, and the prologue are unsupported.

## Pose Mapping

The map below contains all 43 immutable pose entries, in pose-index order. **US native frame** selects validated visual/body sprite data from the game's frame, descriptor, and sprite tables. **Updates** is the mod's managed animation timing; it is not a claim about native Alucard animation timing. Hurtboxes are managed `(offset X, offset Y, half-width, half-height)` values relative to Player 2; the X offset mirrors with facing.

| Pose | Managed state and frame | US native frame | Updates | Managed hurtbox |
| ---: | --- | ---: | ---: | --- |
| 0 | `Idle 0` | `0x7A` | 12 | `(0, 1, 4, 20)` |
| 1 | `Idle 1` | `0x7B` | 12 | `(0, 1, 4, 20)` |
| 2 | `Idle 2` | `0x7A` | 12 | `(0, 1, 4, 20)` |
| 3 | `Idle 3` | `0x7B` | 12 | `(0, 1, 4, 20)` |
| 4 | `Walk 0` | `0x19` | 4 | `(0, 1, 4, 20)` |
| 5 | `Walk 1` | `0x1B` | 4 | `(0, 1, 4, 20)` |
| 6 | `Walk 2` | `0x1D` | 4 | `(2, 3, 5, 13)` |
| 7 | `Walk 3` | `0x1F` | 4 | `(5, -1, 8, 9)` |
| 8 | `Walk 4` | `0x21` | 4 | `(5, -1, 8, 9)` |
| 9 | `Walk 5` | `0x23` | 4 | `(5, -1, 8, 9)` |
| 10 | `Walk 6` | `0x25` | 4 | `(2, 3, 5, 13)` |
| 11 | `Walk 7` | `0x27` | 4 | `(2, 3, 5, 13)` |
| 12 | `JumpRise 0` | `0x65` | 6 | `(5, -5, 6, 12)` |
| 13 | `JumpRise 1` | `0x66` | 6 | `(5, -5, 6, 12)` |
| 14 | `Fall 0` | `0x6E` | 6 | `(0, -3, 4, 16)` |
| 15 | `Fall 1` | `0x6F` | 6 | `(0, -3, 4, 16)` |
| 16 | `Landing 0` | `0x74` | 5 | `(2, 3, 5, 13)` |
| 17 | `Landing 1` | `0x75` | 5 | `(2, 3, 5, 13)` |
| 18 | `Landing 2` | `0x76` | 5 | `(0, 7, 4, 16)` |
| 19 | `Landing 3` | `0x77` | 5 | `(0, 1, 4, 20)` |
| 20 | `Landing 4` | `0x78` | 5 | `(0, 1, 4, 20)` |
| 21 | `CrouchEnter 0` | `0x02` | 2 | `(0, 7, 4, 16)` |
| 22 | `CrouchEnter 1` | `0x03` | 4 | `(0, 13, 4, 9)` |
| 23 | `CrouchEnter 2` | `0x04` | 4 | `(0, 13, 4, 9)` |
| 24 | `CrouchEnter 3` | `0x05` | 4 | `(0, 13, 4, 9)` |
| 25 | `CrouchEnter 4` | `0x06` | 4 | `(0, 13, 4, 9)` |
| 26 | `CrouchEnter 5` | `0x07` | 4 | `(0, 13, 4, 9)` |
| 27 | `CrouchEnter 6` | `0x08` | 4 | `(0, 13, 4, 9)` |
| 28 | `CrouchEnter 7` | `0x09` | 4 | `(0, 13, 4, 9)` |
| 29 | `CrouchEnter 8` | `0x0A` | 4 | `(0, 13, 4, 9)` |
| 30 | `CrouchEnter 9` | `0x0B` | 4 | `(0, 13, 4, 9)` |
| 31 | `CrouchEnter 10` | `0x0C` | 4 | `(0, 13, 4, 9)` |
| 32 | `CrouchEnter 11` | `0x0D` | 4 | `(0, 13, 4, 9)` |
| 33 | `CrouchEnter 12` | `0x0E` | 4 | `(0, 13, 4, 9)` |
| 34 | `CrouchHold 0` | `0x0F` | 255 | `(0, 13, 4, 9)` |
| 35 | `CrouchExit 0` | `0x11` | 3 | `(0, 7, 4, 16)` |
| 36 | `CrouchExit 1` | `0x12` | 3 | `(0, 1, 4, 20)` |
| 37 | `Hurt 0` | `0x9F` | 18 | `(0, -3, 4, 16)` |
| 38 | `AttackStartup 0` | `0x7A` | 8 | `(0, 1, 4, 20)` |
| 39 | `AttackActive 0` | `0x7A` | 4 | `(0, 1, 4, 20)` |
| 40 | `AttackRecovery 0` | `0x7A` | 10 | `(0, 1, 4, 20)` |
| 41 | `Downed 0` | `0x9F` | 255 | `(0, -3, 4, 16)` |
| 42 | `CompactHurt 0` | `0x9F` | 18 | `(0, 13, 4, 9)` |

Durations of 255 are long looping/held managed poses. Landing, crouch entry/exit, and the attack phases are managed one-shots; terminal one-shot poses are held rather than modulo-looped.

## Movement and Reconstruction

- Horizontal movement advances in at most one-pixel substeps. Standing side checks use seven vertical sensors (`24, 17, 9, 1, -7, -14, -21`); crouched checks use five (`24, 17, 9, 5, 1`).
- Vertical floor and ceiling movement uses three horizontal sensors at `-6, 0, 6`, as does grounded refresh. Standing-clearance reconstruction uses a dense 7-by-3 hull; crouched clearance uses a 5-by-3 hull.
- Down enters crouch only while grounded. Releasing Down checks the full standing hull; Player 2 remains crouched and reports stand-blocked if there is not enough clearance.
- Reconstruction waits for three safe updates, tests X offsets `±24, ±32, ±40, ±48` and Y offsets `0, -8, 8, -16, 16`, and validates both standing and crouched hulls plus floor support. It suspends rather than placing Player 2 into an unvalidated candidate.
- If Player 2 becomes more than 256 pixels horizontally or 192 pixels vertically separated from Player 1, tether recovery starts the same validated reconstruction process.
- Collision supports dense static tile checks and one-way floors. Any shaped/slope effect bit (`0xF800`) suspends the collision frame and starts reconstruction rather than pretending that sloped traversal is correct.

This does not implement traversal on slopes, moving platforms/elevators, water or quicksand behavior, inverted gravity, or stage-specific scripted terrain.

## Profile-Aware Outgoing Combat

Circle (`O`) and Up+Circle (`I+O`) share the existing safe 8-update startup, 4-update active pose, and 10-update recovery. At active entry, the mod chooses a stable equipped hand (left when nonempty, otherwise right), validates item range `0..168`, copies the immutable US equipment definition, and calls only the deterministic `CalcAttack` routine when required. It deliberately does **not** call `GetEquipProperties`, because that routine consumes native RNG and recomputes global Player 1 statistics. A bounded guest-stack region is saved, cleared, restored byte-for-byte, verified, and the complete CPU snapshot is restored. The one GTE-dependent item `0x8D`, empty/non-damaging profiles, unsupported pointers, invalid items, or malformed hit-state tuples cancel that attack before publication.

The immutable latched tuple supplies attack, element, enemy invincibility frames, stun frames, native hit state, hit effect, deterministic definition-source field, and item identity. Randomized `GetEquipProperties` output is intentionally not reproduced. The managed profiles are:

| Input | Geometry and lifetime |
| --- | --- |
| Circle | One normal-engine contact window at facing offset `±14,-8`, half-size `12x10`. |
| Up+Circle | One straight facing projectile, half-size `5x5`, speed 4 px/update, maximum 40 windows and 160 px. It is removed on first native hit, timeout/range/screen failure, unsafe lifecycle, transition, or room mismatch. |

There is never more than one projectile or outgoing entity. A projectile keeps one exact-owned slot and native `EntityNull` update while its managed world position advances before each next collision window. Each window deactivates publication fields while mutating, revalidates room/ownership/timing, clears prior hit evidence, captures bounded compatible targets, then publishes entity ID/update/hit state last. It does not pierce.

Allocation keeps the v0.3 quarantine model: marker `0x50324B43`, generation, and room hash at `+0x7C/+0x80/+0x84`; exact ownership before cleanup; valid attacker ID `0..10`; and mutation stop on ambiguous reuse. Normal collision alone may mutate targets and run death/reward behavior. The mod never calls `DealDamage` and never writes target HP, dead flags, or rewards. Compatible breakables remain eligible through centralized target bits `hitboxState & 0x3E`, rather than a hard-coded bit-2 test.

## Enemy Diagnostics and Center Cube Awareness

During safe gameplay, a read-only scan of stage slots `64..191` samples active, nondead, on-screen bodies with target bits in `state & 0x3E`. `EN` reports native and current-profile-compatible candidate samples plus a bounded nearest snapshot: slot, entity ID, enemy ID, HP, and integer P1/P2 screen distances. No candidates is an honest `W/EMPTY`, not failure. Existing native attack hit flags plus per-attacker cooldown increases track target hits; HP/dead observations additionally classify defeated and HP-zero/breakable-like evidence. Nothing in this diagnostic writes targets.

Only generated `GetDistanceToPlayerX_cen`, `GetDistanceToPlayerY_cen`, and `GetSideToPlayer_cen` have post-hooks. They may change only `context.V0`, and only when normal Center Cube gameplay has a stable alive Player 2 and `g_CurrentEntity` is exactly an active, targetable, nondead stage slot `64..191`. P2 is selected only when its squared screen distance plus a 64-unit margin is strictly less than P1's; ties choose P1. Every validation failure preserves native V0. Exceptions preserve V0 and disable only awareness. Menus, transitions, loading, downed P2, other stages, and unstable rooms naturally disarm it.

## Combat HUD and Visuals

The backend-independent `DrawOTag` GP0 path draws the avatar first, then a small deterministic marker for an active projectile, then a fixed-screen HUD inside the canonical 256-pixel stage. Sprite success and off-screen avatar culling do not suppress the HUD. It reuses the exact expected-buffer/ordering-table gates and `WriteStageDrawEnvironment`, and uses only transient GP0 tiles: cyan P2 pip, black frame, HP background/fill, downed tint, revive progress, and profile/active-attack indicator. It allocates no primitives or textures.

## Managed Incoming Damage and Revive

The read-only contact scan examines native stage slots `64..191`. A newly damaging overlap, changed attack phase, or bounded repeat opportunity contributes managed damage; if several occur in one scan, the strongest is used. Attack values are clamped to `1..40`. A hit subtracts from Player 2's 100 managed HP, applies 60 updates of invulnerability, 18 updates of hurt lock, and knockback away from the contact (`±2.5` horizontal and `-3.5` vertical pixels/update initially). A crouched hit uses the compact hurt pose. Zero HP enters downed state.

To revive, keep Player 1 within 24 horizontal and 32 vertical pixels, hold **Player 1 Down + Player 2 Circle** continuously for 120 safe updates, and keep both players compatible, controlled, alive/same-room as required by the probe. Revive returns Player 2 at 50 HP with 120 updates of post-revive invulnerability. Releasing a button or violating a condition cancels progress. Revive proximity does **not** raycast wall occlusion yet.

The scan never calls native Player 1 damage and never writes an incoming entity. Protected Player 1 runtime/status, castle flags/map, and save-workspace regions are fingerprinted before and after every scan.

## Installation

1. Open SymphonyRecomp's `mods` directory.
2. Clone this repository as `coop`:

   ```bash
   git clone https://github.com/mojomast/sotnrecompmultiplayer.git coop
   ```

3. Start SymphonyRecomp and enable **Co-op Feasibility Probe** in the mods menu.
4. Configure **Pad 2** in SymphonyRecomp's input settings only when testing a physical controller.
5. Load an Alucard save in a normal castle room.

SymphonyRecomp compiles the C# files under `source/` at runtime. After `git pull`, restart or reload the mod and confirm `Co-op Feasibility Probe v0.4.0` appears in its settings panel.

## Test Procedure

Use only this diagnostic mod for the first test when possible. The virtual controls are `I/J/K/L = Up/Left/Down/Right`, `U = Cross/jump`, `O = Circle/attack`, and `P = Start` on Pad 2.

1. Leave **Use virtual Player 2 keyboard** enabled unless testing configured controller 2. Load an ordinary room with flat floor, low clearance, walls, a ceiling, a ledge, an ordinary transition, and enemies if possible.
2. Open settings, click **Reset diagnostic**, close the entire Mods window, and release all virtual keys for at least two frames. Optionally run the bounded automatic Right/Left/Cross movement test first.
3. Test idle, walking in both directions, rise/fall, landing, coyote jump, and landing-buffered jump. Verify animation and facing follow Player 2.
4. Hold Pad 2 Down (`K`) to crouch. Test a low ceiling: release Down and verify Player 2 remains crouched while stand-blocked, then move/reconstruct into clearance and verify the standing transition.
5. Exercise the 43 mapped poses where practical. `D` requires all animation states/hurtbox states and all 43 hurtbox poses; `VIS` requires all 43 visual poses. A crouched hit is required for `CompactHurt`; a completed attack supplies all three attack phases; reaching zero HP supplies `Downed`.
6. Test dense static terrain against floors, walls, ceilings, one-way platforms, narrow edges, and both stances. Do not interpret suspension at a shaped/slope tile as slope support.
7. Equip a normal damaging hand item. Tap Pad 2 Circle (`O`) near an ordinary enemy, then use Up+Circle (`I+O`) from farther away. Verify the contact attack has one window, only one projectile exists, its marker moves straight, and it disappears on hit/range/timeout/transition. Confirm `X` reports the latched item tuple, extraction/restore checks, exact cleanup, valid attacker ID, and hit-flag plus target-cooldown evidence. Native rewards may change.
8. Let an ordinary damaging enemy/hazard body hit Player 2. Verify HP decreases, knockback/hurt occurs, and immediate repeated contact is suppressed during hit invulnerability. Take a hit while crouched to exercise compact hurt.
9. Reduce Player 2 to zero HP and verify downed behavior. Start a revive and deliberately release a button once to record a cancellation. Then place Player 1 within the inclusive `24 x 32` range and hold **Player 1 Down + Player 2 Circle** for 120 uninterrupted safe updates. Verify recovery at `50/100` HP. Walls do not block revive proximity yet.
10. Cross an ordinary room boundary with Player 1. After the room stabilizes, verify Player 2 reconstructs at a validated standing or crouched X/Y candidate, then move Player 2 at least eight commanded pixels so `T` can pass.
11. Force separation beyond the `256 x 192` tether bounds and verify a tether reconstruction is reported rather than an unchecked teleport.
12. Test contact entry, stay, exit, positive attack contact, and magenta tint; check **I saw the Player 2 contact tint** only after seeing it. Spend at least ten seconds in quiet and enemy rooms. Verify `EN=...EMPTY` remains `W`, then records nearest slot/IDs/HP/P1-P2 distances and hit/defeat/HP-zero evidence when targets exist.
13. In normal Center Cube only, approach an ordinary target from opposite sides with P1 and P2. Verify `AW` overrides only while P2 is strictly closer by the margin, ties retain P1, and leaving CEN/menu/transition/downed state disarms awareness without affecting other systems.
14. Verify the fixed P2 HUD remains visible when the avatar is off screen and when the textured sprite succeeds. Check HP fill, downed tint, revive fill, contact/projectile profile indicator, and moving projectile marker; `HU` should show matching eligible/submitted counts.
15. Exercise all seven Pad 2 controls. In physical mode, optionally require and sweep all four analog axes. Check **I can see the Player 2 avatar** only for the independently animated textured avatar, not fallback geometry.
16. Reopen settings, inspect failures/status, click **Copy report**, and send the single `P2D4 ...` line plus any first-error, collision, contact, visual, reconstruction, attack, enemy, awareness, HUD, or health diagnostics.

The complete `HP=P` predicate intentionally requires at least one damage event, one hit-invulnerability suppression, one downing, one revive start, one revive cancellation, and one successful verified revive, ending alive at exactly 50 HP.

## `P2D4` Report Fields

Version `0.4.0` uses the `P2D4` schema. `VER` and `VIS` are deliberately distinct, resolving the old duplicate `V` key. Values below are **illustrative, not captured proof**:

```text
P2D4 VER=0.4.0 ... VIS=P:... X=P:CLEAN:WINDOW/.../P1,42,18,20,2/E2,0,2,0/J8,8 EN=P:S500/N1400/C900/T72,31,7,24,80,36,1/H2,1,0/LTARGET AW=P:C220/O90/S72/LP2:72 HU=P:E640/S640 HP=P:50/100/...
```

| Field | Exact layout and meaning |
| --- | --- |
| `VER` | Unique semantic version key; `0.4.0` for this schema. |
| `H` | Result and `VSync/RunMainEngine/UpdatePlayerEntities/RenderEntities/Pad2Read` callback counts. `P` requires each count ≥60; fatal is `F`, otherwise `W`. |
| `I` | Input result and source. Virtual: `K:-/pad/game/tap/A-`; physical: `C:host/pad/game/tap/Aaxes`. Virtual `P` requires all seven key-downs, key-ups, pad, game, and tap observations, a neutral observation, and zero current virtual output. Physical `P` requires connection and all seven at all four stages, plus four active axes when requested. Otherwise `W`. |
| `K` | Virtual `down/up/Hcurrent-output/Rraw-held/Uraw-seen/Nneutral-observed/SUI-suppression`; `-` in physical mode. |
| `M` | Result and commanded left pixels/right pixels/jump-rise observation. `P` requires ≥8 pixels each way and a measured ≥4-pixel rise. |
| `R` | Result and submitted/eligible render callbacks, user visual confirmation, `DrawOTag` count, and HLE active/ready bits. `P` requires confirmation and ≥60 submitted frames. |
| `N` | Native body-source gate: result and confirmed/captured/eligible/opposite-facing/fallback counts, `/S` consecutive confirmation streak, `/F` opposite facing in that streak, and `/L` latest status. `P` requires confirmation, ≥60 confirmed submissions, streak ≥60, opposite facing in the streak, and `OK`. It gates safe post-stage drawing; Player 2 does not replay or splice this GT4. |
| `B` | Contact result: `/F` scans, `/S` slots scanned, `/E` eligible samples, `/O` overlaps, `/C` current, `/P` peak, `/D` damaging samples, `/I` entries, `/T` stays, `/X` exits, `/R` resets, `/U` resume-grace scans/budget/suspended, `/G` guard checks/failures, `/V` tint confirmation, `/H` current hurtbox, `/Q` differing guard region, `/L` status. `P` requires ≥120 scans, exactly 128 slots and one guard check per scan, entry/stay/exit, a damaging sample, tint confirmation, and no disable/guard failure. Disable or guard failure is `F`. |
| `C` | Collision result and calls/restore failures/rejected corrections/unsupported-terrain suspensions/ground contacts/wall corrections/ceiling corrections, then `/B` solid/empty bits. `P` requires ≥120 calls, zero restore failures, zero unsupported suspensions, solid and empty observations, and at least one ground/wall/ceiling contact. Fatal, collision disable, or restore failure is `F`. |
| `T` | Transition result and passed/completed/layer-event counts, `/R` reconstruction stable updates/attempts/successes/failures, `H` tether recoveries, and `/L` status. `P` requires at least one completed transition, passed=completed, and no pending transition or awaited post-transition movement. Fatal, collision disable, or a current hard no-safe-candidate reconstruction failure is `F`. |
| `S` | Attack/effect pool minimum free slots/minimum longest free run/sample count. Before five samples it is `W`; zero minimum free is `F`; `P` requires ≥4 free and a run ≥2, otherwise `W`. |
| `G` | Safety code plus `E` mod enabled, `S` current safe frame, and `P` proxy initialized bits. This is a state code, not a `P/W/F` predicate. |
| `Q` | Diagnostic generation/proxy-reset requests/completed resets/reset-pending bit. |
| `A` | Automatic-test state (`I` idle, `Q` queued, `R` running, `P` completed, `X` cancelled), sequence frame, and completed runs. |
| `E` | `0` none; `X` caught fatal exception; `Axxxxxxxx` collision API mismatch; `Cvalue` rejected correction; `Qslot` quarantined attack; `ATK` other attack hard failure; `HPn` health invariant failures; `G` contact guard mismatch; `M` unsupported contact memory; `V` independent visual disabled; `EQ` equipment scratch restoration failure; `AW` awareness-local failure. |
| `D` | Managed animation result: locomotion/animation/frame/tick, `/S` animation-state mask, `/F` advanced-state mask, `/Q` completed attack-phase mask, `/P` hurtbox-state mask, `/T` transitions, `/A` frame advances, `/H` hurtbox samples and current shape, `/C` crouched/stand-blocked bits. Locomotion is `0..7` = Idle, Walk, Rising, Falling, Crouched, Attacking, Hurt, Downed. Animation is `0..13` = Idle, Walk, JumpRise, Fall, Landing, CrouchEnter, CrouchHold, CrouchExit, Hurt, CompactHurt, AttackStartup, AttackActive, AttackRecovery, Downed. `P` requires all 14 state and hurtbox-state bits, advance bits for Idle/Walk/JumpRise/Fall/Landing/CrouchEnter/CrouchExit, all 43 hurtbox-pose bits, ≥4 transitions, ≥120 hurtbox samples, and attack phase mask `7`. Fatal is `F`. |
| `VIS` | Independent visual result: current native frame, `/P` 43-bit visual-pose mask, `/H` 43-bit hurtbox-pose mask, `/E` eligible, `/S` submitted, `/R` restore checks/failures, `/X` visual failures, `/L` status. `P` requires user confirmation, the complete visual-pose mask, ≥120 submissions, ≥1 restore check, and `OK`; the hurtbox mask is reported but enforced by `D`. Fatal, visual disable, or restore failure is `F`. |
| `J` | Jump result: `/N` normal, `/C` coyote, `/B` buffered counts, `/R` remaining coyote/buffer windows. `P` requires at least one coyote and one buffered jump. |
| `X` | Outgoing attack result: status, `/T` timer, `/O` owned slot/cleanup-pending, `/Q` exact quarantine tuple, `/A` allocations, `/W` validated normal-engine windows, `/C` cleanups/lifecycle cancellations, `/F` failures/timing failures, `/I` attacker ID/valid, `/G` timing generations, `/R` causal/hit/cooldown/HP-change evidence, `/P` kind/item/attack/element/hit-state, `/E` extraction failures and scratch restore checks/failures, `/J` projectile windows/lifetime. `P` requires a cleaned allocation, valid windows/attacker ID, causal evidence, and no quarantine, hard failure, count error, or restore failure. Multiple projectile windows per one allocation are valid. |
| `EN` | Enemy diagnostic result: `/S` safe scans, `/N` native target-body samples, `/C` cumulative current-profile-compatible samples, `/T` nearest slot/entity ID/enemy ID/HP/P1 distance/P2 distance/current-compatible bit, `/H` native target hits/defeats/compatible HP-zero hits, `/L` status. `P` requires the current nearest target to be compatible. No native candidates is `W` (`EMPTY`), never `F`; fatal is `F`. |
| `AW` | Center Cube awareness result: helper calls, `/O` V0 overrides, `/S` currently chosen slot, `/L` status. At least one override is `P`, no evidence is `W`, subsystem exception/disable is `F`. |
| `HU` | Combat HUD result and eligible/submitted draws. `P` requires ≥60 eligible draws with every eligible draw submitted; fatal is `F`. |
| `HP` | Managed health result: HP/max, `/I` invulnerability, `/K` hurt lock, `/D` applied events/opportunities consumed/invulnerability suppressions/hit-invulnerability suppressions/last damage/slot/element, `/N` downed bit/count, `/R` progress/starts/cancels/revives/verified recoveries, `/F` invariant failures. `P` requires damage, hit-invulnerability suppression, downing, revive start and cancellation, ≥1 revive, recoveries=revives, final alive state at exactly 50 HP, and no invariant failure. Fatal or invariant failure is `F`. |

Unless specified otherwise, `P` means pass, `W` means incomplete/waiting (or a nonfatal capacity warning), and `F` means a latched failure. A `P2D4` line is diagnostic evidence, not proof of universal game compatibility.

## Safety Model

- Player 1 remains the only native player entity. Player 2 movement, stance, timing, HP, hurt, downed, revive, and incoming damage are managed state; Player 1 movement/entity and native damage are not called or directly changed.
- Terrain collision uses a bounded 260-byte (`0x104`) guest-stack scratch footprint during synchronous player update. Every byte is saved, cleared, restored, and verified, and the CPU context is restored. Corrections outside `-64..64`, API mismatch, and shaped/slope effects suspend or disable rather than guess.
- Persistent movement uses one-pixel substeps, dense standing/crouched sensors, clearance checks, floor support validation, and bounded reconstruction candidates. Unsafe game states, transitions, menus/maps, loading, cutscenes, invalid tilemaps, and unsupported characters suspend the proxy.
- Rendering still consumes no persistent primitive or GT4. It validates the native Player 1 body packet only as a post-stage gate, uploads one validated mapped sprite to a transient VRAM rectangle, draws one direct GP0 quad, restores the exact rectangle in `finally`, and verifies restoration every 60 submissions. Invalid tables/layout/CLUT use diagnostic geometry; a restoration verification failure trips the fatal circuit breaker because continuing after uncertain VRAM restoration is unsafe.
- Incoming contact remains read-only over stage slots `64..191`, with before/after fingerprints for protected native state. Only managed HP and motion respond.
- Outgoing combat is the sole intentional native entity mutation: exactly one free slot in `17..47` is transactionally ownership-marked. Contact publishes one window; a nonpiercing projectile reuses that exact slot across bounded windows. Each publication keeps live fields last; exact ownership is required before every mutation/cleanup.
- Native outgoing collision is intentionally permitted to mutate target combat state and run native death/reward/progression semantics. The mod never directly writes target HP.
- Equipment derivation copies the immutable definition and invokes only deterministic `CalcAttack`; it never consumes native RNG or recomputes Player 1 globals. It uses a bounded saved/cleared/restored/verified guest-stack region and restores the CPU snapshot. Invalid tuples cancel before entity publication, while a failed restore trips the circuit breaker. Enemy diagnostics remain read-only.
- CEN awareness changes only helper `context.V0`; it never writes RAM or spoofs P1. Any exception disables only awareness. Projectile/HUD visuals are deterministic transient GP0 tiles with no native weapon-overlay replay or unvalidated sprite frames.
- Processed Pad 2 masks are sampled once per Player 2 update. Virtual input changes only active-low runtime port `1`, replaces rather than merges hardware Pad 2 input, and is released on unsafe states or settings UI.
- Hook exceptions trip a circuit breaker. Attack cancellation/cleanup is attempted without replacing the original error; quarantined ownership remains visible in `X` and `E`.

These checks reduce risk on the exact supported release. They do not prove behavior for modified executables, future SymphonyRecomp versions, every enemy, every hazard, or every room.

## Current Limitations

- Player 1 owns the camera, normal room transitions, menus, dialogue, and cutscenes.
- The 43-pose set is managed and intentionally bounded; it is not the full native Alucard animation state machine.
- Static terrain only: shaped/sloped terrain suspends rather than traverses; moving platforms/elevators, water, quicksand, inverted gravity, and unusual scripted rooms are unsupported.
- Contact scans native stage entities only. Primitive-only hazards and stage-specific nonstandard hazard logic are excluded.
- Enemy awareness is experimental only for three helper calls in normal Center Cube; most enemies/stages still do not target Player 2, and helper users may not consult all three values consistently.
- Outgoing combat derives a bounded tuple from one equipped hand but does not reproduce weapon-specific animations, factories, arcs, consumables, spells, subweapons, transformations, familiars, critical rolls/policies, or overlay behavior.
- Native reward/progression effects from Player 2 kills are shared but do not yet have explicit pickup, boss, duplicate-reward, or progression policy.
- Revive checks range and state but does not raycast wall occlusion.
- Tether recovery exists, but camera policy beyond Player 1 ownership and broader room-exit policy are not implemented.
- The fixed combat HUD has no text/numeric HP, inventory/menu ownership, persistence, networking, rollback, or remote synchronization.
- A configured Player 2 keyboard mapping can make `Connected2` true without a physical second controller, and hot-plugging can change assignment.

## Version History

### 0.4.0

- Introduces unique-key `P2D4` (`VER` and `VIS`) plus enemy (`EN`), Center Cube awareness (`AW`), HUD (`HU`), and profile/projectile evidence.
- Adds bounded read-only target diagnostics over slots `64..191`, nearest-target snapshots, and hit/cooldown-based hit, defeat, and HP-zero/breakable-like evidence; empty rooms remain waiting, not failure.
- Adds CEN-only post-hooks for native X/Y distance and side helpers that can override only V0 for a strictly closer, stable alive P2 and fail subsystem-locally.
- Replaces the fixed attack constants with immutable equipment-definition plus deterministic `CalcAttack` tuples, one-window Circle contact, and one bounded nonpiercing Up+Circle projectile while retaining exact ownership/quarantine semantics without consuming native RNG.
- Adds direct-GP0 projectile presentation and a fixed P2 HP/downed/revive/profile HUD that cannot be suppressed by successful sprite rendering or avatar culling.

### 0.3.0

- Expands the immutable map from 16 to 43 poses, adding landing, crouch entry/hold/exit, standing and compact hurt, fixed attack phases, and downed mappings with managed durations and hurtboxes.
- Replaces sparse movement probes with persistent dense standing/crouched static-terrain sensors, stand-clearance checks, one-pixel substeps, validated X/Y and stance-aware reconstruction, and tether recovery. Shaped/sloped terrain now explicitly suspends.
- Adds one fixed Circle melee using exactly one ownership-marked transient native attack/effect slot in `17..47` for the bounded next normal-engine collision window, preserving native target damage/death/reward semantics.
- Adds transactional attack publication, exact marker/generation/room ownership cleanup, quarantine/reuse protection, validated attacker IDs, timing checks, and hit-flag plus target-cooldown causal diagnostics.
- Adds managed 100 HP, read-only contact damage, hit invulnerability, hurt lock, knockback, compact hurt, downed state, and a 120-safe-update cooperative revive to 50 HP.
- Introduces `P2D3`, expanding `D` and `V`, revising collision and transition diagnostics, and adding outgoing `X` and managed `HP` fields.

### 0.2.0

- Replaces Player 1 frame replay with independent, data-driven `Idle`/`Walk`/`JumpRise`/`Fall` frames resolved from validated US runtime sprite tables.
- Adds an immutable 16-pose mapping that binds each frame's duration and pose-specific hurtbox.
- Renders one textured quad directly through GP0 after the stage ordering table, exactly restores the overwritten VRAM rectangle, and periodically verifies that restoration by complete readback.
- Removes Player 2 GT4 allocation and ordering-table splicing; the completed Player 1 packet is now a read-only post-stage render gate.
- Fails closed on visual table, draw-layout, CLUT, or restore validation failures while retaining diagnostic geometry fallback.
- Adds four-update coyote time and a four-update landing jump buffer.
- Introduces the `P2D2` report schema with independent visual field `V`, jump-forgiveness field `J`, and revised native-source field `N` semantics.

### 0.1.6

- Added a deterministic mod-owned logical locomotion clock and sampled Pad 2 once per simulation update.
- Replaced copied Player 1 hurtboxes with a conservative mod-owned profile.
- Expanded collision scratch protection to the full bounded 260-byte footprint.
- Added `D` diagnostics while retaining `P2D1`; visuals still replayed Player 1.

### 0.1.5

- Added bounded read-only Player 2 contact shadowing against native stage entities and protected-state fingerprints.
- Recorded narrow-gap clipping as a limitation of sparse terrain probes.

### 0.1.4

- Replayed Alucard's completed native textured quad at Player 2 without an entity or persistent primitive.
- Added cyan tint, facing mirroring, and diagnostic geometry fallback.

### 0.1.3

- Added raw/current virtual-key masks and an automatic Right/Left/jump Pad 2 movement test.

### 0.1.2

- Polled virtual keys once per VSync and replaced the HLE-only marker with backend-independent GP0 rectangles.

### 0.1.1

- Added the `I/J/K/L`, `U`, `O`, and `P` virtual Player 2 keyboard and port-2 injection.

### 0.1.0

- Initial physical-controller feasibility probe.

## Architecture Direction

The intended first playable mode is constrained same-room co-op:

- Player 1 remains SOTN's native Alucard.
- Player 2 uses mod-owned movement, health, animation, and combat state.
- Both players initially share the loaded room, camera, castle progression, inventory, equipment effects, experience, and relics.
- Player 1 owns menus, dialogue, cutscenes, and room transitions.
- Player 2 is reconstructed beside Player 1 after transitions.
- Future online play would use host-authoritative input and state snapshots rather than rollback or deterministic lockstep.

Rollback, two complete native player contexts, split-screen, independent rooms, Richter/Maria co-op, and host migration are outside the initial scope.

## Roadmap

1. **Implemented experimentally:** Pad 2 input, independent rendering/restoration, managed poses/hurtboxes, jump forgiveness, terrain collision, room reconstruction, contact shadowing, and entity-capacity diagnostics.
2. **Implemented experimentally in bounded form:** the 43-pose persistent same-room proxy, dense static-terrain standing/crouched movement, transition reconstruction, and tether recovery.
3. **Implemented experimentally in bounded form:** managed damage, invulnerability, knockback, downed state, revive, and the initial exact-owned native collision path.
4. **Implemented experimentally in bounded form:** equipment-derived contact/projectile profiles, read-only enemy diagnostics, CEN-only helper awareness, projectile marker, and fixed combat HUD.
5. Broaden attack/hazard/awareness semantics beyond CEN and define camera, room-exit, pickup, reward, and shared-world progression policies.
6. Add a LAN transport probe, remote avatar snapshots, and host-authoritative movement.
7. Synchronize combat events, pickups, bosses, progression, and reconnect keyframes.

The next honest work is broader enemy/hazard/awareness coverage plus explicit camera and shared-world policy—not a claim that the bounded proxy is already complete co-op.

## Development

The implementation hooks `dra/RunMainEngine`, `dra/UpdatePlayerEntities`, `dra/RenderEntities`, `main/DrawOTag`, and three generated `cen` player-distance/side helpers; observes `VSyncEvent`, `PadReadEvent`, `PlayerLoadedEvent`, and `RoomLayerLoadEvent`; synchronously calls validated terrain-collision, deterministic attack-calculation, and attacker-ID routines; resolves immutable poses through validated US runtime sprite tables; scans native stage geometry through a read-only RAM view; transactionally publishes one bounded native attack/effect entity; and renders one direct GP0 textured avatar plus transient marker/HUD geometry after the stage ordering table.

The source is dynamically compile-checked against the exact SymphonyRecomp `v0.4.3b` APIs and the current local development runtime using .NET 10. Gameplay validation still requires SymphonyRecomp and a legally owned US game copy.

The repository contains no copyrighted game assets or game image.

Do not submit AI-generated issues or pull requests to SymphonyRecomp's upstream repositories. This independent experiment is not affiliated with Black Label HQ or Konami.
