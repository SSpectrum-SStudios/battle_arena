using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;
using System.Collections.Generic;

namespace BattleArena.scripts.components
{
    public partial class AttackComponent : Area3D, IEffectable, IModifiable<AttackPayload>
    {

        public int entityID;
        public AttackPayload baseAttackPayload = new();
        public List<IModifier<AttackPayload>> modifiers = new();

        public override void _EnterTree()
        {
            this.SetMultiplayerAuthority(1);
        }

        public override void _Ready()
        {
            this.AreaEntered += OnAreaEntered;
        }

        private void OnAreaEntered(Area3D area)
        {
            if (!this.IsMultiplayerAuthority())
            {
                return;
            }
            if (area is HittableComponent hittable)
            {
                hittable.Hit(this.GetModifiedValue());
            }
        }

        public void AddEffect(IEffect effect)
        {
            effect.ApplyEffect(this);
        }

        public int GetID()
        {
            return this.entityID;
        }

        public void SetID(int id)
        {
            this.entityID = id;
            this.modifiers.Add(new modifiers.ApplyAttackingIDModifier { AttackerID = id });
            this.modifiers.Sort(new ModifierComparer<AttackPayload>());
        }

        public AttackPayload GetModifiedValue()
        {
            AttackPayload modifiedValue = AttackPayload.Clone(this.baseAttackPayload);
            foreach (var modifier in this.modifiers)
            {
                modifiedValue = modifier.Modify(modifiedValue);
            }
            return modifiedValue;
        }

        public void RemoveModifier(IModifier<AttackPayload> modifier)
        {
            this.modifiers.Remove(modifier);
        }

        public void AddModifier(IModifier<AttackPayload> modifier)
        {
            this.modifiers.Add(modifier);
            this.modifiers.Sort(new ModifierComparer<AttackPayload>());
        }

        public AttackPayload GetBaseValue()
        {
            return this.baseAttackPayload;
        }

    }
} // namespace BattleArena.scripts.components
