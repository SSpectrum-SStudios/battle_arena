using Godot;

namespace BattleArena.scripts.interfaces
{
    public interface IModifier<T>
    {
        public T Modify(T value);
        public bool ModifiableIsCompatible(IModifiableBase value);
        public enums.ModifierPriority GetPriority();
    }

    public class ModifierComparer<T> : System.Collections.Generic.IComparer<IModifier<T>>
    {
        public int Compare(IModifier<T> x, IModifier<T> y)
        {
            return x.GetPriority().CompareTo(y.GetPriority());
        }
    }


}
