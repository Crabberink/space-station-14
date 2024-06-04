using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server.Power.Generation.Supermatter;

/// <summary>
/// Supermatter delamination types from safest to most dangerous.
/// </summary>
public enum DelaminationType
{
    /// <summary>
    /// No ongoing delamination
    /// </summary>
    None = 0,
    /// <summary>
    /// The SM explodes when it delaminates
    /// </summary>
    Default = 1,

    /// <summary>
    /// The SM creates a singularity when it delaminates
    /// </summary>
    Singularity = 2,

    /// <summary>
    /// The SM creates a tesla when it delaminates
    /// </summary>
    Tesla = 3,
    /// <summary>
    /// The SM crystal slowly grows outward, consuming the entire station
    /// </summary>
    //ResonanceCascade = 4
}
