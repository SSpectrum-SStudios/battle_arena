using Godot;

namespace BattleArena.scripts.interfaces
{
    public interface IEffectNode
    {
        public void ApplyEffect(Node target);
        public void RemoveEffect(Node target);
    }
}
