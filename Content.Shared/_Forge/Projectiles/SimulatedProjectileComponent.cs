namespace Content.Shared._Forge.Projectiles;

/// <summary>
/// Marks ammo that uses tick-integrated segment raycast simulation instead of a flying physics entity.
/// </summary>
[RegisterComponent]
public sealed partial class SimulatedProjectileComponent : Component
{
    /// <summary>
    /// Maximum travel distance in meters before the projectile expires without hitting anything.
    /// </summary>
    [DataField]
    public float MaxDistance = 100f;

    /// <summary>
    /// How far to offset the ray origin along the shot direction to avoid immediate self/floor collisions.
    /// </summary>
    [DataField]
    public float SpawnOffset = 0.4f;
}
