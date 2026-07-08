using System.Numerics;
using Content.Shared._Forge.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._Forge.Projectiles;

/// <summary>
/// Client-side visuals for simulated projectiles (tracer lines without replicated flying entities).
/// </summary>
public sealed class SimulatedProjectileSystem : SharedSimulatedProjectileSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<int, ClientSimProjectile> _active = new();
    private readonly Queue<int> _predictedIds = new();
    private SimulatedProjectileOverlay? _tracerOverlay;
    private int _nextPredictId = -1;

    public override void Initialize()
    {
        base.Initialize();
        _tracerOverlay = new SimulatedProjectileOverlay(this);
        _overlayManager.AddOverlay(_tracerOverlay);

        SubscribeNetworkEvent<SimulatedProjectileSpawnEvent>(OnSpawn);
        SubscribeNetworkEvent<SimulatedProjectileEndEvent>(OnEnd);
        SubscribeLocalEvent<SimulatedProjectileComponent, ComponentStartup>(OnSimulatedStartup);
        SubscribeLocalEvent<GunShotEvent>(OnGunShot);
    }

    private void OnSimulatedStartup(EntityUid uid, SimulatedProjectileComponent component, ComponentStartup args)
    {
        // Predicted client-side ammo can be deleted locally; networked server proxies must only be hidden.
        if (IsClientSide(uid))
        {
            QueueDel(uid);
            return;
        }

        HideProxyEntity(uid);

        // SpriteComponent may start after SimulatedProjectile; defer hide if not ready yet.
        if (!HasComp<SpriteComponent>(uid))
        {
            Timer.Spawn(0, () =>
            {
                if (Exists(uid))
                    HideProxyEntity(uid);
            });
        }
    }

    private void HideProxyEntity(EntityUid uid)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetVisible((uid, sprite), false);

        if (PhysQuery.TryComp(uid, out var physics))
            _physics.SetCanCollide(uid, false, body: physics);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_tracerOverlay != null)
            _overlayManager.RemoveOverlay(_tracerOverlay);
    }

    private void OnGunShot(ref GunShotEvent ev)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (_player.LocalEntity != ev.User)
            return;

        if (!_guns.TryGetGun(ev.User, out var gunUid, out var gun))
            return;

        var fromCoordinates = Transform(ev.User).Coordinates;
        var fromMap = _transform.ToMapCoordinates(fromCoordinates);
        var toMap = _transform.ToMapCoordinates(ev.ToCoordinates).Position;
        var mapDirection = toMap - fromMap.Position;

        if (mapDirection.LengthSquared() < 0.01f)
            return;

        var fromEnt = _mapManager.TryFindGridAt(fromMap, out var gridUid, out _)
            ? _transform.WithEntityId(fromCoordinates, gridUid)
            : _transform.ToCoordinates(fromMap);

        var gunVelocity = _physics.GetMapLinearVelocity(gunUid) - _physics.GetMapLinearVelocity(fromEnt);
        var direction = GetFireDirection(mapDirection, gunUid);
        var tickSeconds = (float) _timing.TickPeriod.TotalSeconds;

        foreach (var (ent, _) in ev.Ammo)
        {
            if (ent == null || !CanUseSimulatedProjectile(ent.Value))
                continue;

            var sim = Comp<SimulatedProjectileComponent>(ent.Value);
            var velocity = gunVelocity + direction * gun.ProjectileSpeedModified;
            var origin = fromMap.Offset(direction * sim.SpawnOffset);
            var (color, length, lifetime) = GetTracerVisuals(ent.Value);

            var id = _nextPredictId--;
            _predictedIds.Enqueue(id);
            AddTracer(id, origin, velocity, color, length, lifetime, tickSeconds);
        }
    }

    private void OnSpawn(SimulatedProjectileSpawnEvent ev)
    {
        if (_predictedIds.TryDequeue(out var predictedId))
            _active.Remove(predictedId);

        var origin = _transform.ToMapCoordinates(ev.Origin);
        var tickSeconds = (float) _timing.TickPeriod.TotalSeconds;
        AddTracer(ev.Id, origin, ev.Velocity, ev.TracerColor, ev.TracerLength, ev.TracerLifetime, tickSeconds);
    }

    private void OnEnd(SimulatedProjectileEndEvent ev)
    {
        if (ev.FinalPosition != null
            && _active.TryGetValue(ev.Id, out var sim))
        {
            var final = _transform.ToMapCoordinates(ev.FinalPosition.Value).Position;
            sim.PositionHistory.Add(final);
        }

        _active.Remove(ev.Id);
    }

    private void AddTracer(
        int id,
        MapCoordinates origin,
        Vector2 velocity,
        Color color,
        float length,
        float lifetime,
        float tickSeconds)
    {
        var start = origin.Position;
        var end = start + velocity * tickSeconds;
        _active[id] = new ClientSimProjectile
        {
            Id = id,
            MapId = origin.MapId,
            PositionHistory = new List<Vector2> { start, end },
            Velocity = velocity,
            Color = color,
            Length = length,
            EndTime = _timing.CurTime + TimeSpan.FromSeconds(lifetime),
        };
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var expired = new List<int>();
        var dt = (float) _timing.TickPeriod.TotalSeconds;

        foreach (var (id, sim) in _active)
        {
            if (curTime > sim.EndTime)
            {
                expired.Add(id);
                continue;
            }

            var last = sim.PositionHistory[^1];
            sim.PositionHistory.Add(last + sim.Velocity * dt);

            while (sim.PositionHistory.Count > 2 && GetTrailLength(sim.PositionHistory) > sim.Length)
                sim.PositionHistory.RemoveAt(0);
        }

        foreach (var id in expired)
            _active.Remove(id);
    }

    public void Draw(DrawingHandleWorld handle, MapId currentMap)
    {
        handle.SetTransform(Matrix3x2.Identity);

        foreach (var sim in _active.Values)
        {
            if (sim.MapId != currentMap)
                continue;

            var positions = sim.PositionHistory;
            if (positions.Count < 2)
                continue;

            for (var i = 1; i < positions.Count; i++)
                handle.DrawLine(positions[i - 1], positions[i], sim.Color);
        }
    }

    private static float GetTrailLength(List<Vector2> positions)
    {
        var length = 0f;
        for (var i = 1; i < positions.Count; i++)
            length += Vector2.Distance(positions[i - 1], positions[i]);
        return length;
    }

    private sealed class ClientSimProjectile
    {
        public int Id;
        public MapId MapId;
        public List<Vector2> PositionHistory = new();
        public Vector2 Velocity;
        public Color Color;
        public float Length;
        public TimeSpan EndTime;
    }
}

public sealed class SimulatedProjectileOverlay : Robust.Client.Graphics.Overlay
{
    private readonly SimulatedProjectileSystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public SimulatedProjectileOverlay(SimulatedProjectileSystem system)
    {
        _system = system;
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _system.Draw(args.WorldHandle, args.MapId);
    }
}
