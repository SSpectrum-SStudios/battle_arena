using BattleArena.scripts.components;
using BattleArena.scripts.effects.effectNodes;
using BattleArena.scripts.interfaces;
using Godot;
using System;

namespace BattleArena.scripts.effects
{
    public partial class SingleDamageEffect : BaseDamagingEffect
    {
        public override void ApplyEffect(Node target)
        {
            IEffectNode effectNode = Instantiate(target);
            target.AddChild(effectNode as Node, true);
            effectNode.ApplyEffect(target);
        }

        public override IEffect Copy()
        {
            return (IEffect)this.Duplicate(true);
        }

        public override bool EffectableIsCompatible(IEffectable effectable)
        {
            return effectable is HealthComponent;
        }

        public override IEffectNode Instantiate(Node target)
        {
            return new SingleDamageEffectNode(this.DamageContext);
        }
    }
}
