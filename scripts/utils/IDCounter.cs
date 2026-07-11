
using System.Threading;
using System.Threading.Tasks;

namespace BattleArena.scripts.utils
{
    public static class IDCounter
    {
        private static int _counter = 0;

        public static int GetNextID()
        {
            return Interlocked.Increment(ref IDCounter._counter);
        }

        public static int GetMostRecentID()
        {
            long counterValue = _counter;
            return (int)Interlocked.Read(ref counterValue);
        }
    }
}
