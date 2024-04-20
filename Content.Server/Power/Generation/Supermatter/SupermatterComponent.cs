using Robust.Shared.Audio;
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
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("absorptionRatio")]
    public float AbsorptionRatio = 0.15f;

    /// <summary>
    /// The amount of internal energy in the supermatter measured in GeV. This value affects gas output, damage, and power generation.
    /// </summary>
    /// <remarks>
    /// This value starts off at zero, meaning the SM is inactive.
    /// </remarks>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("internalEnergy")]
    public float InternalEnergy = 0f;

    /// <summary>
    /// The amount of damage the crystal has received.
    /// When this value reaches 100, the SM will begin a countdown before delaminating.
    /// </summary>
    /// <remarks>
    /// This value can extend past 100, and a delamination will not be halted until it returns below 100
    /// </remarks>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("damage")]
    public float Damage = 0f;

    /// <summary>
    /// The amount of damage the crystal had last update. This is used to check if the SM is taking damage or healing
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float PreviousDamage = 0f;

    /// <summary>
    /// The sound that plays when an object is destroyed by the SM
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("dustSound")]
    public SoundSpecifier ObjectDustedSound = new SoundPathSpecifier("/Audio/Effects/supermatter_consume.ogg");

    /// <summary>
    /// If an object with this tag collides with the SM, it will play no sound.
    /// Only used to make emitters not destroy everyones eardrums
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("noCollisionSoundTag")]
    public string NoSoundTag = "EmitterBolt";

    /// <summary>
    /// This is the temperature (Kelvin) at which the crystal will begin taking damage 
    /// </summary>
    /// <remarks>
    /// It starts at 40 C
    /// </remarks>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("temperatureLimit")]
    public float TemperatureLimit = 313.15f;

    /// <summary>
    /// Multiplies the amount and temperature of waste gas
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("wasteMultiplier")]
    public float WasteMultiplier = 0f;

    /// <summary>
    /// This value is used to determine the wattage of the electrical arcs discharged by the SM by default.
    /// It represents the watts per MeV of internal energy (W/MeV).
    /// </summary>
    /// <seealso cref="InternalEnergy"></seealso>
    /// <seealso cref="ZapPowerTransmission"></seealso>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("zapTransmissionRate")]
    public float ZapTransmissionRate = 1040f;

    /// <summary>
    /// This value is used to determine the wattage of the electrical arcs discharged by the SM based on the absorbed gases
    /// It represents the watts per MeV of internal energy (W/MeV)
    /// </summary>
    /// <seealso cref="InternalEnergy"></seealso>
    /// <seealso cref="ZapPowerTransmission"></seealso>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("gasPowerTransmissionRate")]
    public float GasPowerTransmissionRate = 0f;

    /// <summary>
    /// The watts of power that the electrical arcs discharged by the SM carry.
    /// This value is calculated using the InternalEnergy, ZapTransmissionRate, and GasPowerTransmissionRate
    /// </summary>
    /// <seealso cref="InternalEnergy"></seealso>
    /// <seealso cref="ZapTransmissionRate"></seealso>
    /// <seealso cref="GasPowerTransmissionRate"></seealso>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("zapPowerTransmission")]
    public float ZapPowerTransmission = 0;

    /// <summary>
    /// Whether or not the crystal has been activated (something has given it internal energy)
    /// </summary>
    /// <seealso cref="InternalEnergy"></seealso>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("activated")]
    public bool Activated = false;
}
