using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;

namespace BattleArena.scripts.effects
{
    public partial class BaseDamagingEffect : Resource, IEffect
    {
        public DamageContext DamageContext { get; set; } = new DamageContext();
        public virtual void ApplyEffect(Node target)
        {
            throw new NotImplementedException();
        }

        public virtual IEffect Copy()
        {
            throw new NotImplementedException();
        }

        public virtual bool EffectableIsCompatible(IEffectable effectable)
        {
            throw new NotImplementedException();
        }

        public virtual IEffectNode Instantiate(Node target)
        {
            throw new NotImplementedException();
        }
    }
}
