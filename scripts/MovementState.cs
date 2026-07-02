namespace Cascade;

/// <summary>
/// The player's movement state, per the state machine described in CLAUDE.md
/// ("Movement system design"). Transitions:
///
///   Grounded   --slide--> Sliding
///   Grounded   --jump-->  Airborne
///   Grounded   --jump (slide held)--> BulletJump
///   Sliding    --jump-->  BulletJump
///   BulletJump --jump-->  Airborne   (redirect charge consumed)
///   Airborne   --land-->  Grounded
///   BulletJump --land-->  Grounded (or Sliding if holding slide on land)
/// </summary>
public enum MovementState
{
    Grounded,
    Sliding,
    Airborne,
    BulletJump,
}
