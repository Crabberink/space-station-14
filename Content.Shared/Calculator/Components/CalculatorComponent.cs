using Content.Shared.Calculator.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Calculator.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedCalculatorSystem))]
public sealed partial class CalculatorComponent : Component
{
    [Serializable, NetSerializable]
    public sealed class CalculatorBoundUserInterfaceState : BoundUserInterfaceState
    {
        // public CalculatorBoundUserInterfaceState()
        // {
        //
        // }

        public bool Equals(CalculatorBoundUserInterfaceState? other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;

            //No variables for now
            return true;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is CalculatorBoundUserInterfaceState other && Equals(other);
        }

        public override int GetHashCode()
        {
            //No variables for now
            return HashCode.Combine(0);
        }
    }

    [Serializable, NetSerializable]
    public enum CalculatorUiKey : byte
    {
        Key,
    }
}
