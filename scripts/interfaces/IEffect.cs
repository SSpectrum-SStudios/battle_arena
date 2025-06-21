using Godot;
using System;

namespace BattleArena.scripts.interfaces
{
    public interface IEffect
    {
        public void ApplyEffect(Node target);
        public bool EffectableIsCompatible(IEffectable effectable);
        public IEffect Copy();
        public IEffectNode Instantiate(Node target);
    }

}