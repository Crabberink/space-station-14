using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server.Power.Generation.Supermatter;
public enum SupermatterWarningLevel
{
    /// <summary>
    /// No integrity loss
    /// </summary>
    Safe = 0,
    /// <summary>
    /// Some integrity loss
    /// </summary>
    Warning = 1,
    /// <summary>
    /// High integrity loss
    /// </summary>
    Danger = 2,
    /// <summary>
    /// Extreme integrity loss
    /// </summary>
    Emergency = 3,
    /// <summary>
    /// Integrity below 0
    /// </summary>
    Delamination = 4
}
