using Godot;

namespace Cascade;

/// <summary>
/// Player controller: owns the movement state machine and dispatches per-state
/// physics updates. See CLAUDE.md ("Movement system design") for the full spec.
///
/// Grounded run speed (Task 3), sliding — velocity capture + friction decay
/// (Task 4), jump/bullet-jump entry (Task 5), and the BulletJump air
/// redirect + reduced Airborne air control (Task 6) are implemented.
/// </summary>
public partial class Player : CharacterBody3D
{
    [Export]
    public float Gravity = 20.0f;

    [Export]
    public float RunSpeed = 9.0f;

    [Export]
    public float SlideFriction = 4.0f;

    [Export]
    public float JumpVelocity = 8.0f;

    [Export]
    public float BulletJumpBoostMultiplier = 1.5f;

    [Export]
    public float RedirectSpeed = 10.0f;

    [Export]
    public float AirControlAcceleration = 4.0f;

    /// <summary>
    /// If the character falls below this world Y, it's treated as having
    /// fallen off the test arena and is reset to its spawn point. This is a
    /// testing convenience for the open, wall-less MovementArena — not part
    /// of the movement system design itself.
    /// </summary>
    [Export]
    public float FallResetY = -10.0f;

    private MovementState _currentState = MovementState.Grounded;

    private Vector3 _spawnPosition;

    /// <summary>
    /// Whether the one-time BulletJump air redirect charge is available.
    /// Set true on transition into BulletJump (see TransitionTo), consumed
    /// (set false) the moment it's used in UpdateBulletJump.
    /// </summary>
    private bool _redirectChargeAvailable = false;

    /// <summary>
    /// Horizontal (XZ) velocity captured at slide initiation. Decays via
    /// friction each tick in UpdateSliding. Y is intentionally never stored
    /// here — sliding is a horizontal-plane mechanic.
    /// </summary>
    private Vector3 _slideVelocity = Vector3.Zero;

    /// <summary>
    /// Horizontal speed ceiling for the current Airborne or BulletJump
    /// session, captured once on transition into either state (see
    /// TransitionTo) as Max(RunSpeed, entry horizontal speed). Both states'
    /// passive air-control nudge clamp to this value every tick so a long
    /// flight can't build speed beyond what the character entered with —
    /// recomputing this per-tick instead would let the cap ratchet upward
    /// indefinitely, which is the bug this field exists to avoid.
    /// </summary>
    private float _flightSpeedCap = 0.0f;

    private const float SlideStopThreshold = 0.1f;

    private Camera3D? _camera;

    public override void _Ready()
    {
        _camera = GetViewport().GetCamera3D();
        _spawnPosition = GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GlobalPosition.Y < FallResetY)
        {
            GlobalPosition = _spawnPosition;
            Velocity = Vector3.Zero;
            TransitionTo(MovementState.Grounded);
        }

        switch (_currentState)
        {
            case MovementState.Grounded:
                UpdateGrounded(delta);
                break;
            case MovementState.Sliding:
                UpdateSliding(delta);
                break;
            case MovementState.Airborne:
                UpdateAirborne(delta);
                break;
            case MovementState.BulletJump:
                UpdateBulletJump(delta);
                break;
        }

        MoveAndSlide();
    }

    /// <summary>
    /// Grounded: WASD run speed, camera-relative (Task 3, done); slide
    /// initiation on "slide" input (Task 4, done); jump — plain jump to
    /// Airborne, or BulletJump if "slide" is held when jump is pressed
    /// (Task 5, done).
    /// </summary>
    private void UpdateGrounded(double delta)
    {
        Vector2 inputVector = Input.GetVector("move_left", "move_right", "move_forward", "move_back");

        Vector3 velocity = Velocity;
        Vector3 direction = CameraRelativeDirection(inputVector);
        velocity.X = direction.X * RunSpeed;
        velocity.Z = direction.Z * RunSpeed;
        Velocity = velocity;

        if (Input.IsActionJustPressed("slide"))
        {
            TransitionTo(MovementState.Sliding);
            return;
        }

        if (Input.IsActionJustPressed("jump"))
        {
            if (Input.IsActionPressed("slide"))
            {
                LaunchBulletJump(new Vector3(Velocity.X, 0.0f, Velocity.Z));
                return;
            }

            velocity = Velocity;
            velocity.Y = JumpVelocity;
            Velocity = velocity;

            TransitionTo(MovementState.Airborne);
            return;
        }

        if (!IsOnFloor())
        {
            TransitionTo(MovementState.Airborne);
        }
    }

    /// <summary>
    /// Sliding: velocity vector locked at initiation (see TransitionTo), decays
    /// via friction here every physics tick. Not steered by WASD input — per
    /// the design's key invariant, slides are velocity vectors, not animations.
    /// Ends (back to Grounded) once the decayed speed drops below
    /// SlideStopThreshold or the slide input is released.
    /// </summary>
    private void UpdateSliding(double delta)
    {
        if (!IsOnFloor())
        {
            TransitionTo(MovementState.Airborne);
            return;
        }

        if (Input.IsActionJustPressed("jump"))
        {
            LaunchBulletJump(_slideVelocity);
            return;
        }

        float speed = _slideVelocity.Length();
        float decayedSpeed = speed - (SlideFriction * (float)delta);

        _slideVelocity = decayedSpeed > 0.0f
            ? _slideVelocity.Normalized() * decayedSpeed
            : Vector3.Zero;

        Vector3 velocity = Velocity;
        velocity.X = _slideVelocity.X;
        velocity.Z = _slideVelocity.Z;
        Velocity = velocity;

        if (_slideVelocity.Length() < SlideStopThreshold || !Input.IsActionPressed("slide"))
        {
            TransitionTo(MovementState.Grounded);
        }
    }

    /// <summary>
    /// Airborne: normal gravity, reduced air control, no redirect charge
    /// (Task 6, done). Air control is additive (nudges existing horizontal
    /// velocity) rather than overwriting it, so momentum is preserved — this
    /// is what makes it "gentle steering" rather than a full redirect.
    /// </summary>
    private void UpdateAirborne(double delta)
    {
        ApplyGravity(delta);

        Vector2 inputVector = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 direction = CameraRelativeDirection(inputVector);

        Vector3 velocity = Velocity;
        velocity.X += direction.X * AirControlAcceleration * (float)delta;
        velocity.Z += direction.Z * AirControlAcceleration * (float)delta;

        Vector3 horizontalVelocity = new Vector3(velocity.X, 0.0f, velocity.Z);
        if (horizontalVelocity.Length() > _flightSpeedCap)
        {
            horizontalVelocity = horizontalVelocity.Normalized() * _flightSpeedCap;
            velocity.X = horizontalVelocity.X;
            velocity.Z = horizontalVelocity.Z;
        }

        Velocity = velocity;

        if (IsOnFloor())
        {
            TransitionTo(MovementState.Grounded);
        }
    }

    /// <summary>
    /// BulletJump: airborne with one redirect charge available (Task 5/6,
    /// done). Pressing jump while the charge is available consumes it and
    /// overwrites horizontal velocity from current WASD input + camera
    /// forward (RoR2-style redirect), then transitions to Airborne. Y
    /// velocity is left untouched — this redirects the trajectory, it
    /// doesn't relaunch the character. Also gets the same passive
    /// gentle-steering nudge Airborne has, so a chained bullet-jump sequence
    /// can be nudged off-course without spending the redirect charge.
    /// </summary>
    private void UpdateBulletJump(double delta)
    {
        ApplyGravity(delta);

        if (Input.IsActionJustPressed("jump") && _redirectChargeAvailable)
        {
            _redirectChargeAvailable = false;

            Vector2 inputVector = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            Vector3 direction = CameraRelativeDirection(inputVector);

            Vector3 velocity = Velocity;
            velocity.X = direction.X * RedirectSpeed;
            velocity.Z = direction.Z * RedirectSpeed;
            Velocity = velocity;

            TransitionTo(MovementState.Airborne);
            return;
        }

        // Passive in-flight steering, identical to Airborne's gentle nudge —
        // lets a chained bullet-jump sequence be gently redirected without
        // spending the one-time redirect charge above. Capped by the same
        // per-flight speed ceiling so this can't build speed beyond what the
        // character launched with.
        Vector2 steerInput = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 steerDirection = CameraRelativeDirection(steerInput);

        Vector3 steeredVelocity = Velocity;
        steeredVelocity.X += steerDirection.X * AirControlAcceleration * (float)delta;
        steeredVelocity.Z += steerDirection.Z * AirControlAcceleration * (float)delta;

        Vector3 steeredHorizontal = new Vector3(steeredVelocity.X, 0.0f, steeredVelocity.Z);
        if (steeredHorizontal.Length() > _flightSpeedCap)
        {
            steeredHorizontal = steeredHorizontal.Normalized() * _flightSpeedCap;
            steeredVelocity.X = steeredHorizontal.X;
            steeredVelocity.Z = steeredHorizontal.Z;
        }

        Velocity = steeredVelocity;

        if (IsOnFloor())
        {
            // Defense-in-depth: every TransitionTo(BulletJump) already resets
            // this to true on next entry, so this is currently a no-op, but
            // it keeps the invariant enforced by code rather than convention
            // in case charge-granting ever becomes conditional.
            _redirectChargeAvailable = false;

            if (Input.IsActionPressed("slide"))
            {
                TransitionTo(MovementState.Sliding);
            }
            else
            {
                TransitionTo(MovementState.Grounded);
            }
        }
    }

    /// <summary>
    /// Converts a 2D WASD input vector into a world-space direction relative
    /// to the active camera's facing, flattened onto the XZ (horizontal)
    /// plane. Used by Grounded (Task 3) and will be reused for air control
    /// and redirects in later tasks.
    /// </summary>
    private Vector3 CameraRelativeDirection(Vector2 inputVector)
    {
        if (inputVector == Vector2.Zero)
        {
            return Vector3.Zero;
        }

        Camera3D camera = _camera ?? GetViewport().GetCamera3D();
        if (camera == null)
        {
            return Vector3.Zero;
        }

        Basis cameraBasis = camera.GlobalTransform.Basis;

        Vector3 forward = cameraBasis.Z * -1.0f;
        forward.Y = 0.0f;
        forward = forward.Normalized();

        Vector3 right = cameraBasis.X;
        right.Y = 0.0f;
        right = right.Normalized();

        Vector3 direction = (right * inputVector.X) + (forward * -inputVector.Y);
        return direction.Normalized();
    }

    /// <summary>
    /// Applies gravity to vertical velocity. Shared by the airborne states
    /// (Airborne, BulletJump) so their gravity handling doesn't drift apart
    /// as each grows its own state-specific logic in later tasks.
    /// </summary>
    private void ApplyGravity(double delta)
    {
        Vector3 velocity = Velocity;
        velocity.Y -= Gravity * (float)delta;
        Velocity = velocity;
    }

    /// <summary>
    /// Shared BulletJump entry point for both of its entry paths (Grounded
    /// jump-with-slide-held and Sliding jump — see CLAUDE.md's state machine
    /// diagram). Scales the magnitude of <paramref name="horizontalVelocity"/>
    /// by BulletJumpBoostMultiplier (direction preserved, not renormalized to
    /// a fixed speed), applies it to Velocity.X/Z, sets Velocity.Y to
    /// JumpVelocity, and transitions into BulletJump. Kept in one place so
    /// the two call sites don't drift apart as Task 6 adds redirect logic.
    /// </summary>
    private void LaunchBulletJump(Vector3 horizontalVelocity)
    {
        Vector3 boostedVelocity = horizontalVelocity * BulletJumpBoostMultiplier;

        Velocity = new Vector3(boostedVelocity.X, JumpVelocity, boostedVelocity.Z);

        TransitionTo(MovementState.BulletJump);
    }

    /// <summary>
    /// Central transition point for the movement state machine. Later tasks
    /// should route all state changes through here (rather than mutating
    /// _currentState directly).
    ///
    /// On transition into Sliding, snapshots the current horizontal (XZ)
    /// velocity into _slideVelocity — this is the locked vector that
    /// UpdateSliding decays via friction. Y is dropped since sliding is a
    /// horizontal-plane mechanic (Task 4, done).
    ///
    /// On transition into BulletJump, arms the one-time air redirect charge
    /// (Task 6, done) — consumed in UpdateBulletJump when jump is pressed —
    /// and captures _flightSpeedCap the same way Airborne does (see below;
    /// LaunchBulletJump has already applied the boosted Velocity by the time
    /// this runs, so the cap reflects the boosted entry speed, not the
    /// pre-boost speed).
    ///
    /// On transition into Airborne or BulletJump, captures _flightSpeedCap as
    /// Max(RunSpeed, entry horizontal speed) — see _flightSpeedCap's doc
    /// comment for why this must be a one-time snapshot rather than
    /// recomputed each tick.
    /// </summary>
    private void TransitionTo(MovementState newState)
    {
        if (newState == _currentState)
        {
            return;
        }

        if (newState == MovementState.Sliding)
        {
            _slideVelocity = new Vector3(Velocity.X, 0.0f, Velocity.Z);
        }

        if (newState == MovementState.BulletJump)
        {
            _redirectChargeAvailable = true;
        }

        if (newState == MovementState.Airborne || newState == MovementState.BulletJump)
        {
            float entrySpeed = new Vector3(Velocity.X, 0.0f, Velocity.Z).Length();
            _flightSpeedCap = Mathf.Max(RunSpeed, entrySpeed);
        }

        _currentState = newState;
    }
}
