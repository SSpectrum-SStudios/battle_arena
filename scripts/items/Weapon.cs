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
    public partial class Weapon : IItem
    {
        public Animation Reset { get; private set; }
        public List<Animation> AbilityAnimations { get; private set; } = [];
        public ItemData Data { get; private set; }
        public AnimationPlayer AnimPlayer { get; private set; }
        public void Activate()
        {
            throw new NotImplementedException();
        }

        public void Setup(ItemData data, AnimationPlayer animPlayer)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data), "Item data cannot be null.");
            AnimPlayer = animPlayer ?? throw new ArgumentNullException(nameof(animPlayer), "Animation player cannot be null.");

            var animLib = AnimPlayer.GetAnimationLibrary("");
            animLib.AddAnimation(Reset.ResourceName, Reset);
            foreach (var anim in AbilityAnimations)
            {
                if (anim != null)
                {
                    animLib.AddAnimation(anim.ResourceName, anim);
                }
            }
        }

        private void on_animation_finished(StringName AnimName)
        {
            if (AbilityAnimations.Any(anim => anim.ResourceName == AnimName))
            {
                this.AnimPlayer.Play(Reset.ResourceName);
            }

        }

    }
}
