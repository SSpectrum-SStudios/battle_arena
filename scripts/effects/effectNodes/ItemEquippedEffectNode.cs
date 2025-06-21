using BattleArena.scripts.interfaces;
using Godot;
using System;

namespace BattleArena.scripts.effects.effectNodes
{
    public partial class ItemEquippedEffectNode : Node, IEffectNode
    {
        public IEffectNode EffectToApply { get; set; }
        public int ItemID { get; set; }

        public ItemEquippedEffectNode(int itemID, IEffectNode effectToApply) 
        {
            ItemID = itemID;
            EffectToApply = effectToApply;
        }

        public void ApplyEffect(Node target)
        {
            if (EffectToApply != null)
            {
                EffectToApply.ApplyEffect(target);
                this.AddChild(EffectToApply as Node, true);
            }
        }

        public void RemoveEffect(Node target)
        {
            if (EffectToApply != null)
            {
                EffectToApply.RemoveEffect(target);
            }
            this.QueueFree();
        }
    }
}
