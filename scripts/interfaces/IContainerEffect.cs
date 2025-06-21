

namespace BattleArena.scripts.interfaces
{
    public interface IContainerEffect : IEffect
    {
        public IEffect GetNestedEffect();
    }
}
