using BattleArena.scripts.components;
using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using BattleArena.scripts.utils;
using Godot;
using System;

namespace BattleArena.scripts.effects.effectNodes 
{
    public partial class LifeStealEffectNode : Node, IEffectNode
    {
        public float LifeStealPercentage { get; set; }
        private HealthComponent targetNode;
        public int EntityID { get; set; } = -1; // Default to -1, indicating no specific entity

        public LifeStealEffectNode(float lifeStealPercentage, HealthComponent target)
        {
            LifeStealPercentage = lifeStealPercentage;
            targetNode = target;
            EntityID = targetNode.EntityID;
        }
        public void ApplyEffect(Node target)
        {
            this.targetNode = target as HealthComponent;
            EntityID = targetNode.EntityID;
            GlobalSignals.Instance.OnDamageTaken += OnDamageTaken;

        }

        public void RemoveEffect(Node target)
        {
            GlobalSignals.Instance.OnDamageTaken -= OnDamageTaken;
            QueueFree();
        }

        private void OnDamageTaken(DamageContext damageContext)
        {
            if (damageContext.AttackerID == EntityID)
            {
                float healAmount = damageContext.DamageAmount * LifeStealPercentage;
                targetNode.Heal(healAmount);
            }
        }
    }
}
