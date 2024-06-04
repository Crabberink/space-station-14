using Content.Server.Administration.Logs;
using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Audio;
using Content.Server.Chat.Managers;
using Content.Server.DeviceNetwork;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Item;
using Content.Server.Power.Components;
using Content.Server.Sound;
using Content.Server.Tesla.Components;
using Content.Server.Tesla.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.Examine;
using Content.Shared.Flash.Components;
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
using Robust.Shared.Toolshed.Commands.Debug;
using Robust.Shared.Timing;
using Content.Server.Chat.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Radio;
using Content.Shared.Chat;
using Content.Shared.Physics;
using Robust.Shared.Audio;
using Content.Server.Chemistry.Components;

namespace Content.Server.Power.Generation.Supermatter;

//Ported from /tg/station13, which was ported from /vg/station13, which was forked from baystation12
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
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly LightningArcShooterSystem _lightning = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly PointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly AmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    //Used in power loss calculations, the same for all SMs
    const float PowerlossLinearRate = 0.83f;
    const float PowerlossCubicDivisor = 500f;
    float _PowerlossLinearThreshold;
    float _PowerlossLinearOffset;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupermatterComponent, AtmosDeviceUpdateEvent>(SupermatterUpdate);
        SubscribeLocalEvent<SupermatterComponent, StartCollideEvent>(OnCollision);
        SubscribeLocalEvent<SupermatterComponent, PreventCollideEvent>(OnPreventCollision);

        //I'm not 100% sure what this does but I think it calculates the value at which cubic powerloss would result in negative values
        _PowerlossLinearThreshold = MathF.Sqrt(PowerlossLinearRate / 3f * MathF.Pow(PowerlossCubicDivisor, 3f));
        _PowerlossLinearOffset = -_PowerlossLinearThreshold * PowerlossLinearRate + MathF.Pow(_PowerlossLinearThreshold / PowerlossCubicDivisor, 3f);
    }

    private void SupermatterUpdate(EntityUid uid, SupermatterComponent supermatter, ref AtmosDeviceUpdateEvent args)
    {
        //Activate the SM if the external power is gained
        if (!supermatter.Activated && supermatter.ExternalPowerGain > 0f)
        {
            _adminLogger.Add(LogType.SupermatterActivation, LogImpact.Medium, $"Supermatter {ToPrettyString(uid)} has been activated!");
            supermatter.Activated = true;

            //Begin zapping tesla coils (or engineers who didn't set everything up)
            _lightning.SetEnabled(uid, true);

            //Prepare the supermatter warning timer
            supermatter.NextWarnTime = _gameTiming.CurTime;
        }

        //The SM isn't active, so skip processing
        if (!supermatter.Activated)
            return;

        var position = _transform.GetGridTilePositionOrDefault(uid);
        var environment = _atmos.GetTileMixture(args.Grid, args.Map, position, true);

        if (environment == null)
            return;

        //Process Power and Internal Energy
        CalculateInternalEnergy(supermatter);
        float zapTransmission = CalculateZapTransmission(supermatter);
        float zapPower = supermatter.InternalEnergy * zapTransmission;
        _lightning.SetCharge(uid, zapPower);

        //Process damage and set the delam type
        supermatter.DelamType = CalculateDamage(supermatter);

        //Delaminate at 0 integrity
        if (supermatter.Damage >= 100)
        {
            Delaminate(uid, supermatter);
            //We just exploded so no point in continuing
            return;
        }

        //Update the internal gas, release it, and absorb more
        ExchangeGas(supermatter, environment);
        float previousDamage = supermatter.Damage;

        //Find the warning level for the alerts
        CalculateWarningLevel(supermatter);

        //Alert the crew if the integrity is dropping
        ProcessDamageAlert(uid, supermatter);

        UpdateAppearance(uid, supermatter);
    }

    private void ProcessDamageAlert(EntityUid uid, SupermatterComponent supermatter)
    {
        //The warning level is safe, so don't send any alerts
        if (supermatter.WarningLevel < SupermatterWarningLevel.Warning)
            return;

        if(supermatter.NextWarnTime > _gameTiming.CurTime)
            return;

        supermatter.NextWarnTime = _gameTiming.CurTime + supermatter.WarningInterval;

        string integrity = (100 - supermatter.Damage).ToString("F2");

        string warning;

        //The supermatter warning level is emergency or higher, so send the emergency message
        if (supermatter.WarningLevel >= SupermatterWarningLevel.Emergency)
        {
            warning = Loc.GetString("supermatter-integrity-message-emergency", ("integrity", integrity));
        }
        else
        {
            warning = Loc.GetString("supermatter-integrity-message-warning", ("integrity", integrity));
        }

        //The supermatter is healing, so override the alert with a healing one
        if (supermatter.Damage < supermatter.PreviousDamage)
        {
            warning = Loc.GetString("supermatter-integrity-message-healing", ("integrity", integrity));
        }

        _radio.SendRadioMessage(uid, warning, supermatter.AlertRadioChannel, uid);

        _chat.TrySendInGameICMessage(
            source: uid,
            message: warning,
            desiredType: InGameICChatType.Speak,
            hideChat: false,
            ignoreActionBlocker: true);

        // Play alarm sound effect based on warning level
        switch (supermatter.WarningLevel)
        {
            case SupermatterWarningLevel.Warning:
                _audio.PlayPvs(supermatter.WarningAlarmSound, uid);
                break;
            case SupermatterWarningLevel.Danger:
                _audio.PlayPvs(supermatter.DangerAlarmSound, uid);
                break;
            case SupermatterWarningLevel.Emergency:
                _audio.PlayPvs(supermatter.EmergencyAlarmSound, uid);
                break;
        }

    }

    /// <summary>
    /// Calculates and updates the WarningLevel and AlertRadioChannel of the SM
    /// </summary>
    private void CalculateWarningLevel(SupermatterComponent supermatter)
    {
        if (supermatter.Damage < supermatter.WarningPoint)
        {
            supermatter.WarningLevel = SupermatterWarningLevel.Safe;
            supermatter.AlertRadioChannel = "Engineering";
            return;
        }
        
        if (supermatter.Damage < supermatter.DangerPoint)
        {
            supermatter.WarningLevel = SupermatterWarningLevel.Warning;
            supermatter.AlertRadioChannel = "Engineering";
            return;
        }

        if (supermatter.Damage < supermatter.EmergencyPoint)
        {
            supermatter.WarningLevel = SupermatterWarningLevel.Danger;
            supermatter.AlertRadioChannel = "Engineering";
            return;
        }

        if (supermatter.Damage < 100)
        {
            supermatter.WarningLevel = SupermatterWarningLevel.Emergency;
            supermatter.AlertRadioChannel = "Common";
            return;
        }

        supermatter.WarningLevel = SupermatterWarningLevel.Delamination;
    }

    /// <summary>
    /// Calculates and updates the InternalEnergy of the SM
    /// </summary>
    private void CalculateInternalEnergy(SupermatterComponent supermatter)
    {
        //Calculate power gain based on factors such as temp, gas, energy, and external gain
        float externalPower = supermatter.ExternalPowerGain;
        //External power gain is the measure of the energy gained from external sources since the last update, so reset it to 0
        supermatter.ExternalPowerGain = 0f; 

        float heatPower = CalculateGasHeatPowerGeneration(supermatter) * supermatter.GasMix.Temperature * supermatter.GasHeatPowerScale;

        //Power loss is calculated immediately after power gain to deal with cases where hot gas is immediately dumped into the SM and the power shoots up

        //The energy with no powerloss, used in powerloss calculation
        float momentaryPower = supermatter.InternalEnergy;
        momentaryPower += externalPower + heatPower;

        float powerloss;
        //A bunch of math from the tgstation SM that just exponentially increases energy loss unless its above a threshold.
        //If the threshold is reached, the energy loss calculation becomes linear.
        //From what I understand this is to prevent the power loss growing exponentially until more energy is removed than whats available, resulting in a negative energy value
        if (momentaryPower < _PowerlossLinearThreshold)
        {
            powerloss = -1f * MathF.Pow(momentaryPower / PowerlossCubicDivisor, 3f);
        }
        else
        {
            powerloss = -1f * (momentaryPower * PowerlossLinearRate + _PowerlossLinearOffset);
        }

        //Positive
        float gasPowerlossInhibition = -1 * CalculateGasPowerlossInhibition(supermatter) * powerloss;

        //External power gain
        supermatter.InternalEnergy += externalPower;
        //Heat power gain
        supermatter.InternalEnergy += heatPower;
        //Power loss
        supermatter.InternalEnergy += powerloss;
        //Powerloss negation from gases
        supermatter.InternalEnergy += gasPowerlossInhibition;
    }

    /// <summary>
    /// Calculates ZapTransmission using values from internal gasses and BasePowerTransmission
    /// </summary>
    private float CalculateZapTransmission(SupermatterComponent supermatter)
    {
        float gasZapTransmission = supermatter.BasePowerTransmission * CalculateGasTransmissionRate(supermatter);

        float zapTransmission = supermatter.BasePowerTransmission + gasZapTransmission;

        zapTransmission = Math.Max(zapTransmission, 0f);

        return zapTransmission;
    }

    /// <summary>
    /// Calculates and applies damage based on temp, mols, and energy
    /// </summary>
    private DelaminationType CalculateDamage(SupermatterComponent supermatter)
    {
        //The delamination type that the damage is causing
        DelaminationType delaminationType = DelaminationType.None;

        float tempLimit = CalculateTempLimit(supermatter);

        //Calculate overheat damage which ranges from 0-0.15 damage per update
        float heatDamage = Math.Clamp((supermatter.GasMix.Temperature - tempLimit) / 24000f, 0f, 0.15f);

        //Calculate overcharge damage which ranges from 0-0.1 damage per update
        float powerDamage = Math.Clamp((supermatter.InternalEnergy - supermatter.PowerPenaltyThreshold) / 40000f, 0f, 0.1f);

        //Calculate mole damage which ranges from 0-0.1 damage per update
        float molDamage = Math.Clamp((supermatter.GasMix.TotalMoles)/3200f, 0f, 0.1f);

        //Calculate space damage if near space which ranges from 0-1 damage per update
        // float spaceDamage = Math.Clamp(supermatter.InternalEnergy * 0.000125, 0f, 1f);

        //Calculate low temperature damage healing which ranges from 0-0.1 damage per update
        float temperatureHeal = Math.Clamp((supermatter.GasMix.Temperature - tempLimit) / 6000f, -0.1f, 0);

        float damageDelta = heatDamage + powerDamage + molDamage + temperatureHeal;

        supermatter.Damage += damageDelta;

        //Damage is being taken so set the delam type to Default
        if (damageDelta > 0f)
            delaminationType = DelaminationType.Default;

        //A singulo delamination takes priority over a default delamination
        if (molDamage > 0f)
            delaminationType = DelaminationType.Singularity;

        //A tesla delamination takes priority over a singulo delamination
        if (powerDamage > 0f)
            delaminationType = DelaminationType.Tesla;

        supermatter.PreviousDamage = supermatter.Damage;

        //Prevent damage from being lower than 0 (Integrity >100%)
        supermatter.Damage = MathF.Max(supermatter.Damage, 0);

        return delaminationType;
    }

    /// <summary>
    /// Calculates the temperature at which the supermatter will begin taking damage
    /// </summary>
    private float CalculateTempLimit(SupermatterComponent supermatter)
    {
        float baseTempLimit = Atmospherics.T0C + supermatter.HeatPenaltyThreshold;
        float gasHeatResistance = CalculateGasHeatResistance(supermatter) * baseTempLimit;
        //Temp limit boost with low mols
        float lowMoleTempLimit = Math.Clamp(2 - supermatter.GasMix.TotalMoles / 100, 0f, 1f) * baseTempLimit;

        float tempLimit = baseTempLimit + gasHeatResistance + lowMoleTempLimit;

        tempLimit = Math.Max(tempLimit, Atmospherics.TCMB);

        return tempLimit;
    }

    /// <summary>
    /// Calculates and returns the heat power generation value from internal gasses
    /// </summary>
    private float CalculateGasHeatPowerGeneration(SupermatterComponent supermatter)
    {
        var mix = supermatter.GasMix;

        float totalMoles = mix.TotalMoles;

        // If there is no gas then the result is 0
        if (totalMoles == 0)
            return 0f;

        //List of all gas IDs
        Gas[] gasIDs = Enum.GetValues<Gas>();

        float heatPowerGeneration = 0f;

        foreach(Gas gas in gasIDs)
        {
            float moles = mix.GetMoles(gas);
            float gasPercentage = (moles / totalMoles);

            var gasPrototype = _atmos.GetGas(gas);

            heatPowerGeneration += gasPrototype.SupermatterHeatPowerGeneration * gasPercentage;
        }

        heatPowerGeneration = Math.Clamp(heatPowerGeneration, 0f, 1f);

        return heatPowerGeneration;
    }

    /// <summary>
    /// Calculates and returns the powerloss inhibition value from internal gasses
    /// </summary>
    private float CalculateGasPowerlossInhibition(SupermatterComponent supermatter)
    {
        var mix = supermatter.GasMix;

        float totalMoles = mix.TotalMoles;

        // If there is no gas then the result is 0
        if (totalMoles == 0)
            return 0f;

        //List of all gas IDs
        Gas[] gasIDs = Enum.GetValues<Gas>();

        float powerlossInhibition = 0f;

        foreach (Gas gas in gasIDs)
        {
            float moles = mix.GetMoles(gas);
            float gasPercentage = (moles / totalMoles);

            var gasPrototype = _atmos.GetGas(gas);

            powerlossInhibition += gasPrototype.SupermatterPowerlossInhibition * gasPercentage;
        }

        powerlossInhibition = Math.Clamp(powerlossInhibition, 0f, 1f);

        return powerlossInhibition;
    }

    /// <summary>
    /// Calculates and returns the transmission rate value from internal gasses
    /// </summary>
    private float CalculateGasTransmissionRate(SupermatterComponent supermatter)
    {
        var mix = supermatter.GasMix;

        float totalMoles = mix.TotalMoles;

        // If there is no gas then the result is 0
        if (totalMoles == 0)
            return 0f;

        //List of all gas IDs
        Gas[] gasIDs = Enum.GetValues<Gas>();

        float transmissionRate = 0f;

        foreach (Gas gas in gasIDs)
        {
            float moles = mix.GetMoles(gas);
            float gasPercentage = (moles / totalMoles);

            var gasPrototype = _atmos.GetGas(gas);

            transmissionRate += gasPrototype.SupermatterPowerTransmission * gasPercentage;
        }

        return transmissionRate;
    }

    /// <summary>
    /// Calculates and returns the heat modifier value from internal gasses
    /// </summary>
    private float CalculateGasHeatModifier(SupermatterComponent supermatter)
    {
        var mix = supermatter.GasMix;

        float totalMoles = mix.TotalMoles;

        // If there is no gas then the result is 0
        if (totalMoles == 0)
            return 0f;

        //List of all gas IDs
        Gas[] gasIDs = Enum.GetValues<Gas>();

        float heatModifier = 0f;

        foreach (Gas gas in gasIDs)
        {
            float moles = mix.GetMoles(gas);
            float gasPercentage = (moles / totalMoles);

            var gasPrototype = _atmos.GetGas(gas);

            heatModifier += gasPrototype.SupermatterHeatModifier * gasPercentage;
        }

        return heatModifier;
    }

    /// <summary>
    /// Calculates and returns the heat resistance value from internal gasses
    /// </summary>
    private float CalculateGasHeatResistance(SupermatterComponent supermatter)
    {
        var mix = supermatter.GasMix;

        float totalMoles = mix.TotalMoles;

        // If there is no gas then the result is 0
        if (totalMoles == 0)
            return 0f;

        //List of all gas IDs
        Gas[] gasIDs = Enum.GetValues<Gas>();

        float heatModifier = 0f;

        foreach (Gas gas in gasIDs)
        {
            float moles = mix.GetMoles(gas);
            float gasPercentage = (moles / totalMoles);

            var gasPrototype = _atmos.GetGas(gas);

            heatModifier += gasPrototype.SupermatterHeatResistance * gasPercentage;
        }

        return heatModifier;
    }

    /// <summary>
    /// Calculates the waste multiplier, used in waste gas processing
    /// </summary>
    /// <see cref="UpdateInternalGas(SupermatterComponent)"/>
    private float CalculateWasteMultiplier(SupermatterComponent supermatter)
    {
        float wasteMultiplier = 1 + CalculateGasHeatModifier(supermatter);
        wasteMultiplier = MathF.Min(wasteMultiplier, 0.5f);

        return wasteMultiplier;
    }

    /// <summary>
    /// Updates the internal gasmix, releases it to the air, and absorbs more gas from the environment
    /// </summary>
    private void ExchangeGas(SupermatterComponent supermatter, GasMixture environment)
    {
        //Get the gas to be absorbed before releasing waste gas so we aren't just immediately absorbing the waste gas
        var absorbedGas = AbsorbGas(supermatter, environment);

        UpdateInternalGas(supermatter);
        _atmos.Merge(environment, supermatter.GasMix);
        supermatter.GasMix.Clear();

        _atmos.Merge(supermatter.GasMix, absorbedGas);
    }

    /// <summary>
    /// Adds waste gas to the internal gasmix of the SM and heats it
    /// </summary>
    private void UpdateInternalGas(SupermatterComponent supermatter)
    {
        //Scale the internal energy based on ReactionPowerModifer so that the gas and heat output can be adjusted to acceptable levels
        float deviceEnergy = supermatter.InternalEnergy * supermatter.ReactionPowerModifier;

        float wasteMultiplier = CalculateWasteMultiplier(supermatter);

        //Since the SM generates plasma and oxygen, we calculate how much to add adjust the moles in the gasmix
        float plasmaMoles = MathF.Max(deviceEnergy * wasteMultiplier / supermatter.PlasmaReleaseModifier, 0f);
        float oxygenMoles = MathF.Max(((deviceEnergy + supermatter.GasMix.Temperature * wasteMultiplier)-Atmospherics.T0C)/ supermatter.OxygenReleaseModifier, 0f);

        float temperatureGain = deviceEnergy * wasteMultiplier / supermatter.ThermalReleaseModifier;

        //Calculate the dQ of the temperatureGain
        float Cp = _atmos.GetHeatCapacity(supermatter.GasMix, false);
        float dQ = Cp * temperatureGain;

        _atmos.AddHeat(supermatter.GasMix, dQ);

        supermatter.GasMix.AdjustMoles(Gas.Plasma, plasmaMoles);
        supermatter.GasMix.AdjustMoles(Gas.Oxygen, oxygenMoles);
    }

    /// <summary>
    /// Removes a portion of gas from the air based on the AbsorptionRatio and returns it;
    /// </summary>
    /// <returns>
    /// The gas to be absorbed
    /// </returns>
    private GasMixture AbsorbGas(SupermatterComponent supermatter, GasMixture environment)
    {
        var absorbedAir = environment.RemoveRatio(supermatter.AbsorptionRatio);

        return absorbedAir;
    }

    private void Delaminate(EntityUid uid, SupermatterComponent supermatter)
    {
        //For now just delete it
        EntityManager.QueueDeleteEntity(uid);
    }

    private void UpdateAppearance(EntityUid uid, SupermatterComponent supermatter)
    {
        //Once the SM is active, enable the point light so it glows
        _pointLight.SetEnabled(uid, supermatter.Activated);
    }

    //When something collides with the SM, delete it and increase the internal energy
    private void OnCollision(EntityUid uid, SupermatterComponent supermatter, ref StartCollideEvent args)
    {
        var otherEntity = args.OtherEntity;

        //If the object that the SM collided with is static, then the SM ignores it.
        //Prevents someone from grabbing a shard and tearing down the station's walls
        if (TryComp(otherEntity, out PhysicsComponent? physicsComponent))
        {
            if (physicsComponent.BodyType == BodyType.Static)
                return;
        }

        //Increase the ExternalPowerGain when something is consumed, which will then be used to calculate internal energy gain
        supermatter.ExternalPowerGain += 100;

        EntityManager.QueueDeleteEntity(otherEntity);

        //If the collided entity has the NoSoundTag, skip playing the sound
        if (_tags.HasTag(otherEntity, supermatter.NoSoundTag))
            return;

        //Play the sound effect for consuming an object
        _audio.PlayPvs(supermatter.ObjectDustedSound, uid);
    }

    //Prevent collisions with vapor from fire extinguishers
    private void OnPreventCollision(EntityUid uid, SupermatterComponent supermatter, ref PreventCollideEvent args)
    {
        if(HasComp<VaporComponent>(args.OtherEntity))
        {
            args.Cancelled = true;
        }
    }
}
