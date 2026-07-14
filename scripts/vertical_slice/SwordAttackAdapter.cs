#nullable enable

using BattleArena.Core.Common;
using Godot;

namespace BattleArena.VerticalSlice;

public partial class SwordAttackAdapter : Node3D
{
    [Export(PropertyHint.Range, "1,9223372036854775807,1")]
    public long OwnerCombatantId { get; set; } = 1;

    [Export(PropertyHint.Range, "0.05,5,0.01")]
    public float AttackDurationSeconds { get; set; } = 0.45f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HitboxStartsAt { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HitboxEndsAt { get; set; } = 0.65f;

    [Export]
    public NodePath HitboxPath { get; set; } = "";

    private Area3D _hitbox = null!;
    private VerticalSliceGame _game = null!;
    private ActionExecutionId? _executionId;
    private float _elapsed;
    private bool _attacking;

    public override void _Ready()
    {
        _hitbox = GetNode<Area3D>(HitboxPath);
        _hitbox.Monitoring = false;
        _hitbox.AreaEntered += OnAreaEntered;
        _game = GetTree().GetFirstNodeInGroup("vertical_slice_game") as VerticalSliceGame
            ?? throw new InvalidOperationException("VerticalSliceGame was not found.");
        SetSwingRotation(0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_attacking)
        {
            return;
        }

        _elapsed += (float)delta;
        var progress = Mathf.Clamp(_elapsed / AttackDurationSeconds, 0f, 1f);
        SetSwingRotation(progress);

        var hitboxActive = progress >= HitboxStartsAt && progress <= HitboxEndsAt;
        if (_hitbox.Monitoring != hitboxActive)
        {
            _hitbox.Monitoring = hitboxActive;
        }

        if (progress < 1f)
        {
            return;
        }

        _hitbox.Monitoring = false;
        _attacking = false;
        SetSwingRotation(0f);
        if (_executionId is { } executionId)
        {
            _game.EndAction(executionId);
            _executionId = null;
        }
    }

    public bool TryStartAttack()
    {
        if (_attacking)
        {
            return false;
        }

        _executionId = _game.BeginSwordAction(new CombatantId(OwnerCombatantId));
        if (_executionId is null)
        {
            return false;
        }

        _elapsed = 0f;
        _attacking = true;
        SetSwingRotation(0f);
        return true;
    }

    private void OnAreaEntered(Area3D area)
    {
        if (!_attacking ||
            _executionId is not { } executionId ||
            area is not CombatantHurtbox hurtbox ||
            hurtbox.CombatantId == OwnerCombatantId)
        {
            return;
        }

        _game.RegisterHit(executionId, new CombatantId(hurtbox.CombatantId));
    }

    private void SetSwingRotation(float progress)
    {
        var eased = Mathf.SmoothStep(0f, 1f, progress);
        Rotation = new Vector3(0f, Mathf.Lerp(Mathf.DegToRad(-75f), Mathf.DegToRad(75f), eased), 0f);
    }
}
