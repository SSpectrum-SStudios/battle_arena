using BattleArena.scripts.effects;
using BattleArena.scripts.enums;
using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;

namespace BattleArena.scripts.modifiers
{
    public partial class ApplyAttackingIDModifier : Resource, IModifier<AttackPayload>
    {
        public int AttackerID { get; set; } = -1; // Default to -1, meaning no attacker ID is set
        public ModifierPriority GetPriority()
        {
            return ModifierPriority.LOWEST;
        }

        public bool ModifiableIsCompatible(IModifiableBase value)
        {
            return value is IModifiable<AttackPayload>;
        }

        public AttackPayload Modify(AttackPayload value)
        {
            foreach (IEffect effect in value.attackingEffects)
            {
                var coreEffect = utils.Utilities.GetCoreEffect(effect) as BaseDamagingEffect;
                if (coreEffect is not null)
                {
                    coreEffect.DamageContext.AttackerID = AttackerID; // Set the attacker ID in the damage context
                }
            }
            return value;
        }
    }
}
