using BattleArena.scripts.enums;
using BattleArena.scripts.interfaces;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BattleArena.scripts.resources
{
    public partial class DamageContext : Resource
    {
        [Export]
        public float DamageAmount
        {
            get { return DamageAmount; }
            set { DamageAmount = value; EmitChanged(); }
        }

        [Export]
        public DamageType DamageType
        {
            get { return DamageType; }
            set { DamageType = value; EmitChanged(); }
        }

        [Export]
        public int AttackerID
        {
            get { return AttackerID; }
            set { AttackerID = value; EmitChanged(); }
        }
    }
}
