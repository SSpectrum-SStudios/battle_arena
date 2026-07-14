#nullable enable

using BattleArena.Core.Combat;
using BattleArena.Core.Effects;
using Godot;

namespace BattleArena.VerticalSlice;

public partial class VerticalSliceHud : CanvasLayer
{
    [Export]
    public NodePath StatusLabelPath { get; set; } = "";

    [Export]
    public NodePath EffectsLabelPath { get; set; } = "";

    private Label _statusLabel = null!;
    private Label _effectsLabel = null!;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _effectsLabel = GetNode<Label>(EffectsLabelPath);
    }

    public void UpdateDisplay(
        HealthSnapshot? dummy,
        IReadOnlyList<PeriodicDamageEffectSnapshot> effects,
        bool auraActive)
    {
        _statusLabel.Text =
            "WASD / Left Stick: Move    Shift / L3: Sprint    Space / A: Jump\n" +
            "Mouse / Right Stick: Look    V / R3: Camera    Left Click / RT: Attack\n" +
            "Q / LB: Toggle poison aura    Escape: Release mouse\n\n" +
            $"Aura: {(auraActive ? "ACTIVE (2x damage, 2x tick speed)" : "OFF")}\n" +
            $"Dummy: {(dummy is null ? "unavailable" : $"{dummy.CurrentHealth:0.#} / {dummy.EffectiveMaximumHealth:0.#}")}";

        if (effects.Count == 0)
        {
            _effectsLabel.Text = "Active effects on dummy: none";
            return;
        }

        _effectsLabel.Text = "Active effects on dummy:\n" + string.Join(
            "\n",
            effects.Select(effect =>
            {
                var portions = string.Join(
                    " + ",
                    effect.TickDamagePortions.Select(portion => $"{portion.Amount:0.#} {portion.Type}"));
                return $"#{effect.Effect.Id.Value}: {portions} every {effect.Interval.Ticks} ticks; " +
                       $"remaining {effect.Effect.RemainingTicks?.ToString() ?? "duration"}; " +
                       $"modifiers {effect.ModifierCount}";
            }));
    }
}
