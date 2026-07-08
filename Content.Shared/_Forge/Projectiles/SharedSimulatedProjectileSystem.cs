using System.Numerics;
using Content.Shared._Emberfall.Weapons.Ranged;
using Content.Shared.Damage.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;

namespace Content.Shared._Forge.Projectiles;

/// <summary>
/// Shared helpers for simulated projectiles (segment raycast collision filtering).
/// </summary>
public abstract class SharedSimulatedProjectileSystem : EntitySystem
{
    [Dependency] protected SharedTransformSystem Transform = default!;
    protected EntityQuery<PhysicsComponent> PhysQuery;
    protected EntityQuery<FixturesComponent> FixQuery;

    public override void Initialize()
    {
        base.Initialize();
        PhysQuery = GetEntityQuery<PhysicsComponent>();
        FixQuery = GetEntityQuery<FixturesComponent>();
    }

    public int GetProjectileCollisionMask(EntityUid uid)
    {
        var defaultMask = (int) CollisionGroup.Impassable | (int) CollisionGroup.BulletImpassable;

        if (!FixQuery.TryComp(uid, out var fixtures)
            || !fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fix))
        {
            return defaultMask;
        }

        return fix.CollisionMask;
    }

    public bool CanUseSimulatedProjectile(EntityUid uid)
    {
        if (!HasComp<SimulatedProjectileComponent>(uid) || !HasComp<ProjectileComponent>(uid))
            return false;

        if (HasComp<EmbeddableProjectileComponent>(uid))
            return false;

        // Component type names checked via metadata to avoid hard deps on server-only types.
        var meta = MetaData(uid);
        if (meta.EntityPrototype?.Components.ContainsKey("TriggerOnCollide") == true)
            return false;

        if (meta.EntityPrototype?.Components.ContainsKey("TriggerOnProjectileHit") == true)
            return false;

        if (meta.EntityPrototype?.Components.ContainsKey("TargetSeeking") == true)
            return false;

        if (meta.EntityPrototype?.Components.ContainsKey("TargetGuided") == true)
            return false;

        if (meta.EntityPrototype?.Components.ContainsKey("IgniteOnCollide") == true)
            return false;

        return true;
    }

    /// <summary>
    /// Returns the closest valid hit along a ray segment, or null if nothing was hit.
    /// </summary>
    public (EntityUid Entity, float Distance)? TryGetSegmentHit(
        EntityUid proxy,
        SimulatedProjectileFlightData flight,
        EntityUid? sourceGrid,
        MapId mapId,
        Vector2 origin,
        Vector2 direction,
        float distance,
        int collisionMask)
    {
        if (distance <= 0f)
            return null;

        if (!PhysQuery.TryComp(proxy, out var projBody)
            || !FixQuery.TryComp(proxy, out var projFixtures)
            || !projFixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var projFix))
        {
            return null;
        }

        var normalized = direction / distance;
        var ray = new CollisionRay(origin, normalized, collisionMask);
        var hits = EntityManager.System<SharedPhysicsSystem>()
            .IntersectRay(mapId, ray, distance, proxy, false);

        (EntityUid Uid, float Distance) minHit = (default, float.MaxValue);

        foreach (var hit in hits)
        {
            var hitEnt = hit.HitEntity;

            if (!PhysQuery.TryComp(hitEnt, out var otherBody) || !FixQuery.TryComp(hitEnt, out var otherFix))
                continue;

            Fixture? hitFix = null;
            foreach (var kv in otherFix.Fixtures)
            {
                if (kv.Value.Hard)
                {
                    hitFix = kv.Value;
                    break;
                }
            }

            if (hitFix == null)
                continue;

            if (ShouldPreventHit(proxy, flight, sourceGrid, mapId, hitEnt))
                continue;

            var ourEv = new PreventCollideEvent(proxy, hitEnt, projBody, otherBody, projFix, hitFix);
            RaiseLocalEvent(proxy, ref ourEv);
            if (ourEv.Cancelled)
                continue;

            var otherEv = new PreventCollideEvent(hitEnt, proxy, otherBody, projBody, hitFix, projFix);
            RaiseLocalEvent(hitEnt, ref otherEv);
            if (otherEv.Cancelled)
                continue;

            if (hit.Distance < minHit.Distance)
                minHit = (hitEnt, hit.Distance);
        }

        if (minHit.Uid == default)
            return null;

        return (minHit.Uid, minHit.Distance);
    }

    public bool ShouldPreventHit(
        EntityUid ignore,
        SimulatedProjectileFlightData flight,
        EntityUid? sourceGrid,
        MapId mapId,
        EntityUid target)
    {
        if (TryComp<RequireProjectileTargetComponent>(target, out var requireTarget)
            && requireTarget.IgnoreThrow
            && requireTarget.Active)
        {
            return false;
        }

        if (flight.IgnoreShooter && (target == flight.Shooter || target == flight.Weapon))
            return true;

        if (sourceGrid.HasValue)
        {
            var targetGrid = Transform(target).GridUid;
            if (targetGrid.HasValue && sourceGrid == targetGrid)
                return true;
        }

        if (Transform(target).MapID != mapId)
            return true;

        var ev = new ProjectileCollisionAttemptEvent(ignore, target);
        RaiseLocalEvent(ref ev);
        if (ev.Cancelled)
            return true;

        return false;
    }

    public (Color Color, float Length, float Lifetime) GetTracerVisuals(EntityUid uid)
    {
        if (TryComp<TracerComponent>(uid, out var tracer))
            return (tracer.Color, tracer.Length, tracer.Lifetime);

        return (Color.White, 2f, 10f);
    }

    public float GetLifetime(EntityUid uid, SimulatedProjectileComponent sim)
    {
        if (TryComp<TimedDespawnComponent>(uid, out var despawn))
            return despawn.Lifetime;

        return sim.MaxDistance / 20f;
    }

    protected Vector2 GetFireDirection(Vector2 mapDirection, EntityUid gunUid)
    {
        if (mapDirection.LengthSquared() > 0.01f)
            return mapDirection.Normalized();

        return Transform.GetWorldRotation(gunUid).ToVec().Normalized();
    }
}
