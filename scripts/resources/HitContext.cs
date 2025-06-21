using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BattleArena.scripts.resources
{
    public partial class HitContext : Resource
    {
        public int HitEntityID
        {
            get { return HitEntityID; }
            set { HitEntityID = value; EmitChanged(); }
        }
        public AttackPayload AttackPayload
        {
            get { return AttackPayload; }
            set { AttackPayload = value; EmitChanged(); }
        }
    }
}
