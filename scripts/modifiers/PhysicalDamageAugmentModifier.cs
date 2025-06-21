using BattleArena.scripts.effects;
using BattleArena.scripts.enums;
using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;

namespace BattleArena.scripts.modifiers
{
    public partial class PhysicalDamageAugmentModifier : Resource, IModifier<AttackPayload>
    {
        public bool IsMultiplier { get; set; } = true; // Default to true for multiplier behavior
        public float AugmentAmount { get; set; } = 0.0f; // Default to 0, meaning no augmentation
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
                if (coreEffect is null)
                    continue; // Skip if the effect is not a BaseDamagingEffect

                var context = coreEffect.DamageContext;
                if (context.DamageType is not DamageType.PHSICAL)
                    continue; // Only modify physical damage

                if (IsMultiplier)
                {
                    context.DamageAmount *= AugmentAmount;
                }
                else
                {
                    context.DamageAmount += AugmentAmount;
                }
            }
            return value;
        }
    }
}
