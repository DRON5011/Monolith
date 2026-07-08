using System.Numerics;
using Content.Server.Projectiles;
using Content.Shared._Forge.Projectiles;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Projectiles;

/// <summary>
/// Server-side simulated projectiles: segment raycast each tick without a flying physics body.
/// </summary>
public sealed class SimulatedProjectileSystem : SharedSimulatedProjectileSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ProjectileSystem _projectiles = default!;
    [Dependency] private TransformSystem _transform = default!;

    private readonly List<SimProjectileState> _active = new();
    private int _nextId = 1;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public void Fire(
        EntityUid proxy,
        Vector2 mapDirection,
        Vector2 gunVelocity,
        float speed,
        EntityUid gunUid,
        EntityUid? shooter,
        float offset,
        EntityCoordinates fromCoordinates,
        bool gridPhase)
    {
        if (!TryComp<ProjectileComponent>(proxy, out var projectile)
            || !TryComp<SimulatedProjectileComponent>(proxy, out var sim)
            || !TryComp<MetaDataComponent>(proxy, out var meta))
        {
            return;
        }

        var collisionMask = GetProjectileCollisionMask(proxy);
        var (tracerColor, tracerLength, tracerLifetime) = GetTracerVisuals(proxy);
        var lifetime = GetLifetime(proxy, sim);
        var maxDistance = sim.MaxDistance;
        var flight = SimulatedProjectileFlightData.From(proxy, projectile, meta);

        _projectiles.SetShooter(proxy, projectile, shooter ?? gunUid);
        projectile.Weapon = gunUid;
        flight.Shooter = projectile.Shooter;
        flight.Weapon = gunUid;

        var direction = GetFireDirection(mapDirection, gunUid);
        var velocity = gunVelocity + direction * speed;

        var mapCoords = _transform.ToMapCoordinates(fromCoordinates);
        var tickSeconds = (float) _timing.TickPeriod.TotalSeconds;
        if (offset != 0f)
            mapCoords = mapCoords.Offset(velocity * offset * tickSeconds);

        mapCoords = mapCoords.Offset(direction * sim.SpawnOffset);

        EntityUid? sourceGrid = null;
        if (gridPhase)
            sourceGrid = fromCoordinates.GetGridUid(EntityManager);

        // Keep the proxy on the server for PreventCollide checks, but disable physics movement.
        var proxyPhysics = EnsureComp<PhysicsComponent>(proxy);
        _physics.SetLinearVelocity(proxy, Vector2.Zero, body: proxyPhysics);
        _physics.SetBodyStatus(proxy, proxyPhysics, BodyStatus.OnGround);
        _physics.SetCanCollide(proxy, false, body: proxyPhysics);
        _transform.SetMapCoordinates(proxy, mapCoords);

        var id = _nextId++;
        var state = new SimProjectileState
        {
            Id = id,
            Proxy = proxy,
            Flight = flight,
            Position = mapCoords,
            Velocity = velocity,
            CollisionMask = collisionMask,
            SourceGrid = sourceGrid,
            SpawnTime = _timing.CurTime,
            MaxLifetime = TimeSpan.FromSeconds(lifetime),
            MaxDistance = maxDistance,
            DistanceTraveled = sim.SpawnOffset,
        };

        _active.Add(state);

        var filter = Filter.Pvs(fromCoordinates, entityMan: EntityManager);
        RaiseNetworkEvent(new SimulatedProjectileSpawnEvent(
            id,
            GetNetCoordinates(_transform.ToCoordinates(mapCoords)),
            velocity,
            tracerColor,
            tracerLength,
            tracerLifetime,
            direction.ToWorldAngle() + flight.Angle), filter);

        SimulateStep(_active.Count - 1, tickSeconds);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var dt = (float) _timing.TickPeriod.TotalSeconds;

        for (var i = _active.Count - 1; i >= 0; i--)
            SimulateStep(i, dt);
    }

    private void SimulateStep(int index, float dt)
    {
        if (index < 0 || index >= _active.Count)
            return;

        var state = _active[index];

        if (state.Flight.ProjectileSpent)
        {
            EndSimulation(ref state);
            _active.RemoveAt(index);
            return;
        }

        var elapsed = _timing.CurTime - state.SpawnTime;
        if (elapsed > state.MaxLifetime || state.DistanceTraveled >= state.MaxDistance)
        {
            EndSimulation(ref state);
            _active.RemoveAt(index);
            return;
        }

        var velLen = state.Velocity.Length();
        if (velLen <= 0f)
        {
            EndSimulation(ref state);
            _active.RemoveAt(index);
            return;
        }

        var rayDistance = velLen * dt;
        var step = state.Velocity / velLen * rayDistance;
        var hit = TryGetSegmentHit(
            state.Proxy,
            state.Flight,
            state.SourceGrid,
            state.Position.MapId,
            state.Position.Position,
            step,
            rayDistance,
            state.CollisionMask);

        if (hit != null)
        {
            var hitDir = step / rayDistance;
            var hitMap = state.Position.Offset(hitDir * hit.Value.Distance);
            state.DistanceTraveled += hit.Value.Distance;

            var spent = ProcessHit(ref state, hit.Value.Entity, hitMap);

            if (spent || state.Flight.ProjectileSpent)
            {
                EndSimulation(ref state, hitMap);
                _active.RemoveAt(index);
                return;
            }

            state.Position = hitMap.Offset(hitDir * 0.05f);
            state.DistanceTraveled += 0.05f;
            _active[index] = state;
            return;
        }

        state.Position = state.Position.Offset(step);
        state.DistanceTraveled += rayDistance;
        _active[index] = state;
    }

    private bool ProcessHit(ref SimProjectileState state, EntityUid target, MapCoordinates hitMap)
    {
        _transform.SetMapCoordinates(state.Proxy, hitMap);

        var projectile = Comp<ProjectileComponent>(state.Proxy);
        state.Flight.ApplyTo(projectile);

        var physics = Comp<PhysicsComponent>(state.Proxy);
        _physics.SetLinearVelocity(state.Proxy, state.Velocity, body: physics);

        _projectiles.ProjectileCollide((state.Proxy, projectile, physics), target, hitMap);
        state.Flight.SyncFrom(projectile);

        return state.Flight.ProjectileSpent;
    }

    private void EndSimulation(ref SimProjectileState state, MapCoordinates? finalPosition = null)
    {
        var coords = _transform.ToCoordinates(finalPosition ?? state.Position);
        var filter = Filter.Pvs(coords, entityMan: EntityManager);
        RaiseNetworkEvent(new SimulatedProjectileEndEvent(
            state.Id,
            GetNetCoordinates(coords)), filter);

        if (!TerminatingOrDeleted(state.Proxy))
            QueueDel(state.Proxy);
    }

    private sealed class SimProjectileState
    {
        public int Id;
        public EntityUid Proxy;
        public SimulatedProjectileFlightData Flight = new();
        public MapCoordinates Position;
        public Vector2 Velocity;
        public int CollisionMask;
        public EntityUid? SourceGrid;
        public TimeSpan SpawnTime;
        public TimeSpan MaxLifetime;
        public float MaxDistance;
        public float DistanceTraveled;
    }
}
