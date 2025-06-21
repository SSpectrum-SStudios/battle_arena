using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;
using System.Collections.Generic;

namespace BattleArena.scripts.components
{
    public partial class HealthComponent : Area3D, IEffectable, IModifiable<HealthResource>
    {
        public HealthResource health = new();
        public List<IModifier<HealthResource>> modifiers = new();
        public int EntityID { get; set; } = -1; // Default to -1, indicating no specific entity

        public HealthResource modifiedHealthCached;

        [Export]
        public float SyncedMaxHealth
        {
            get { return health.MaxHealth; } set { health.MaxHealth = value; }
        }

        [Export]
        public float SyncedCurrentHealth
        {
            get { return health.CurrentHealth; } set { health.CurrentHealth = value; }
        }

        [Export]
        public float SyncedModifiedMaxHealth
        {
            get { return modifiedHealthCached.MaxHealth; } set { modifiedHealthCached.MaxHealth = value; }
        }
        [Export]
        public float SyncedModifiedCurrentHealth
        {
            get { return modifiedHealthCached.CurrentHealth; } set { modifiedHealthCached.CurrentHealth = value; }
        }

        [Signal]
        public delegate void HealthAtZeroEventHandler();

        public void AddEffect(IEffect effect)
        {
            effect.ApplyEffect(this);
        }
        public void SetID(int id)
        {
            this.EntityID = id;
        }
        public int GetID()
        {
            return this.EntityID;
        }
        public void AddModifier(IModifier<HealthResource> modifier)
        {
            this.modifiers.Add(modifier);
            this.modifiers.Sort(new ModifierComparer<HealthResource>());
        }

        public HealthResource GetBaseValue()
        {
            return this.health.Duplicate(true) as HealthResource;
        }

        public HealthResource GetModifiedValue()
        {
            return this.modifiedHealthCached;
        }

        public void RemoveModifier(IModifier<HealthResource> modifier)
        {
            modifiers.Remove(modifier);
            this.RecalculateModifiedHealth();

        }

        public override void _EnterTree()
        {
            this.SetMultiplayerAuthority(1);
        }

        public override void _Ready()
        {
            this.health.CurrentHealth = this.health.MaxHealth; // Initialize current health to max health
            this.modifiedHealthCached = this.health.Duplicate(true) as HealthResource;
            this.RecalculateModifiedHealth();
        }

        public void TakeDamage(DamageContext damageContext)
        {
            if (!IsMultiplayerAuthority())
                return;

            if (this.modifiedHealthCached.CurrentHealth - damageContext.DamageAmount <= 0)
            {
                damageContext.DamageAmount = this.modifiedHealthCached.CurrentHealth; // Ensure we don't go negative
                this.modifiedHealthCached.CurrentHealth = 0;
                EmitSignal(SignalName.HealthAtZero);
                GD.Print($"Entity {EntityID} health reached zero.");
            }
            else
            {
                this.modifiedHealthCached.CurrentHealth -= damageContext.DamageAmount;
            }

            GD.Print($"Entity {EntityID} took {damageContext.DamageAmount} damage, new health: {this.modifiedHealthCached.CurrentHealth}");

        }

        public void Heal(float amount)
        {
            if (!IsMultiplayerAuthority())
                return;
            
            this.modifiedHealthCached.CurrentHealth += amount;
            if (this.modifiedHealthCached.CurrentHealth > this.modifiedHealthCached.MaxHealth)
            {
                this.modifiedHealthCached.CurrentHealth = this.modifiedHealthCached.MaxHealth;
            }

            GD.Print($"Healed {amount}, new health: {this.modifiedHealthCached.CurrentHealth}");
        }

        protected void RecalculateModifiedHealth()
        {
            if (!IsMultiplayerAuthority())
                return;

            float ratio = this.modifiedHealthCached.CurrentHealth / this.modifiedHealthCached.MaxHealth;
            this.health.CurrentHealth = this.health.MaxHealth * ratio;
            this.modifiedHealthCached = this.health.Duplicate(true) as HealthResource;
            foreach (IModifier<HealthResource> modifier in this.modifiers)
            {
                this.modifiedHealthCached = modifier.Modify(this.modifiedHealthCached);
            }
        }
    }
}
