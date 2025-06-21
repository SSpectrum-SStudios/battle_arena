using BattleArena.scripts.components;
using BattleArena.scripts.effects.effectNodes;
using BattleArena.scripts.interfaces;
using Godot;
using System;

namespace BattleArena.scripts.effects
{
    public partial class LifeStealEffect : Resource, IEffect
    {
        public float LifeStealPercentage { get; set; } = 0.1f; // Default to 10% life steal
        public LifeStealEffect(float lifeStealPercentage = 0.1f)
        {
            LifeStealPercentage = lifeStealPercentage > 0f ? lifeStealPercentage : LifeStealPercentage;
        }
        public void ApplyEffect(Node target)
        {
            var effectNode = this.Instantiate(target);
            target.AddChild(effectNode as Node, true);
            effectNode.ApplyEffect(target);
        }

        public IEffect Copy()
        {
            return new LifeStealEffect(LifeStealPercentage) as IEffect;
        }

        public bool EffectableIsCompatible(IEffectable effectable)
        {
            return effectable is HealthComponent;
        }

        public IEffectNode Instantiate(Node target)
        {
            if (target is HealthComponent healthComponent)
            {
                return new LifeStealEffectNode(LifeStealPercentage, healthComponent);
            }
            GD.PrintErr("LifeStealEffect: Target is not a HealthComponent, cannot instantiate LifeStealEffectNode.");
            return null;

        }
    }
}
