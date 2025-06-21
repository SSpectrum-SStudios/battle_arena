using BattleArena.scripts.resources;
using Godot;
using System;

namespace BattleArena.scripts.utils
{
    public partial class GlobalSignals : Node
    {
        public static GlobalSignals Instance { get; private set; }

        [Signal]
        public delegate void EntityHitReceivedEventHandler(HitContext hitContext);
        [Signal]
        public delegate void OnDamageTakenEventHandler(DamageContext damageContext);
        [Signal]
        public delegate void EntityHitModifiedEventHandler(HitContext hitContext);

    }
}
