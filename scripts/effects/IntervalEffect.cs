using BattleArena.scripts.effects.effectNodes;
using BattleArena.scripts.interfaces;
using Godot;
using System;

namespace BattleArena.scripts.effects
{
    public partial class IntervalEffect : Resource, IContainerEffect
    {
        public IEffect EffectToApply { get; set; }
        public int NumberOfTicks { get; set; } = 5;
        public float TimeBetweenTicks { get; set; } = .5f;

        public IntervalEffect(IEffect effectToApply, int numTicks = 0, float timeBetweenTicks = 0f) {
            EffectToApply = effectToApply;
            NumberOfTicks = numTicks > 0 ? numTicks : NumberOfTicks;
            TimeBetweenTicks = timeBetweenTicks > 0f ? timeBetweenTicks : TimeBetweenTicks;
        }

        public void ApplyEffect(Node target)
        {
            IEffectNode effectNode = this.Instantiate(target);
            target.AddChild(effectNode as Node);
            effectNode.ApplyEffect(target);
        }

        public IEffect Copy()
        {
            return (IEffect)this.Duplicate(true);
        }

        public bool EffectableIsCompatible(IEffectable effectable)
        {
            return EffectToApply.EffectableIsCompatible(effectable);
        }

        public IEffectNode Instantiate(Node target)
        {
            return new IntervalEffectNode(EffectToApply, target, NumberOfTicks, TimeBetweenTicks);
        }

        public IEffect GetNestedEffect()
        {
            return utils.Utilities.GetCoreEffect(EffectToApply);
        }
    }
}
