using BattleArena.scripts.components;
using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;

namespace BattleArena.scripts.effects.effectNodes
{
    public partial class SingleDamageEffectNode : Node, IEffectNode
    {
        public DamageContext DamageContext { get; set; }

        public SingleDamageEffectNode(DamageContext context)
        {
            DamageContext = context;
        }

        public void ApplyEffect(Node target)
        {
            var targetComp = target as HealthComponent;
            if (targetComp != null)
            {
                targetComp.TakeDamage(this.DamageContext);
                this.RemoveEffect(targetComp);
            }
        }

        public void RemoveEffect(Node target)
        {
            this.QueueFree();
        }
    }
}
