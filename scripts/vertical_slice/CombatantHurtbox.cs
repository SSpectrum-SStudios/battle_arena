#nullable enable

using Godot;

namespace BattleArena.VerticalSlice;

public partial class CombatantHurtbox : Area3D
{
    [Export(PropertyHint.Range, "1,9223372036854775807,1")]
    public long CombatantId { get; set; } = 1;
}
