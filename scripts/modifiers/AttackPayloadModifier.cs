using BattleArena.scripts.enums;
using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;

namespace BattleArena.scripts.modifiers
{
    public partial class AttackPayloadModifier : Resource, IModifier<AttackPayload>
    {
        public IEffect EffectForPayload { get; set; }
        public ModifierPriority GetPriority()
        {
            return ModifierPriority.HIGHEST;
        }

        public bool ModifiableIsCompatible(IModifiableBase value)
        {
            return value is IModifiable<AttackPayload>;
        }

        public AttackPayload Modify(AttackPayload value)
        {
            value.attackingEffects.Add(EffectForPayload);
            return value;
        }
    }
}
