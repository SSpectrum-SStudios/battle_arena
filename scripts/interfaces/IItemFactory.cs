using BattleArena.scripts.resources;
using Godot;
using System.Collections.Generic;

namespace BattleArena.scripts.interfaces
{
    public interface IItemFactory
    {
        Node3D CreateItem(ItemData data, AnimationPlayer animPlayer);
    }
}
