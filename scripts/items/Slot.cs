using BattleArena.scripts.enums;
using BattleArena.scripts.interfaces;
using BattleArena.scripts.resources;
using Godot;
using System;

namespace BattleArena.scripts.items
{
    public partial class Slot : Marker3D
    {
        public SlotType SlotType { get;}

        private IItem _item;
        public Slot(IItemFactory itemFactory, ItemData data, AnimationPlayer player, SlotType slotType) {
            this.SlotType = slotType;
            if (itemFactory == null) {
                throw new ArgumentNullException(nameof(itemFactory), "Item factory cannot be null.");
            }
            var itemNode = itemFactory.CreateItem(data, player);
            if (itemNode is IItem item)
            {
                this.AddChild(itemNode);
                _item = item;
            }
        }
    }
}
