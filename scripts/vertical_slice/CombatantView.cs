#nullable enable

using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using Godot;

namespace BattleArena.VerticalSlice;

public partial class CombatantView : Node
{
    [Export(PropertyHint.Range, "1,9223372036854775807,1")]
    public long CombatantIdValue { get; set; } = 1;

    [Export(PropertyHint.Range, "1,1000000,1")]
    public double MaximumHealth { get; set; } = 100d;

    [Export]
    public NodePath VisualRootPath { get; set; } = "";

    [Export]
    public NodePath HurtboxPath { get; set; } = "";

    [Export]
    public NodePath BodyCollisionPath { get; set; } = "";

    [Export]
    public NodePath StatusLabelPath { get; set; } = "";

    private Node3D? _visualRoot;
    private CombatantHurtbox? _hurtbox;
    private CollisionShape3D? _bodyCollision;
    private Label3D? _statusLabel;

    public CombatantId CombatantId => new(CombatantIdValue);

    public Node3D Actor => GetParent<Node3D>();

    public override void _Ready()
    {
        AddToGroup("vertical_slice_combatant");
        _visualRoot = GetNodeOrNull<Node3D>(VisualRootPath);
        _hurtbox = GetNodeOrNull<CombatantHurtbox>(HurtboxPath);
        _bodyCollision = GetNodeOrNull<CollisionShape3D>(BodyCollisionPath);
        _statusLabel = GetNodeOrNull<Label3D>(StatusLabelPath);
    }

    public void ApplyHealth(HealthSnapshot snapshot)
    {
        if (snapshot.CombatantId != CombatantId)
        {
            throw new ArgumentException("The health snapshot belongs to another combatant.", nameof(snapshot));
        }

        if (_statusLabel is not null)
        {
            _statusLabel.Text = snapshot.IsEliminated
                ? "ELIMINATED"
                : $"{snapshot.CurrentHealth:0.#} / {snapshot.EffectiveMaximumHealth:0.#}";
        }

        SetAlive(!snapshot.IsEliminated);
    }

    private void SetAlive(bool alive)
    {
        if (_visualRoot is not null)
        {
            _visualRoot.Visible = alive;
        }

        if (_hurtbox is not null)
        {
            _hurtbox.SetDeferred(Area3D.PropertyName.Monitorable, alive);
            _hurtbox.SetDeferred(Area3D.PropertyName.Monitoring, alive);
        }

        if (_bodyCollision is not null)
        {
            _bodyCollision.SetDeferred(CollisionShape3D.PropertyName.Disabled, !alive);
        }
    }
}
