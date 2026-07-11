using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BattleArena.scripts.items
{
    public partial class DefaultItemFactory : IItemFactory
    {
        public Node3D CreateItem(ItemData data, AnimationPlayer animPlayer)
        {
            var scene = GD.Load<PackedScene>(data.ItemScenePath);
            if (scene == null)
            {
                GD.PushError($"Scene not found: {data.ItemScenePath}");
                return null;
            }

            var itemNode = scene.Instantiate<Node3D>();
            if (itemNode is IItem item)
            {
                item.Setup(data, animPlayer);
            }

            return itemNode;
        }
    }
    
}
