using BattleArena.scripts.interfaces;
using Godot;
using System.Collections.Generic;
using System;
using BattleArena.scripts.utils;

namespace BattleArena.scripts.resources {
    public partial class ItemData : Resource
    {
        public List<IEffect> Effects { get; }
        public int ItemID { get; } = IDCounter.GetNextID();
        public string ItemName { get; set; }
        public string ItemScenePath { get; set; }
        public string ItemIconPath { get; set; }

        public ItemData(string itemName, string itemScenePath, string itemIconPath, List<IEffect> effects)
        {
            Effects = effects ?? new List<IEffect>();
            ItemName = itemName;
            ItemScenePath = itemScenePath;
            ItemIconPath = itemIconPath;
        }
    }
}

