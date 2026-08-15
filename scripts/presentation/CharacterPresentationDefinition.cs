#nullable enable

using System.Text.Json.Serialization;
using Godot;

namespace BattleArena.Presentation;

public sealed class CharacterPresentationDefinition
{
    public int SchemaVersion { get; init; }

    public string Id { get; init; } = "";

    public string ModelScenePath { get; init; } = "";

    public float ModelScale { get; init; } = 1f;

    public float ModelYawDegrees { get; init; }

    public string WeaponSocketBone { get; init; } = "";

    public IReadOnlyList<string> HiddenNodeNames { get; init; } = [];

    public IReadOnlyDictionary<string, AnimationClipBinding> Animations { get; init; }
        = new Dictionary<string, AnimationClipBinding>();

    public IReadOnlyList<ExternalAnimationBinding> ExternalAnimations { get; init; } = [];
}

public sealed class ExternalAnimationBinding
{
    public string ScenePath { get; init; } = "";

    public string SourceClip { get; init; } = "";

    public string TargetLibrary { get; init; } = "external";

    public string TargetClip { get; init; } = "";

    public IReadOnlyDictionary<string, string> BoneMap { get; init; }
        = new Dictionary<string, string>();

    public bool DiscardUnmappedBones { get; init; }
}

public sealed class AnimationClipBinding
{
    public string Clip { get; init; } = "";

    public float BlendSeconds { get; init; } = 0.1f;

    public float? ReferenceSpeed { get; init; }

    public bool Loop { get; init; }

    [JsonIgnore]
    public StringName ClipName => new(Clip);
}
