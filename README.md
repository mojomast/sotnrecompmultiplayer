# SotN Recomp Multiplayer

An experimental [SymphonyRecomp](https://github.com/BlackLabelHQ/SymphonyRecomp) mod project for adding cooperative multiplayer to Castlevania: Symphony of the Night.

## Project Status

This repository is at the Player 2 feasibility stage. Version `0.1.3` provides a diagnostic proxy that tests the engine paths needed before local or networked co-op can be implemented safely.

This is not a complete co-op mod. It tests the plumbing needed before one can be built:

- Logical controller 2 input from the runtime through SOTN's processed pad state.
- A mod-owned Player 2 position driven by controller 2.
- Basic gravity, jumping, floors, ceilings, walls, and one-way tile collision.
- Host-rendered Player 2 diagnostic geometry.
- Recreating the proxy after normal room transitions.
- Read-only pressure measurements for SOTN's player-effect entity pool.

The proxy is managed entirely by the mod. It does not allocate a SOTN entity, damage enemies, change Alucard, alter progression, or write save data.

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

When updating, run `git pull`, restart or reload the mod, and confirm `Co-op Feasibility Probe v0.1.3` appears in its settings panel.

## Test Procedure

Use only this diagnostic mod for the first test when possible.

1. No second controller is required. Leave **Use virtual Player 2 keyboard** enabled for the default test. If testing hardware instead, disable it and configure controller 2 before starting the game.
2. Load Alucard into an ordinary room with a flat floor, wall, and normal exit.
3. Open the mod settings and click **Reset diagnostic**.
4. For a controller-free smoke test, click **Run automatic P2 movement test**, then close the entire Mods window. The probe sends short Right, Left, and Cross commands only to Pad 2 and returns it to neutral.
5. To test manual virtual controls, close the entire Mods window and wait two frames so this mod's UI suppression expires.
6. Press and release `I`, `L`, `K`, `J`, `U`, `O`, and `P`. These inject Up, Right, Down, Left, Cross, Circle, and Start respectively into SOTN controller port 2.
7. Use `J`/`L` to move the cyan proxy and `U` to jump. These keys should not be bound to Player 1.
8. In physical mode, press the equivalent controller buttons, enable **Require analog test**, and move both sticks through their full ranges.
9. Test the proxy against a floor and wall, then jump into a reachable ceiling. Test a one-way platform if available. `C` remains waiting until floor, wall, and ceiling behavior have all been observed.
10. Use Player 1 to cross a normal room boundary. Move the proxy again after the next room settles.
11. Spend at least ten seconds in both a quiet room and an enemy/effect-heavy room.
12. Reopen the mod settings and check **I can see the proxy** if it was rendered correctly.
13. Click **Copy report** and paste the single `P2D1 ...` line back to the developer. **Print report to console** is available as a fallback; use SymphonyRecomp's Console panel or the terminal that launched it.
14. Also paste any **First error** or **Collision disabled** line shown in the panel and describe any visual or collision anomaly.

The settings panel reports which required controls have reached each stage relevant to the selected physical or virtual input mode.

## Report Fields

Example:

```text
P2D1 V=0.1.3 H=P:900/880/860/860/2700 I=P:K:-/7/7/7/A- K=7/7/H0000/R0000/U7860/N1/S0 M=P:18/24/1 R=P:600/610/1/D900/H00 C=P:4512/0/0/350/4/1/B11 T=P:1/1/3 S=P:17/6/860 G=OK:E1S1P1 Q=1/1/1/0 A=P:64/1 E=0
```

| Field | Meaning |
| --- | --- |
| `H` | Hook result and VSync/engine/player-update/render/port-2-pad callback counts. |
| `I` | Input result and source: `K` virtual keyboard or `C` configured controller, followed by host/pad/game/tapped stages and analog-axis count. |
| `K` | Virtual key-down/key-up counts, output/raw/current-union masks, neutral observation, and UI suppression frames. `-` means physical-controller mode. |
| `M` | Proxy movement result and left/right pixel distance plus jump observation. |
| `R` | Direct GP0 draws/eligible callbacks, visual confirmation, `DrawOTag` callbacks, and HLE active/ready bits. GP0 drawing also works when both HLE bits are zero. |
| `C` | Collision result, calls/restoration failures/invalid corrections/ground/wall/ceiling contacts plus solid/empty observation bits. |
| `T` | Transition result and passed/completed/layer-event counts. |
| `S` | Player-effect slot pressure result, minimum free slots/minimum longest free run/sample count. |
| `G` | Safety-gate code plus enabled/safe/proxy-initialized bits. This identifies why active tests have not started. |
| `Q` | Diagnostic generation, proxy reset requests, completed resets, and pending-reset bit. |
| `A` | Automatic movement state, sequence frame, and completed-run count. `P` means the sequence returned Pad 2 to neutral. |
| `E` | `0` for none, `A` for API mismatch, `C` for rejected collision data, or `X` for a caught exception. |

`P` means pass, `W` means waiting or warning, and `F` means failure.

Pass thresholds are deliberately conservative:

- `H` needs at least 60 callbacks from each required hook/event.
- `I` in virtual mode needs all seven key-downs/key-ups plus all seven buttons at pad/game/tapped stages. Physical mode needs all seven buttons at all four stages and, when selected, all four analog axes.
- `M` needs at least eight commanded pixels in both horizontal directions and a measured four-pixel jump rise.
- `R` needs at least 60 direct GP0 draws plus the tester's visual confirmation.
- `C` needs at least 120 calls, intact scratch restoration, solid and empty samples, and observed ground, wall, and ceiling contacts.
- `T` needs a changed stable room identity and post-transition Player 2 movement for every counted transition.
- `S` needs at least five samples, at least four free attack/effect slots, and a contiguous run of at least two.

## Safety Model

- Collision output uses temporary guest stack storage only during a synchronous game-update hook.
- Every byte used for collision output is restored and verified after each call.
- The expected `v0.4.3b` collision function pointer, nested collision-table pointer, and tilemap dimensions must validate before active tests run.
- The proxy consumes no game entity or primitive-pool slot.
- Entity capacity is read only.
- Menus, maps, cutscenes, loading, special transitions, unsupported characters, and invalid tilemaps suspend the proxy.
- Hook exceptions stop active probing and are retained in the settings panel instead of being retried every frame.
- Virtual controls mutate only the active-low `PadReadEvent` for runtime port `1`, which is SOTN controller port 2. Port 0 and Player 1 input are not modified.
- Virtual mode replaces incoming Pad 2 state rather than merging with configured hardware, so its report cannot pass using a physical controller accidentally.
- Opening the settings panel, loading, entering menus, or entering cutscenes releases and suppresses virtual input. **Release virtual keys** provides a manual recovery action.
- The proxy marker is emitted directly through normal PSX GP0 rectangle commands after the stage ordering table, so it works with both HLE and software GPU rendering.
- The automatic movement test is explicit, cancellable, bounded to 64 VSyncs, sends only Right/Left/Cross, aborts when gameplay becomes unsafe, and finishes with neutral Pad 2 input.

These checks reduce risk on the exact supported release but cannot prove compatibility with modified executables or future SymphonyRecomp versions. The collision model is intentionally incomplete. A pass establishes that a controlled, rendered, terrain-aware proxy can be maintained; it does not establish full Alucard physics, combat, enemy targeting, moving platforms, elevators, transformations, spells, familiars, or completed co-op.

## Current Limitations

- Player 1 owns the camera and room transitions.
- The proxy uses diagnostic geometry rather than an Alucard sprite.
- Collision is based on tile probes and does not include moving entities or hazards.
- Slopes, stair steps, water, quicksand, inverted gravity, and unusual scripted rooms need separate testing.
- No Player 2 HP, combat, inventory, menu, revive, or persistence exists yet.
- A configured Player 2 keyboard mapping can make `Connected2` true without a physical second controller.
- Hot-plugging can change controller assignment.
- Virtual keyboard injection validates SymphonyRecomp's BIOS packed-pad path; it does not claim to test every possible direct-buffer controller API.

## Version History

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

The implementation hooks `dra/RunMainEngine`, `dra/UpdatePlayerEntities`, `dra/RenderEntities`, and `main/DrawOTag`; observes `VSyncEvent`, `PadReadEvent`, `PlayerLoadedEvent`, and `RoomLayerLoadEvent`; calls SOTN's generic collision API through `0x8003C7BC`; and renders backend-independent GP0 rectangles after the stage ordering table.

The source is dynamically compile-checked against the exact SymphonyRecomp `v0.4.3b` APIs and the current local development runtime using .NET 10. Gameplay validation still requires SymphonyRecomp and a legally owned US game copy.

The repository contains no copyrighted game assets or game image.

Do not submit AI-generated issues or pull requests to SymphonyRecomp's upstream repositories. This independent experiment is not affiliated with Black Label HQ or Konami.
