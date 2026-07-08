using System.Numerics;
using Content.Server._Forge.Projectiles;
using Content.Server._Mono.FireControl;
using Content.Shared._Forge.Projectiles;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private SimulatedProjectileSystem _simulated = default!;

    private bool TryFireSimulatedProjectile(
        EntityUid uid,
        Vector2 mapDirection,
        Vector2 gunVelocity,
        GunComponent gun,
        EntityUid gunUid,
        EntityUid? user,
        float offset)
    {
        if (!_simulated.CanUseSimulatedProjectile(uid))
            return false;

        var fromCoords = Transform(uid).Coordinates;
        var gridPhase = HasComp<FireControllableComponent>(gunUid);
        _simulated.Fire(uid, mapDirection, gunVelocity, gun.ProjectileSpeedModified, gunUid, user, offset, fromCoords, gridPhase);
        return true;
    }
}
