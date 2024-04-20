using Content.Server.Administration.Logs;
using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Audio;
using Content.Server.DeviceNetwork;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Item;
using Content.Server.Power.Components;
using Content.Server.Sound;
using Content.Server.Tesla.Components;
using Content.Server.Tesla.EntitySystems;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Rounding;
using Content.Shared.Tag;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Exceptions;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;

namespace Content.Server.Power.Generation.Supermatter;

/// <summary>
/// Handles processing logic for the Supermatter and supermatter shards.
/// </summary>
/// <para>
/// The Supermatter generates power by periodically discharging high voltage arcs which are received by tesla coils to generate power.
/// Alongside this, it emits dangerous amounts of radiation. (Not yet though)
/// It also absorbs part of the atmosphere around it, heats it, and expells it alongside a flammable mixture of plasma and oxygen
/// </para>
/// <seealso cref="SupermatterComponent"/>
public sealed class SupermatterSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly LightningArcShooterSystem _lightning = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly PointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly AmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupermatterComponent, AtmosDeviceUpdateEvent>(SupermatterUpdate);
        SubscribeLocalEvent<SupermatterComponent, StartCollideEvent>(OnCollision);
    }

    private void SupermatterUpdate(EntityUid uid, SupermatterComponent supermatter, ref AtmosDeviceUpdateEvent args)
    {
        //Activate the SM if the internal energy is greater than 0
        if(!supermatter.Activated && supermatter.InternalEnergy > 0f)
        {
            _adminLogger.Add(LogType.SupermatterActivation, LogImpact.Medium, $"Supermatter {ToPrettyString(uid)} has been activated!");
            supermatter.Activated = true;

            //Begin zapping tesla coils (or engineers who didn't set everything up)
            _lightning.SetEnabled(uid, true);
        }

        //The SM isn't active, so skip processing
        if (!supermatter.Activated) { return; }

        //Calculate the internal energy decay
        float energyDecay = MathF.Pow(supermatter.InternalEnergy / 4f, 3f);

        supermatter.InternalEnergy -= energyDecay;

        //Calculate the amount of energy the SM will emit with each zap. ZapTransmissionRate is multiplied by 1000 because its in MeV while InternalEnergy is in GeV
        supermatter.ZapPowerTransmission = supermatter.ZapTransmissionRate * 1000f * supermatter.InternalEnergy;

        UpdateAppearance(uid, supermatter);
    }

    private void UpdateAppearance(EntityUid uid, SupermatterComponent supermatter)
    {
        //Once the SM is active, enable the point light so it glows
        _pointLight.SetEnabled(uid, supermatter.Activated);
    }

    //When something collides with the SM, delete it and increase the internal energy
    private void OnCollision(EntityUid uid, SupermatterComponent supermatter, StartCollideEvent args)
    {
        var otherEntity = args.OtherEntity;

        //If the object that the shard collided with is static, then the SM ignores it
        if(EntityManager.HasComponent<PhysicsComponent>(otherEntity))
        {
            if(EntityManager.GetComponent<PhysicsComponent>(otherEntity).BodyType == BodyType.Static)
            {
                return;
            }
        }

        //Increase the internal energy when something is consumed
        supermatter.InternalEnergy += 0.1f;

        EntityManager.QueueDeleteEntity(otherEntity);

        //If the collided entity has the NoSoundTag, skip playing the sound
        if(_tags.HasTag(otherEntity,supermatter.NoSoundTag)) { return; }

        //Play the sound effect for consuming an object
        _audio.PlayPvs(supermatter.ObjectDustedSound, uid);
    }
}
