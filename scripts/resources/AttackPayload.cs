using BattleArena.scripts.interfaces;
using Godot;
using System.Collections.Generic;
using System;

namespace BattleArena.scripts.resources { 
    public partial class AttackPayload : Resource
    {
        public List<IEffect> attackingEffects;

        public static AttackPayload Clone(AttackPayload original)
        {
            AttackPayload clone = new AttackPayload();
            foreach (IEffect effect in original.attackingEffects)
            {
                clone.attackingEffects.Add(effect.Copy());
            }
            return clone;
        }
    }

    
}
