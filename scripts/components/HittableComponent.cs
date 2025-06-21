using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using BattleArena.scripts.utils;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BattleArena.scripts.components
{
    public partial class HittableComponent : Area3D, IModifiable<HitContext>, IEffectable
    {
        public List<IModifier<HitContext>> modifiers = new();
        public HitContext baseMostRecentHit = null;
        public HitContext modifiedMostRecentHit = null;
        public int entityID = -1;

        public void Hit(AttackPayload attackPayload)
        {
            if (!this.IsMultiplayerAuthority())
                return;

            this.baseMostRecentHit = new HitContext
            {
                HitEntityID = this.entityID,
                AttackPayload = attackPayload
            };

            EmitSignal(GlobalSignals.SignalName.EntityHitReceived, this.baseMostRecentHit);
            HitContext modifiedContext = this.GetModifiedValue();

            if (modifiedContext != null && modifiedContext.AttackPayload.attackingEffects.Any())
            {
                EmitSignal(GlobalSignals.SignalName.EntityHitModified, modifiedContext);
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
        }

        public void AddModifier(IModifier<HitContext> modifier)
        {
            modifiers.Add(modifier);
            modifiers.Sort(new ModifierComparer<HitContext>());
        }

        public HitContext GetBaseValue()
        {
            return baseMostRecentHit ?? new HitContext
            {
                HitEntityID = this.entityID,
                AttackPayload = new AttackPayload()
            };
        }

        public HitContext GetModifiedValue()
        {
            if (this.baseMostRecentHit != null)
            {
                GD.PushError("Tried Copying a null hit. Returning null.");
                return null;
            }
            this.modifiedMostRecentHit = this.baseMostRecentHit.Duplicate(true) as HitContext;
            foreach (var modifier in modifiers)
            {
                modifier.Modify(this.modifiedMostRecentHit);
            }
            return this.modifiedMostRecentHit;
        }

        public void RemoveModifier(IModifier<HitContext> modifier)
        {
            this.modifiers.Remove(modifier);
        }
    }
}// namespace BattleArena.scripts.components

