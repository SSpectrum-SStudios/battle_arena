using BattleArena.scripts.interfaces;
using Godot;
using System;

namespace BattleArena.scripts.effects.effectNodes
{
    public partial class IntervalEffectNode : Node, IEffectNode
    {
        public IEffect EffectToApply { get; set; }
        public Node Target { get; set; }
        public int NumberOfTicks { get; set; }
        public float TimeBetweenTicks { get; set; }
        public int CurrentNumberTicks { get; set; }
        public Timer EffectTimer { get; set; }

        public IntervalEffectNode(IEffect effect, Node target, int numberOfTicks, float timeBetweenTicks)
        {
            EffectToApply = effect;
            Target = target;
            NumberOfTicks = numberOfTicks;
            TimeBetweenTicks = timeBetweenTicks;
            CurrentNumberTicks = NumberOfTicks;
        }
        public void ApplyEffect(Node target)
        {
            TickEffect();
            CreateTimer();
        }

        public void RemoveEffect(Node target)
        {
            if (this.EffectTimer != null && !this.EffectTimer.IsStopped())
            {
                this.EffectTimer.Stop();
                this.EffectTimer.Timeout -= OnTimerTimeout;
                this.EffectTimer.QueueFree();
            }
            this.QueueFree();
        }

        private void TickEffect()
        {
            if (EffectToApply != null)
            {
                IEffectNode effectNodeToApply = EffectToApply.Instantiate(Target);
                effectNodeToApply.ApplyEffect(Target);
                this.Target.AddChild(effectNodeToApply as Node);
            }
            CurrentNumberTicks--;

            if (CurrentNumberTicks <= 0)
            {
                this.RemoveEffect(Target);
            }
        }

        private void CreateTimer()
        {
            this.EffectTimer = new Timer();
            this.EffectTimer.Autostart = true;
            this.EffectTimer.Timeout += OnTimerTimeout;
            this.AddChild(this.EffectTimer);
        }

        public void OnTimerTimeout()
        {
            if (EffectTimer != null)
            {
                TickEffect();
            }
        }

    }
}
