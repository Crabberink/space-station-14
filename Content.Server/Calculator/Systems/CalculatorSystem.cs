using Content.Shared.Calculator.Components;
using Content.Shared.Calculator.Systems;
using Robust.Server.GameObjects;
using static Content.Shared.Calculator.Components.CalculatorComponent;

namespace Content.Server.Calculator.Systems;

public sealed class CalculatorSystem : SharedCalculatorSystem
{
    [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<CalculatorComponent>(CalculatorUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>(OnOpened);
                subs.Event<BoundUIClosedEvent>(OnClosed);
            });
    }

    private void OnOpened(EntityUid uid, CalculatorComponent component, EntityEventArgs args)
    {
        if (!component.Initialized)
            return;

        var state = new CalculatorBoundUserInterfaceState
        {

        };

        _userInterfaceSystem.SetUiState(uid, CalculatorUiKey.Key, state);
    }

    private void OnClosed(EntityUid uid, CalculatorComponent component, BoundUIClosedEvent args)
    {

    }
}
