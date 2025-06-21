
namespace BattleArena.scripts.interfaces
{
    public interface IEffectable
    {
        public void AddEffect(IEffect effect);
        public void SetID(int id);
        public int GetID();

    }
}
