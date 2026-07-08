using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Projectiles;

/// <summary>
/// Snapshot of a simulated projectile used while no entity exists in the world.
/// </summary>
public sealed class SimulatedProjectileFlightData
{
    public EntProtoId Prototype = default!;
    public Angle Angle;
    public EntProtoId? ImpactEffect;
    public EntityUid? Shooter;
    public EntityUid? Weapon;
    public bool IgnoreShooter = true;
    public DamageSpecifier Damage = new();
    public bool DeleteOnCollide = true;
    public bool IgnoreResistances;
    public SoundSpecifier? SoundHit;
    public bool ForceSound;
    public bool OnlyCollideWhenShot;
    public bool ProjectileSpent;
    public FixedPoint2 PenetrationThreshold;
    public List<string>? PenetrationDamageTypeRequirement;
    public FixedPoint2 PenetrationAmount;
    public bool NoDamageDelete = true;
    public float ArmorPenetration;

    public static SimulatedProjectileFlightData From(EntityUid uid, ProjectileComponent comp, MetaDataComponent meta)
    {
        return new SimulatedProjectileFlightData
        {
            Prototype = meta.EntityPrototype?.ID ?? default!,
            Angle = comp.Angle,
            ImpactEffect = comp.ImpactEffect,
            Shooter = comp.Shooter,
            Weapon = comp.Weapon,
            IgnoreShooter = comp.IgnoreShooter,
            Damage = new DamageSpecifier(comp.Damage),
            DeleteOnCollide = comp.DeleteOnCollide,
            IgnoreResistances = comp.IgnoreResistances,
            SoundHit = comp.SoundHit,
            ForceSound = comp.ForceSound,
            OnlyCollideWhenShot = comp.OnlyCollideWhenShot,
            ProjectileSpent = comp.ProjectileSpent,
            PenetrationThreshold = comp.PenetrationThreshold,
            PenetrationDamageTypeRequirement = comp.PenetrationDamageTypeRequirement,
            PenetrationAmount = comp.PenetrationAmount,
            NoDamageDelete = comp.NoDamageDelete,
            ArmorPenetration = comp.ArmorPenetration,
        };
    }

    public void ApplyTo(ProjectileComponent comp)
    {
        comp.Angle = Angle;
        comp.ImpactEffect = ImpactEffect;
        comp.Shooter = Shooter;
        comp.Weapon = Weapon;
        comp.IgnoreShooter = IgnoreShooter;
        comp.Damage = new DamageSpecifier(Damage);
        comp.DeleteOnCollide = DeleteOnCollide;
        comp.IgnoreResistances = IgnoreResistances;
        comp.SoundHit = SoundHit;
        comp.ForceSound = ForceSound;
        comp.OnlyCollideWhenShot = OnlyCollideWhenShot;
        comp.ProjectileSpent = ProjectileSpent;
        comp.PenetrationThreshold = PenetrationThreshold;
        comp.PenetrationDamageTypeRequirement = PenetrationDamageTypeRequirement;
        comp.PenetrationAmount = PenetrationAmount;
        comp.NoDamageDelete = NoDamageDelete;
        comp.ArmorPenetration = ArmorPenetration;
    }

    public void SyncFrom(ProjectileComponent comp)
    {
        ProjectileSpent = comp.ProjectileSpent;
        PenetrationAmount = comp.PenetrationAmount;
    }
}
