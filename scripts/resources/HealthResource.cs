using BattleArena.scripts.interfaces;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BattleArena.scripts.resources
{
    public partial class HealthResource : Resource
    {
        public float MaxHealth
        {
            get { return MaxHealth; }
            set { MaxHealth = value;
                EmitChanged();
            }
        }
        public float CurrentHealth
        {
            get { return CurrentHealth; }
            set { CurrentHealth = value;
                EmitChanged();
            }
        }
    }
}
