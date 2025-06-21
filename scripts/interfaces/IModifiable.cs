using Godot;

namespace BattleArena.scripts.interfaces
{
    public interface IModifiableBase{}
    public interface IModifiable<T> : IModifiableBase
    {
        public void AddModifier(IModifier<T> modifier);
        public void RemoveModifier(IModifier<T> modifier);
        public T GetBaseValue();
        public T GetModifiedValue();
    }
}
