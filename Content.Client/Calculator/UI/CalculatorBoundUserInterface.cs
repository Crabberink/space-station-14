using Content.Shared.Calculator.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using static Content.Shared.Calculator.Components.CalculatorComponent;

namespace Content.Client.Calculator.UI;

public sealed class CalculatorBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private readonly SharedCalculatorSystem _calculatorSystem = default!;

    private CalculatorWindow? _window;

    public CalculatorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _calculatorSystem = EntMan.System<SharedCalculatorSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CalculatorWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

        //Button bullshit
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);

        if (State != null)
        {
            _window?.UpdateState(_prototypeManager, (CalculatorBoundUserInterfaceState) State);
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        var calcState = (CalculatorBoundUserInterfaceState) state;
        _window?.UpdateState(_prototypeManager, calcState);
    }
}
