#nullable enable

using BattleArena.Protocol.V1;
using Godot;

namespace BattleArena.GodotNetworking;

/// <summary>
/// Persistent screen-space presentation for the locally controlled combatant.
/// It binds once to the stable avatar entity and remains bound across lives.
/// </summary>
public partial class NetworkPlayerHud : PanelContainer
{
    [Export]
    public NodePath NameLabelPath { get; set; } = "";

    [Export]
    public NodePath HealthBarPath { get; set; } = "";

    [Export]
    public NodePath HealthValuePath { get; set; } = "";

    [Export]
    public NodePath LifeStatusPath { get; set; } = "";

    private Label _nameLabel = null!;
    private ProgressBar _healthBar = null!;
    private Label _healthValue = null!;
    private Label _lifeStatus = null!;
    private NetworkAvatar? _avatar;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _healthBar = GetNode<ProgressBar>(HealthBarPath);
        _healthValue = GetNode<Label>(HealthValuePath);
        _lifeStatus = GetNode<Label>(LifeStatusPath);
    }

    public void Bind(NetworkAvatar avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        if (_avatar == avatar)
        {
            Apply(avatar.Status);
            return;
        }

        Unbind();
        _avatar = avatar;
        _avatar.StatusChanged += Apply;
        Apply(_avatar.Status);
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    private void Unbind()
    {
        if (_avatar is not null)
        {
            _avatar.StatusChanged -= Apply;
            _avatar = null;
        }
    }

    private void Apply(NetworkAvatarStatus status)
    {
        _nameLabel.Text = status.DisplayLabel;
        _healthBar.MaxValue = Math.Max(1d, status.MaximumHealth);
        _healthBar.Value = Math.Clamp(
            status.CurrentHealth,
            0L,
            Math.Max(1L, status.MaximumHealth));
        _healthValue.Text = $"{status.CurrentHealth} / {status.MaximumHealth}";
        _lifeStatus.Text = status.LifeState switch
        {
            ReplicatedLifeState.Alive => "",
            ReplicatedLifeState.Eliminated =>
                $"ELIMINATED  •  Respawn in {status.RespawnSecondsRemaining:0.0}s",
            ReplicatedLifeState.Respawning =>
                $"RESPAWNING  •  {status.RespawnSecondsRemaining:0.0}s",
            _ => status.LifeState.ToString().ToUpperInvariant(),
        };
    }
}
