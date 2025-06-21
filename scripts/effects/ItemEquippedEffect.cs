using BattleArena.scripts.effects.effectNodes;
using BattleArena.scripts.interfaces;
using Godot;
using System;

namespace BattleArena.scripts.effects
{
    public partial class ItemEquippedEffect : Resource, IContainerEffect
    {
        public int ItemID { get; set; }
        public IEffect EffectToApply { get; set; }
        public ItemEquippedEffect(int item_id = -1, IEffect effectToApply = null) 
        {
            ItemID = item_id;
            EffectToApply = effectToApply;
        }
        public void ApplyEffect(Node target)
        {
            IEffectNode node = this.Instantiate(target);
            target.AddChild(node as Node, true);
            node.ApplyEffect(target);
        }

        public IEffect Copy()
        {
            var cp = new ItemEquippedEffect(ItemID, EffectToApply?.Copy());
            return cp as IEffect;
        }

        public bool EffectableIsCompatible(IEffectable effectable)
        {
            return EffectToApply.EffectableIsCompatible(effectable);
        }

        public IEffect GetNestedEffect()
        {
            return utils.Utilities.GetCoreEffect(EffectToApply);
        }

        public IEffectNode Instantiate(Node target)
        {
            return new ItemEquippedEffectNode(ItemID, EffectToApply.Instantiate(target));
        }
    }
}
