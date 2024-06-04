using Content.Shared.Atmos;
using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server.Power.Generation.Supermatter;

/// <summary>
/// The supermatter crystal.
/// </summary>
/// <seealso cref="SupermatterSystem"/>
///s
[RegisterComponent]
public sealed partial class SupermatterComponent : Component
{
    /// <summary>
    /// The percentage of gas from the tile we're on that should be absorbed
    /// </summary>
    /// <remarks>
    /// A value of 0.15 means 15% of gas is absorbed
    /// </remarks>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float AbsorptionRatio = 0.15f;

    /// <summary>
    /// The amount of internal energy in the supermatter measured in MeV. This value affects gas output, damage, and power generation.
    /// </summary>
    /// <remarks>
    /// This value starts off at zero, meaning the SM is inactive.
    /// </remarks>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float InternalEnergy = 0f;

    /// <summary>
    /// The amount of damage the crystal has received.
    /// When this value reaches 100, the SM will delaminate.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Damage = 0f;

    /// <summary>
    /// The amount of damage the crystal has received.
    /// When this value reaches 100, the SM will delaminate.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PreviousDamage = 0f;

    /// <summary>
    /// Higher values mean less waste gas and heat is released
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ReactionPowerModifier = 0.65f;

    /// <summary>
    /// Higher values mean less plasma released
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PlasmaReleaseModifier = 650f;

    /// <summary>
    /// Higher values mean less oxygen released
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float OxygenReleaseModifier = 340f;

    /// <summary>
    /// Higher values mean less heat released
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ThermalReleaseModifier = 4f;

    /// <summary>
    /// The amount of energy gained from external sources such as emitters
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float ExternalPowerGain = 0f;

    /// <summary>
    /// InternalEnergy is multiplied by this to get the watts of power per zap. Measured in W/MeV
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float BasePowerTransmission = 1040f;

    /// <summary>
    /// Higher value means higher safe operational temperature
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float HeatPenaltyThreshold = 40f;

    /// <summary>
    /// When the mols the SM has absorbed exceeds this amount it will begin taking damage and delamming into a singulo
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MolePenaltyThreshold = 1800f;

    /// <summary>
    /// When the SMs internal energy exceeds this value, it will take damage and begin delamming into a tesla
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PowerPenaltyThreshold = 5000f;

    /// <summary>
    /// Multiplies the amount and temperature of waste gas
    /// </summary>
    [DataField,ViewVariables(VVAccess.ReadWrite)]
    public float WasteMultiplier = 0f;

    /// <summary>
    /// Scales the power gain from temperature caused by some gasses
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float GasHeatPowerScale = 1 / 6f;

    /// <summary>
    /// Whether or not the crystal has been activated (something has given it internal energy)
    /// </summary>
    /// <seealso cref="InternalEnergy"></seealso>
    [DataField,ViewVariables(VVAccess.ReadWrite)]
    public bool Activated = false;

    /// <summary>
    /// The current type of delamination the SM is experiencing
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public DelaminationType DelamType = DelaminationType.None;

    /// <summary>
    /// The internal gasmix of the supermatter
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("gasMixture")]
    public GasMixture GasMix = new();

    /// <summary>
    /// The sound that plays when an object is destroyed by the SM
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("dustSound")]
    public SoundSpecifier ObjectDustedSound = new SoundPathSpecifier("/Audio/Effects/supermatter_consume.ogg");

    /// <summary>
    /// The warning sound that plays when the sm is losing integrity
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier WarningAlarmSound = new SoundPathSpecifier("/Audio/Machines/supermatter_warning.ogg");

    /// <summary>
    /// The warning sound that plays when the sm integrity is at levels specified by <see cref="DangerPoint"/>
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier DangerAlarmSound = new SoundPathSpecifier("/Audio/Machines/supermatter_danger.ogg");

    /// <summary>
    /// The warning sound that plays when the sm integrity is at levels specified by <see cref="EmergencyPoint"/>
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier EmergencyAlarmSound = new SoundPathSpecifier("/Audio/Machines/supermatter_emergency.ogg");

    /// <summary>
    /// The warning sound that plays when the sm begins delaminating
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier DelaminationAlarmSound = new SoundPathSpecifier("/Audio/Machines/supermatter_delam_alarm.ogg");

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SupermatterWarningLevel WarningLevel = SupermatterWarningLevel.Safe;

    /// <summary>
    /// The amount of time between warning messages
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan WarningInterval = TimeSpan.FromSeconds(15f);

    /// <summary>
    /// The next time point that needs pass to send an alert
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextWarnTime = TimeSpan.Zero;

    /// <summary>
    /// The radio channel the SM is using when sending alerts
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<RadioChannelPrototype> AlertRadioChannel = "Engineering";

    /// <summary>
    /// If an object with this tag collides with the SM, it will play no sound.
    /// Only used to make emitters not destroy everyones eardrums
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("noCollisionSoundTag")]
    public string NoSoundTag = "EmitterBolt";

    /// <summary>
    /// The damage at which the supermatter warning level will be considered warning
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float WarningPoint = 5f;

    /// <summary>
    /// The damage at which the supermatter warning level will be considered danger
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DangerPoint = 60f;

    /// <summary>
    /// The damage at which the supermatter warning level will be considered emergency
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float EmergencyPoint = 75f;
}
