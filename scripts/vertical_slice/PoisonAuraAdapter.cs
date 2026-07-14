#nullable enable

using BattleArena.Core.Common;
using Godot;

namespace BattleArena.VerticalSlice;

public partial class PoisonAuraAdapter : Area3D
{
    [Export(PropertyHint.Range, "1,9223372036854775807,1")]
    public long OwnerCombatantId { get; set; } = 1;

    [Export]
    public NodePath VisualPath { get; set; } = "";

    private readonly HashSet<long> _reportedMembers = [];
    private MeshInstance3D _visual = null!;
    private VerticalSliceGame _game = null!;
    private ActiveEffectInfluenceId? _influenceId;

    public bool IsActive => _influenceId is not null;

    public override void _Ready()
    {
        _visual = GetNode<MeshInstance3D>(VisualPath);
        _game = GetTree().GetFirstNodeInGroup("vertical_slice_game") as VerticalSliceGame
            ?? throw new InvalidOperationException("VerticalSliceGame was not found.");
        Monitoring = false;
        _visual.Visible = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_influenceId is not { } influenceId)
        {
            return;
        }

        var currentlyOverlapping = GetOverlappingAreas()
            .OfType<CombatantHurtbox>()
            .Select(static hurtbox => hurtbox.CombatantId)
            .ToHashSet();

        foreach (var enteredId in currentlyOverlapping.Except(_reportedMembers).Order())
        {
            _game.EnterInfluence(influenceId, new CombatantId(enteredId));
        }

        foreach (var exitedId in _reportedMembers.Except(currentlyOverlapping).Order())
        {
            _game.ExitInfluence(influenceId, new CombatantId(exitedId));
        }

        _reportedMembers.Clear();
        _reportedMembers.UnionWith(currentlyOverlapping);
    }

    public void Toggle()
    {
        if (_influenceId is null)
        {
            var started = _game.BeginPoisonAura(new CombatantId(OwnerCombatantId));
            if (started is null)
            {
                return;
            }

            _influenceId = started;
            Monitoring = true;
            _visual.Visible = true;
            _game.SetAuraActive(true);
            return;
        }

        _game.EndInfluence(_influenceId.Value);
        _influenceId = null;
        _reportedMembers.Clear();
        Monitoring = false;
        _visual.Visible = false;
        _game.SetAuraActive(false);
    }
}
