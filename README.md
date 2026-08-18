# SotN Recomp Multiplayer

An experimental [SymphonyRecomp](https://github.com/BlackLabelHQ/SymphonyRecomp) mod project for adding cooperative multiplayer to Castlevania: Symphony of the Night.

## Project Status

This repository is at the Player 2 feasibility stage. Version `0.1.5` provides a diagnostic proxy that tests the engine paths needed before local or networked co-op can be implemented safely.

This is not a complete co-op mod. It tests the plumbing needed before one can be built:

- Logical controller 2 input from the runtime through SOTN's processed pad state.
- A mod-owned Player 2 position driven by controller 2.
- Basic gravity, jumping, floors, ceilings, walls, and one-way tile collision.
- A tinted Player 2 avatar replayed from Alucard's completed native sprite packet, with diagnostic geometry as a fallback.
- Read-only Player 2 hurtbox overlap detection against native stage entities.
- Recreating the proxy after normal room transitions.
- Read-only pressure measurements for SOTN's player-effect entity pool.

The proxy is managed entirely by the mod. It does not allocate a SOTN entity, damage enemies, change Alucard, alter progression, or write save data. Contact detection observes geometry only and does not invoke SOTN's mutating hit-detection or damage routines.

SOTN has one native player entity and singleton player/status state. The initial implementation therefore uses Player 1 as the native Alucard and develops Player 2 as a separately managed proxy. Simply routing a second controller into the original player routines is not sufficient.

## Compatibility

The initial build targets the US SymphonyRecomp `v0.4.3b` release. It validates the expected collision API pointer before making collision queries and otherwise fails closed.

Use a legally owned US PlayStation copy of Castlevania: Symphony of the Night. Other regions, modified executables, Richter mode, and the prologue are unsupported.

## Installation

1. Open SymphonyRecomp's `mods` directory.
2. Clone this repository as `coop`:

   ```bash
   git clone https://github.com/mojomast/sotnrecompmultiplayer.git coop
   ```

3. Start SymphonyRecomp and enable **Co-op Feasibility Probe** in the mods menu.
4. Configure **Pad 2** in SymphonyRecomp's input settings only when testing a physical controller.
5. Load an Alucard save in a normal castle room.

SymphonyRecomp compiles the C# files under `source/` at runtime.

When updating, run `git pull`, restart or reload the mod, and confirm `Co-op Feasibility Probe v0.1.5` appears in its settings panel.

## Test Procedure

Use only this diagnostic mod for the first test when possible.

1. No second controller is required. Leave **Use virtual Player 2 keyboard** enabled for the default test. If testing hardware instead, disable it and configure controller 2 before starting the game.
2. Load Alucard into an ordinary room with a flat floor, wall, and normal exit.
3. Open the mod settings and click **Reset diagnostic**.
4. For a controller-free smoke test, click **Run automatic P2 movement test**, then close the entire Mods window. The probe sends short Right, Left, and Cross commands only to Pad 2 and returns it to neutral.
5. To test manual virtual controls, close the entire Mods window and wait two frames so this mod's UI suppression expires.
6. Press and release `I`, `L`, `K`, `J`, `U`, `O`, and `P`. These inject Up, Right, Down, Left, Cross, Circle, and Start respectively into SOTN controller port 2.
7. Use `J`/`L` to move the cyan-tinted Player 2 avatar and `U` to jump. The avatar intentionally mirrors Player 1's current animation frame in this rendering probe, but its position and facing follow Player 2. These keys should not be bound to Player 1.
8. In physical mode, press the equivalent controller buttons, enable **Require analog test**, and move both sticks through their full ranges.
9. Test the proxy against a floor and wall, then jump into a reachable ceiling. Test a one-way platform if available. `C` remains waiting until floor, wall, and ceiling behavior have all been observed.
10. Use Player 1 to cross a normal room boundary. After the next room settles, move Player 2 in both directions and allow at least 60 uninterrupted frames before reopening settings.
11. In a room with an ordinary enemy or hazard, move Player 2 into its visible body and remain there briefly. The Player 2 avatar should change from cyan to magenta while overlapping, then return to cyan after leaving. Player 2 cannot take damage in this probe.
12. Confirm the contact test did not change Player 1's HP or the enemy's normal behavior. Player 1 should still be able to damage the enemy normally.
13. Spend at least ten seconds in both a quiet room and an enemy/effect-heavy room.
14. Reopen the mod settings and check **I can see the Player 2 avatar** only if the cyan-tinted native sprite was rendered correctly rather than the rectangular fallback.
15. Check **I saw the Player 2 contact tint** only if you observed the magenta contact tint appear and clear.
16. Click **Copy report** and paste the single `P2D1 ...` line back to the developer. **Print report to console** is available as a fallback; use SymphonyRecomp's Console panel or the terminal that launched it.
17. Also paste any **First error**, **Collision disabled**, **Contact disabled**, or read-only guard failure shown in the panel and describe any visual or collision anomaly.

If `B=F` or `E=G`, copy that failure report before resetting. **Reset diagnostic** clears the latched contact guard for a controlled retry.

The settings panel reports which required controls have reached each stage relevant to the selected physical or virtual input mode.

## Report Fields

Example:

```text
P2D1 V=0.1.5 H=P:900/880/860/860/2700 I=P:K:-/7/7/7/A- K=7/7/H0000/R0000/U7860/N1/S0 M=P:18/24/1 R=P:600/610/1/D900/H00 N=P:600/600/600/120/0/S600/F1/LOK B=P:F600/S76800/E900/O45/C0/P1/D45/I1/T43/X1/R1/G600,0/V1/H0,1,4,20/Qnone/LOK C=P:4512/0/0/350/4/1/B11 T=P:1/1/3 S=P:17/6/860 G=OK:E1S1P1 Q=1/1/1/0 A=P:64/1 E=0
```

| Field | Meaning |
| --- | --- |
| `H` | Hook result and VSync/engine/player-update/render/port-2-pad callback counts. |
| `I` | Input result and source: `K` virtual keyboard or `C` configured controller, followed by host/pad/game/tapped stages and analog-axis count. |
| `K` | Virtual key-down/key-up counts, output/raw/current-union masks, neutral observation, and UI suppression frames. `-` means physical-controller mode. |
| `M` | Proxy movement result and left/right pixel distance plus jump observation. |
| `R` | Rendered avatar frames/eligible callbacks, visual confirmation, `DrawOTag` callbacks, and HLE active/ready bits. |
| `N` | Native Player 1 frame replay submissions/captures/eligible frames/facing flips/fallbacks, current consecutive native-frame streak, whether that streak includes a facing flip, and latest sprite status. |
| `B` | Read-only body-contact scan frames/slots/eligible samples/overlaps/current/peak/damaging samples/entries/stays/exits/resets, guard checks/failures, visual confirmation, mirrored hurtbox, guard region, and status. |
| `C` | Collision result, calls/restoration failures/invalid corrections/ground/wall/ceiling contacts plus solid/empty observation bits. |
| `T` | Transition result and passed/completed/layer-event counts. |
| `S` | Player-effect slot pressure result, minimum free slots/minimum longest free run/sample count. |
| `G` | Safety-gate code plus enabled/safe/proxy-initialized bits. This identifies why active tests have not started. |
| `Q` | Diagnostic generation, proxy reset requests, completed resets, and pending-reset bit. |
| `A` | Automatic movement state, sequence frame, and completed-run count. `P` means the sequence returned Pad 2 to neutral. |
| `E` | `0` for none, `A` for API mismatch, `C` for rejected collision data, `G` for contact-guard mismatch, `M` for unsupported contact memory access, or `X` for a caught exception. |

`P` means pass, `W` means waiting or warning, and `F` means failure.

Pass thresholds are deliberately conservative:

- `H` needs at least 60 callbacks from each required hook/event.
- `I` in virtual mode needs all seven key-downs/key-ups plus all seven buttons at pad/game/tapped stages. Physical mode needs all seven buttons at all four stages and, when selected, all four analog axes.
- `M` needs at least eight commanded pixels in both horizontal directions and a measured four-pixel jump rise.
- `R` needs at least 60 rendered proxy frames plus the tester's visual confirmation.
- `N` needs at least 60 consecutive translated native sprite packets, a facing flip during that streak, an `OK` latest status, and the tester's visual confirmation.
- `B` needs at least 120 guarded scans, exactly 128 examined stage slots per scan, observed contact entry/stay/exit, at least one positive-attack contact sample, zero guard failures, and visual confirmation of the contact tint.
- `C` needs at least 120 calls, intact scratch restoration, solid and empty samples, and observed ground, wall, and ceiling contacts.
- `T` needs a changed stable room identity and post-transition Player 2 movement for every counted transition.
- `S` needs at least five samples, at least four free attack/effect slots, and a contiguous run of at least two.

## Safety Model

- Collision output uses temporary guest stack storage only during a synchronous game-update hook.
- Every byte used for collision output is restored and verified after each call.
- The expected `v0.4.3b` collision function pointer, nested collision-table pointer, and tilemap dimensions must validate before active tests run.
- The proxy consumes no game entity or persistent primitive-pool slot. Native sprite replay uses one bounded transient GT4 packet in frames where the normal Alucard body packet is available.
- Entity capacity is read only.
- Menus, maps, cutscenes, loading, special transitions, unsupported characters, and invalid tilemaps suspend the proxy.
- Hook exceptions stop active probing and are retained in the settings panel instead of being retried every frame.
- Virtual controls mutate only the active-low `PadReadEvent` for runtime port `1`, which is SOTN controller port 2. Port 0 and Player 1 input are not modified.
- Virtual mode replaces incoming Pad 2 state rather than merging with configured hardware, so its report cannot pass using a physical controller accidentally.
- Opening the settings panel, loading, entering menus, or entering cutscenes releases and suppresses virtual input. **Release virtual keys** provides a manual recovery action.
- Player 2 copies only Player 1's completed transient sprite command, translates it into the proxy's screen position, optionally mirrors its geometry for Player 2 facing, tints it cyan, and splices it directly after Player 1 in the current ordering-table chain. It never changes Player 1's entity or source sprite command body.
- Contact scanning receives only the runtime's read-only RAM span, examines exactly native stage slots 64 through 191, and reproduces only the geometric player-contact test. It does not call native hit detection, damage code, update functions, or entity allocation.
- Every contact scan fingerprints all entities and selected protected regions covering Player 1 runtime state, status/inventory, castle flags, castle map, and the serialized save workspace before and after the synchronous read-only operation. A mismatch permanently disables contact scanning until diagnostic reset.
- Contact changes only Player 2's copied packet or fallback geometry tint from cyan to magenta. Player 1's packet, enemy fields, hit flags, HP, cooldowns, progression, and saves are not changed.
- If a normal Alucard sprite packet is unavailable, the diagnostic marker is emitted directly through PSX GP0 rectangle commands after the stage ordering table.
- The automatic movement test is explicit, cancellable, bounded to 64 VSyncs, sends only Right/Left/Cross, aborts when gameplay becomes unsafe, and finishes with neutral Pad 2 input.

These checks reduce risk on the exact supported release but cannot prove compatibility with modified executables or future SymphonyRecomp versions. The collision model is intentionally incomplete. A pass establishes that a controlled, rendered, terrain-aware proxy can be maintained; it does not establish full Alucard physics, combat, enemy targeting, moving platforms, elevators, transformations, spells, familiars, or completed co-op.

## Current Limitations

- Player 1 owns the camera and room transitions.
- The proxy replays Player 1's current Alucard frame rather than owning an independent animation state.
- The proxy also mirrors Player 1's current decoded hurtbox shape because it does not yet own independent animation state.
- Collision is based on tile probes and does not include moving entities or hazards.
- Sparse terrain probes can miss narrow solid sections around closely spaced empty tiles; collision coverage will be refined in a dedicated movement milestone.
- Slopes, stair steps, water, quicksand, inverted gravity, and unusual scripted rooms need separate testing.
- No Player 2 HP, combat, inventory, menu, revive, or persistence exists yet.
- Contact shadowing covers native stage slots 64 through 191 and does not include scripted primitive-only hazards or stage-specific nonstandard collision logic.
- A configured Player 2 keyboard mapping can make `Connected2` true without a physical second controller.
- Hot-plugging can change controller assignment.
- Virtual keyboard injection validates SymphonyRecomp's BIOS packed-pad path; it does not claim to test every possible direct-buffer controller API.

## Version History

### 0.1.5

- Adds a bounded read-only contact shadow for Player 2 against native player-contact stage entities.
- Mirrors Player 1's current decoded hurtbox shape at Player 2's position and independent facing.
- Tints Player 2's copied sprite packet or fallback geometry magenta during contact and tracks contact entry, stay, and exit.
- Fingerprints protected gameplay and persistent-state regions around every synchronous scan and fails closed on any mismatch.
- Records narrow-gap clipping as a known limitation of the current sparse terrain probes.

### 0.1.4

- Replays Alucard's completed native textured quad at the Player 2 proxy without allocating an entity or persistent primitive.
- Tints Player 2 cyan and mirrors the copied geometry when Player 2 faces the opposite direction.
- Retains backend-independent diagnostic geometry as a fail-safe and reports native capture, submission, flip, and fallback counts.

### 0.1.3

- Removes the unreliable global ImGui keyboard-capture gate while retaining this mod's own settings suppression.
- Adds raw/current virtual-key masks to the report.
- Adds an explicit automatic Right/Left/jump Pad 2 movement test requiring no controller or keyboard input.

### 0.1.2

- Polls virtual keys once per VSync instead of depending on accepted keyboard events.
- Replaces the HLE-only `GpuPrims` marker with backend-independent PSX GP0 rectangles.
- Reports stage ordering-table calls and HLE backend state.
- Clarifies that the entire Mods window must be closed while testing virtual keys.

### 0.1.1

- Adds a virtual Player 2 keyboard using `I/J/K/L`, `U`, `O`, and `P`.
- Injects those controls into SOTN controller port 2 before the game creates `Pressed2` and `Tapped2`.
- Removes an invalid player-entity update-pointer gate that prevented native Alucard gameplay from ever reaching a safe diagnostic frame.
- Adds safety-gate and proxy-reset state to the pasteable report.

### 0.1.0

- Initial physical-controller feasibility probe.

## Architecture Direction

The intended first playable mode is constrained same-room co-op:

- Player 1 remains SOTN's native Alucard.
- Player 2 uses mod-owned movement, health, animation, and combat state.
- Both players share the loaded room, camera, castle progression, inventory, equipment effects, experience, and relics initially.
- Player 1 owns menus, dialogue, cutscenes, and room transitions.
- Player 2 is reconstructed beside Player 1 after transitions.
- Online play will use host-authoritative input and state snapshots rather than rollback or deterministic lockstep.

Rollback, two complete native player contexts, split-screen, independent rooms, Richter/Maria co-op, and host migration are outside the initial scope.

## Roadmap

1. Validate controller 2, rendering, terrain collision, room reconstruction, and entity capacity with this probe.
2. Replace the diagnostic marker with a persistent local Player 2 proxy and improved movement.
3. Add native-compatible outgoing attack hitboxes and managed Player 2 damage/downed behavior.
4. Add camera tethering, ordinary transition rules, HUD, and shared-world policies.
5. Add a LAN transport probe, remote avatar snapshots, and host-authoritative movement.
6. Synchronize combat events, pickups, bosses, progression, and reconnect keyframes.

Each phase depends on runtime test results from the previous phase.

## Development

The implementation hooks `dra/RunMainEngine`, `dra/UpdatePlayerEntities`, `dra/RenderEntities`, and `main/DrawOTag`; observes `VSyncEvent`, `PadReadEvent`, `PlayerLoadedEvent`, and `RoomLayerLoadEvent`; calls SOTN's generic terrain-collision API through `0x8003C7BC`; scans native stage contact geometry through a read-only RAM view; and renders with a translated native transient GT4 packet or backend-independent GP0 fallback geometry.

The source is dynamically compile-checked against the exact SymphonyRecomp `v0.4.3b` APIs and the current local development runtime using .NET 10. Gameplay validation still requires SymphonyRecomp and a legally owned US game copy.

The repository contains no copyrighted game assets or game image.

Do not submit AI-generated issues or pull requests to SymphonyRecomp's upstream repositories. This independent experiment is not affiliated with Black Label HQ or Konami.
