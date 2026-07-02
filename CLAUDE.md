# Cascade — AI Onboarding

Movement-first roguelite. The movement system is the game — everything else (guns, abilities,
power-ups, scaling enemies) comes after the movement feels undeniably good. Inspiration:
Warframe (bullet jump + infinite chaining), Crab Champions (omnidirectional slides), Risk of
Rain 2 (mid-air direction redirect via jump).

**Current state:** Tasks 1-7 of the build-out below are done and Task 8 (Feel tuning) is underway
via live playtesting. `scenes/MovementArena.tscn`, `scripts/Player.cs` (full movement state
machine: grounded run, slide, jump/bullet-jump, air redirect, speed-capped air control in both
Airborne and BulletJump), and `scripts/PlayerCamera.cs` (always camera-relative: camera orbits via
mouse and unconditionally drives Player.Rotation.Y every tick — no RMB toggle) exist and build
clean.

---

## Tech stack

- **Engine:** Godot 4.7 (.NET)
- **Language:** C# 12, .NET 8
- **Renderer:** Forward Plus (3D) per `project.godot`, but see "Running the game" below — the
  default renderer doesn't produce visible output in this dev sandbox (WSL2), so testing here
  uses `--rendering-method gl_compatibility` instead.

---

## Running the game

Standalone launch, not the Godot editor's embedded Game panel — the embedded panel's input
forwarding is unreliable on WSL2 (see `games/ecosystem_sim/docs/issues/2026_06_30_godot_wsl2_embedded_panel_input.md`
for the same issue hit on another project in this workspace). Use the project's own scripts:

```bash
./startup.sh   # launches the game (kills any existing instance first)
./kill.sh      # kills a running instance
```

`startup.sh` launches Godot with `--rendering-method gl_compatibility` — the default Vulkan/
Forward+ path falls back to a software (llvmpipe) renderer in this sandbox and produces a black
window with no visible content; `gl_compatibility` picks up the real GPU via WSL's D3D12
translation layer and renders correctly. Logs go to `/tmp/cascade_game.log`.

**Known environment limitation:** `Input.MouseMode = Captured` (mouse-look/pointer-lock) doesn't
reliably re-engage after the window loses and regains OS focus in this sandbox's nested display
stack (Weston/XWayland under WSLg) — keyboard input is unaffected, only mouse-look silently stops
working after a focus round-trip. This looks like an environment-specific limitation, not a code
defect (the implementation follows Godot's standard pointer-lock API); it may not reproduce
outside this sandbox. Workaround: relaunch via `./kill.sh && ./startup.sh` after tabbing away and
back if mouse-look stops responding.

**Controls:** WASD move (camera-relative) · C slide · Space jump (hold C + Space for a
ground-based bullet jump) · mouse look (always coupled to character facing — mouse rotates the
character directly, no lock toggle).

**New asset gotcha:** this project has never been opened in the Godot editor, so newly added
assets (textures, etc.) won't have the `.import` metadata Godot needs to load them, and the game
will fail to launch with a "no loader found" / "referenced non-existent resource" error. Run a
one-time headless import pass after adding a new asset, before launching:

```bash
Godot_v4.7-stable_mono_linux.x86_64 --headless --path . --import   # binary resolved from PATH
```

---

## Project structure (planned)

```
Cascade/
├── project.godot
├── Cascade.csproj
├── startup.sh                 # Launch the game standalone (see "Running the game")
├── kill.sh                    # Kill a running instance
├── scenes/
│   ├── Main.tscn              # Root scene (not yet created — MovementArena.tscn is the only scene)
│   └── MovementArena.tscn     # Flat test arena for movement prototyping
├── scripts/
│   ├── Player.cs              # CharacterBody3D: owns state machine, reads input
│   ├── MovementState.cs       # Enum: Grounded, Sliding, Airborne, BulletJump
│   ├── PlayerCamera.cs        # Camera3D: always locked to player facing (no RMB toggle)
│   └── ...
└── assets/
    └── textures/
        └── ground_grid.png    # Tileable 1-meter grid texture on the test arena floor
```

---

## Movement system design

This is the core of the game. Get this right before anything else.

### Inspirations and what we take from each

| Game | What we take |
|------|-------------|
| Warframe | Bullet jump (directional launch), infinite slide↔jump chaining, zero cooldowns on movement |
| Crab Champions | Omnidirectional slides — the slide vector is whatever direction you initiate from, not locked to forward |
| Risk of Rain 2 | Mid-air direction redirect: pressing jump while airborne redirects velocity using current input |

### State machine

```
Grounded ──slide──> Sliding ──jump──> BulletJump (airborne, redirect charge available)
Grounded ──jump──>  Airborne
Grounded ──jump (slide held)──> BulletJump
Sliding  ──jump──>  BulletJump
BulletJump ──jump──> Airborne (redirect consumed, normal air)
Airborne   ──land──> Grounded
BulletJump ──land──> Grounded (or Sliding if holding slide on land)
```

**Key invariant: slides are velocity vectors, not animations.** The slide direction is the
momentum vector at initiation. This gives omnidirectionality for free — no special-casing needed.

### State behaviours

**Grounded**
- WASD moves at run speed, camera-relative
- Can initiate slide at any time (carries current velocity vector)
- Can jump normally

**Sliding**
- Velocity vector locked at initiation (direction + magnitude)
- Decays over time (friction), but can be re-energised by chaining into a jump and landing
- Can chain into BulletJump at any point (no cooldown)

**Airborne**
- Normal gravity
- WASD gives reduced air control (not full redirect, just gentle steering)
- No redirect charge available

**BulletJump** (the key state)
- Entered from: slide→jump or ground→jump (with slide held)
- Has one redirect charge
- Pressing jump consumes the charge: takes current WASD input + camera forward and computes
  new velocity vector — this is the RoR2-style air redirect
- Can chain back into Sliding on landing (hold slide before touching ground)

### Camera and movement decoupling

**Always camera-relative (current, only mode):**
- WASD moves relative to camera forward (standard third-person) — `CameraRelativeDirection` in
  Player.cs
- Mouse orbits the camera (`_yaw`/`_pitch` in PlayerCamera.cs), and that same `_yaw` is written
  into `Player.Rotation.Y` unconditionally, every tick — the camera always owns character facing,
  there's no separate "character faces its velocity" behavior
- Air redirects use (camera forward × WASD input) at the moment jump is pressed
- Enables continuous mid-air steering via mouse — slide backwards while looking forward, change
  trajectory in flight without pressing jump, no toggle needed to enable it

This solves the ambiguity "I'm moving forward but looking left — do I slide backward?" by always
answering "you steer with your face": turning the camera around mid-chain makes the "reverse" key
continue your original momentum direction, since momentum (slide velocity, bullet-jump velocity)
is a locked world-space vector that only decays via friction — only the meaning of forward/right
for *new* input changes when the camera turns.

There used to be a toggle here (RMB held = "locked" mode as above; released = camera free-orbits
independently while the character faces its velocity). That toggle never worked reliably and added
overhead on top of managing momentum/chaining, so it was removed — the always-camera-relative
behavior above is simply how the game works now, unconditionally.

**Future note:** an independent free-orbit camera mode (camera separate from character facing) may
come back later as a "time-slow" mechanic. The orbit math (`_yaw`/`_pitch` accumulation in
PlayerCamera.cs) was deliberately left in place to make that easy to resurrect, but there is no
mode-switching mechanism implemented or planned right now — this is a forward-looking note, not a
spec for an existing feature.

### Chaining rules

- No cooldowns on any movement action
- Slide → jump → (air) → land → slide → jump = infinite chain
- The only resource consumed is the BulletJump redirect charge, which resets on landing

---

## What's next (in order)

1. ~~**MovementArena scene** — flat plane, placeholder capsule character, basic lighting~~ done
2. ~~**Player.cs + state machine** — `CharacterBody3D`, states as enum, `_PhysicsProcess` dispatch~~ done
3. ~~**Grounded movement** — WASD + camera-relative direction, run speed~~ done
4. ~~**Slide** — velocity vector capture at initiation, friction decay~~ done
5. ~~**Jump + BulletJump** — vertical launch with slide→jump boosting direction~~ done
6. ~~**Air redirect** — consume charge, recompute velocity from input~~ done
7. ~~**Camera (PlayerCamera.cs)** — orbit camera, always coupled to player facing~~ done
8. **Feel tuning** — speeds, friction curves, jump height, redirect strength ← next
9. **Then and only then:** roguelite shell (enemies, guns, rooms, power-ups)

---

## Design principles

- **Movement is non-negotiable.** If it doesn't feel tight and precise, nothing else matters.
- **No cooldowns on core movement.** Warframe taught us this. Cooldowns break flow.
- **Momentum is the reward.** Building and maintaining speed should feel satisfying and skilled.
- **3D, third-person.** Camera behind/above player. Forward Plus renderer.
