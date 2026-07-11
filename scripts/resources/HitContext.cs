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
            set { _hitEntityID = value; EmitChanged(); }
        }
        public AttackPayload AttackPayload
        {
            get { return AttackPayload; }
            set { _attackPayload = value; EmitChanged(); }
        }

        private int _hitEntityID;
        private AttackPayload _attackPayload;
    }
}
