using Content.Server.Lightning;
using Content.Server.Tesla.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Tesla.EntitySystems;

/// <summary>
/// Fires electric arcs at surrounding objects.
/// </summary>
public sealed class LightningArcShooterSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LightningArcShooterComponent, MapInitEvent>(OnShooterMapInit);
    }

    private void OnShooterMapInit(EntityUid uid, LightningArcShooterComponent component, ref MapInitEvent args)
    {
        component.NextShootTime = _gameTiming.CurTime + TimeSpan.FromSeconds(component.ShootMaxInterval);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LightningArcShooterComponent>();
        while (query.MoveNext(out var uid, out var arcShooter))
        {
            if (!arcShooter.Enabled)
                continue;
            if (arcShooter.NextShootTime > _gameTiming.CurTime)
                continue;

            ArcShoot(uid, arcShooter);
            var delay = TimeSpan.FromSeconds(_random.NextFloat(arcShooter.ShootMinInterval, arcShooter.ShootMaxInterval));
            arcShooter.NextShootTime += delay;
        }
    }

    private void ArcShoot(EntityUid uid, LightningArcShooterComponent component)
    {
        var arcs = _random.Next(1, component.MaxLightningArc);
        _lightning.ShootRandomLightnings(uid, component.ShootRange, arcs, component.LightningPrototype, component.ArcDepth);
    }

    /// <summary>
    /// Sets whether or not the LightningArcShooter is active, and will fire lightning
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="enabled"></param>
    public void SetEnabled(EntityUid uid, bool enabled)
    {
        var arcShooter = Comp<LightningArcShooterComponent>(uid);

        //If the arc shooter is being enabled, generate a new NextShootTime to prevent it from immediately firing (possibly many times)
        if (enabled && !arcShooter.Enabled)
        {
            var delay = TimeSpan.FromSeconds(_random.NextFloat(arcShooter.ShootMinInterval, arcShooter.ShootMaxInterval));
            arcShooter.NextShootTime = _gameTiming.CurTime + delay;
        }

        arcShooter.Enabled = enabled;
    }

    /// <summary>
    /// Sets the charge in watts of the lightning bolts, which will be used when they strike tesla coils
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="charge"></param>
    public void SetCharge(EntityUid uid, float charge)
    {
        var arcShooter = Comp<LightningArcShooterComponent>(uid);

        arcShooter.LightningCharge = charge;
    }
}
