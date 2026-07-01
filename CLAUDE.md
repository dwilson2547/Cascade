# Cascade — AI Onboarding

Movement-first roguelite. The movement system is the game — everything else (guns, abilities,
power-ups, scaling enemies) comes after the movement feels undeniably good. Inspiration:
Warframe (bullet jump + infinite chaining), Crab Champions (omnidirectional slides), Risk of
Rain 2 (mid-air direction redirect via jump).

**Current state:** Empty scaffold. `project.godot` + `Cascade.csproj` exist. No scenes,
no scripts, no gameplay. Next step: `CharacterBody3D` controller + movement state machine.

---

## Tech stack

- **Engine:** Godot 4.7 (.NET)
- **Language:** C# 12, .NET 8
- **Renderer:** Forward Plus (3D)

---

## Project structure (planned)

```
Cascade/
├── project.godot
├── Cascade.csproj
├── scenes/
│   ├── Main.tscn              # Root scene
│   └── MovementArena.tscn     # Flat test arena for movement prototyping
├── scripts/
│   ├── Player.cs              # CharacterBody3D: owns state machine, reads input
│   ├── MovementState.cs       # Enum: Grounded, Sliding, Airborne, BulletJump
│   ├── PlayerCamera.cs        # Camera3D: free-orbit default, locks to player on RMB
│   └── ...
└── assets/
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

**Default (free-look):**
- WASD moves relative to camera forward (standard third-person)
- Mouse orbits camera freely; character faces movement direction
- Air redirects use (camera forward × WASD input) at the moment jump is pressed

**Right-click held (locked):**
- Camera snaps and locks to character facing
- Mouse now rotates the character directly
- Enables continuous mid-air steering via mouse — slide backwards while looking forward,
  change trajectory in flight without pressing jump
- Release RMB to return to free-look

This split solves the ambiguity: "I'm moving forward but looking left — do I slide backward?"
Default mode: no, your slide follows your velocity. Locked mode: yes, you steer with your face.

### Chaining rules

- No cooldowns on any movement action
- Slide → jump → (air) → land → slide → jump = infinite chain
- The only resource consumed is the BulletJump redirect charge, which resets on landing

---

## What's next (in order)

1. **MovementArena scene** — flat plane, placeholder capsule character, basic lighting
2. **Player.cs + state machine** — `CharacterBody3D`, states as enum, `_PhysicsProcess` dispatch
3. **Grounded movement** — WASD + camera-relative direction, run speed
4. **Slide** — velocity vector capture at initiation, friction decay
5. **Jump + BulletJump** — vertical launch with slide→jump boosting direction
6. **Air redirect** — consume charge, recompute velocity from input
7. **Camera (PlayerCamera.cs)** — free-orbit, RMB lock mode
8. **Feel tuning** — speeds, friction curves, jump height, redirect strength
9. **Then and only then:** roguelite shell (enemies, guns, rooms, power-ups)

---

## Design principles

- **Movement is non-negotiable.** If it doesn't feel tight and precise, nothing else matters.
- **No cooldowns on core movement.** Warframe taught us this. Cooldowns break flow.
- **Momentum is the reward.** Building and maintaining speed should feel satisfying and skilled.
- **3D, third-person.** Camera behind/above player. Forward Plus renderer.
