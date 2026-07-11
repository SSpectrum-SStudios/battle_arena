using BattleArena.scripts.resources;
using Godot;
using System.Collections.Generic;

namespace BattleArena.scripts.interfaces
{
    public interface IItem
    {
        void Setup(ItemData data, AnimationPlayer animPlayer);
        void Activate();
    }
}
