using Godot;

namespace Cascade;

/// <summary>
/// Camera controller: always coupled to the Player's facing. See CLAUDE.md
/// ("Camera and movement decoupling") for the full design.
///
/// _yaw is the single source of truth for horizontal orientation — it drives
/// the camera's orbit position every tick, and is also written directly into
/// the Player's Rotation.Y every tick. There is no separate "camera yaw"/
/// "player yaw": unifying them is what makes "mouse rotates the character
/// directly" work.
///
/// Note: the free-orbit accumulation of _yaw/_pitch below is independent of
/// this coupling and was previously reachable as a toggleable "free-look"
/// mode (RMB to lock/unlock). That toggle has been removed — the game now
/// always runs in what used to be "locked" mode — but the orbit math itself
/// is left intact rather than deleted, since an independent-orbit mode may
/// return later as a "time-slow" mechanic. It is not currently wired up to
/// anything; there is no mode switch.
/// </summary>
public partial class PlayerCamera : Camera3D
{
    [Export]
    public float MouseSensitivity = 0.005f;

    [Export]
    public float MinPitch = -1.2f;

    [Export]
    public float MaxPitch = 1.2f;

    [Export]
    public float OrbitDistance = 6.0f;

    [Export]
    public float HeightOffset = 1.5f;

    /// <summary>
    /// Horizontal orbit angle, radians, measured the same way as a Node3D's
    /// Rotation.Y (see the Player-facing formula in Player.cs for the
    /// derivation). yaw = 0 reproduces the original static camera's
    /// convention of sitting behind the player along +Z.
    /// </summary>
    private float _yaw = 0.0f;

    /// <summary>
    /// Vertical orbit angle, radians. Positive pitch swings the camera below
    /// eye level so it looks *up* at the target; negative pitch swings it
    /// above eye level so it looks *down*. See the offset derivation in
    /// _PhysicsProcess for why this is the sign convention that falls out of
    /// rotating the base offset by Basis(Vector3.Right, _pitch).
    /// </summary>
    private float _pitch = 0.0f;

    private Node3D? _player;

    /// <summary>
    /// Set by OnFocusEntered, consumed one physics tick later in
    /// _PhysicsProcess. Re-capturing the mouse immediately inside the
    /// FocusEntered signal handler was found (via live testing) to silently
    /// fail to re-establish the OS-level pointer grab in some window-manager
    /// setups -- the signal can fire slightly before the WM has fully
    /// finished handing focus back. Deferring by one tick gives that
    /// handoff time to settle before we attempt the grab.
    /// </summary>
    private bool _recaptureMousePending = false;

    public override void _Ready()
    {
        _player = GetTree().GetFirstNodeInGroup("player") as Node3D;

        // Initial state: the window typically has focus when the game
        // starts. The FocusEntered/FocusExited subscriptions below keep this
        // in sync afterward -- without them, MouseMode stays Captured (a raw
        // OS-level pointer grab) even when the window loses focus, so mouse
        // movement over the background/unfocused window would still drive
        // the camera.
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GetWindow().FocusEntered += OnFocusEntered;
        GetWindow().FocusExited += OnFocusExited;
    }

    private void OnFocusEntered()
    {
        _recaptureMousePending = true;
    }

    private void OnFocusExited()
    {
        _recaptureMousePending = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            // Sign check: with yaw defined so that a Player/camera facing of
            // yaw=0 points down -Z, increasing yaw turns the view *left*
            // (world-space forward becomes (-sin(yaw), 0, -cos(yaw)) — see
            // Player.cs's facing-rotation comment for the same derivation).
            // Mouse-right should turn the view right, so mouse-right must
            // *decrease* yaw — hence subtraction here, not addition.
            _yaw -= motion.Relative.X * MouseSensitivity;

            // Mouse-up should look up. Pitch is applied to the orbit offset
            // via Basis(Vector3.Right, _pitch) in _PhysicsProcess, and
            // increasing pitch there moves the camera *below* eye level so
            // it looks up at the target — so mouse-up (negative Relative.Y,
            // since Godot's screen Y grows downward) must *increase* pitch,
            // which the subtraction below achieves.
            _pitch -= motion.Relative.Y * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);

            if (_player != null)
            {
                Vector3 rotation = _player.Rotation;
                rotation.Y = _yaw;
                _player.Rotation = rotation;
            }
        }
    }

    /// <summary>
    /// Runs in _PhysicsProcess (not _Process) so the camera stays in sync
    /// with the Player's CharacterBody3D physics-driven movement tick.
    ///
    /// Builds the camera's position as spherical coordinates around a look
    /// point (Player position + HeightOffset): start from the base offset
    /// (0, 0, OrbitDistance) — behind the player along +Z, matching the
    /// original static camera's convention — rotate it by pitch around the
    /// world X axis, then rotate the result by yaw around world Y. Composing
    /// pitch-then-yaw this way (rather than yaw-then-pitch) is what keeps
    /// "changing yaw orbits at constant height" true: the Y-rotation never
    /// touches the Y component the pitch rotation just produced.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_recaptureMousePending)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _recaptureMousePending = false;
        }

        if (_player == null)
        {
            return;
        }

        Vector3 lookPoint = _player.GlobalPosition + (Vector3.Up * HeightOffset);

        Basis yawBasis = new Basis(Vector3.Up, _yaw);
        Basis pitchBasis = new Basis(Vector3.Right, _pitch);
        Vector3 offset = yawBasis * (pitchBasis * new Vector3(0.0f, 0.0f, OrbitDistance));

        GlobalPosition = lookPoint + offset;
        LookAt(lookPoint, Vector3.Up);
    }
}
