using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Projectiles;

/// <summary>
/// Raised on the server when a simulated projectile begins flight. Clients use this for tracer visuals only.
/// </summary>
[Serializable, NetSerializable]
public sealed class SimulatedProjectileSpawnEvent : EntityEventArgs
{
    public int Id;
    public NetCoordinates Origin;
    public Vector2 Velocity;
    public Color TracerColor;
    public float TracerLength;
    public float TracerLifetime;
    public Angle Angle;

    public SimulatedProjectileSpawnEvent(
        int id,
        NetCoordinates origin,
        Vector2 velocity,
        Color tracerColor,
        float tracerLength,
        float tracerLifetime,
        Angle angle)
    {
        Id = id;
        Origin = origin;
        Velocity = velocity;
        TracerColor = tracerColor;
        TracerLength = tracerLength;
        TracerLifetime = tracerLifetime;
        Angle = angle;
    }
}

/// <summary>
/// Raised when a simulated projectile ends (hit, penetration spent, or lifetime/distance expired).
/// </summary>
[Serializable, NetSerializable]
public sealed class SimulatedProjectileEndEvent : EntityEventArgs
{
    public int Id;
    public NetCoordinates? FinalPosition;

    public SimulatedProjectileEndEvent(int id, NetCoordinates? finalPosition = null)
    {
        Id = id;
        FinalPosition = finalPosition;
    }
}
