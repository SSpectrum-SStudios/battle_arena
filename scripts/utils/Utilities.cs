using BattleArena.scripts.interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BattleArena.scripts.utils
{
    public class Utilities
    {
        public static IEffect GetCoreEffect(IEffect effect)
        {
            if (effect is IContainerEffect containerEffect)
            {
                return GetCoreEffect(containerEffect.GetNestedEffect());
            }
            return effect;
        }
    }
}
