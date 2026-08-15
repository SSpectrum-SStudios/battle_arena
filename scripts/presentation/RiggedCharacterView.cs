#nullable enable

using Godot;
using System.IO;
using BattleArena.Multiplayer.Presentation;

namespace BattleArena.Presentation;

/// <summary>
/// Godot-facing adapter between semantic presentation requests and an imported rig.
/// Gameplay and networking code do not depend on imported node or animation names.
/// </summary>
public partial class RiggedCharacterView : Node3D, ICharacterPresentationView
{
    [Export(PropertyHint.File, "*.json")]
    public string DefinitionPath { get; set; } = "";

    private CharacterPresentationDefinition _definition = null!;
    private Node3D _model = null!;
    private AnimationPlayer _animationPlayer = null!;
    private AnimationTree? _animationTree;
    private Skeleton3D? _skeleton;
    private string? _currentSemanticAnimation;
    private Vector2 _locomotionBlendPosition;
    private float _airborneBlend;
    private float _locomotionPlaybackSpeed = 1f;

    public string PresentationId => _definition.Id;

    public Skeleton3D? Skeleton => _skeleton;

    public override void _Ready()
    {
        _definition = CharacterPresentationDefinitionLoader.Load(DefinitionPath);

        var modelScene = ResourceLoader.Load<PackedScene>(_definition.ModelScenePath)
            ?? throw new InvalidDataException(
                $"Character presentation '{_definition.Id}' could not load '{_definition.ModelScenePath}'.");

        _model = modelScene.Instantiate<Node3D>();
        _model.Name = "Model";
        _model.Scale = Vector3.One * _definition.ModelScale;
        _model.RotationDegrees = new Vector3(0f, _definition.ModelYawDegrees, 0f);
        AddChild(_model);

        HideAuthoredNodes(_model);
        _animationPlayer = FindFirstDescendant<AnimationPlayer>(_model)
            ?? throw new InvalidDataException(
                $"Character presentation '{_definition.Id}' does not contain an AnimationPlayer.");
        _skeleton = FindFirstDescendant<Skeleton3D>(_model);
        ImportExternalAnimations();
        ValidateImportedRig();
        ApplyAuthoredAnimationPolicies();
        ConfigureLocomotionGraph();
    }

    public bool HasAnimation(string semanticId)
    {
        return TryResolveAnimation(semanticId, out var binding) &&
            _animationPlayer.HasAnimation(binding.ClipName);
    }

    public bool Play(string semanticId, float speed = 1f)
    {
        if (!TryResolveAnimation(semanticId, out var binding))
        {
            GD.PushWarning($"Character presentation '{_definition.Id}' has no binding for '{semanticId}'.");
            return false;
        }

        if (!_animationPlayer.HasAnimation(binding.ClipName))
        {
            GD.PushWarning(
                $"Character presentation '{_definition.Id}' maps '{semanticId}' to missing clip '{binding.Clip}'.");
            return false;
        }

        if (_animationTree is not null)
        {
            _animationTree.Active = false;
        }

        _animationPlayer.SpeedScale = speed;
        if (_currentSemanticAnimation == semanticId && _animationPlayer.IsPlaying())
        {
            return true;
        }

        _animationPlayer.Play(binding.ClipName, binding.BlendSeconds, speed);
        _currentSemanticAnimation = semanticId;
        return true;
    }

    public bool PlayScaledByMotion(string semanticId, float movementSpeed)
    {
        if (!TryResolveAnimation(semanticId, out var binding))
        {
            GD.PushWarning($"Character presentation '{_definition.Id}' has no binding for '{semanticId}'.");
            return false;
        }

        var playbackSpeed = binding.ReferenceSpeed is { } referenceSpeed
            ? Mathf.Clamp(movementSpeed / referenceSpeed, 0.15f, 2.5f)
            : 1f;
        return Play(semanticId, playbackSpeed);
    }

    public bool PlayForDuration(string semanticId, double durationSeconds)
    {
        if (durationSeconds <= 0d || !TryResolveAnimation(semanticId, out var binding))
        {
            return false;
        }

        var animation = _animationPlayer.GetAnimation(binding.ClipName);
        return animation is not null &&
            Play(semanticId, (float)(animation.Length / durationSeconds));
    }

    /// <summary>
    /// Applies one semantic update selected from a committed simulation sample.
    /// Hold updates deliberately perform no animation operation.
    /// </summary>
    public void Apply(in CharacterPresentationUpdate update)
    {
        if (!update.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(update));
        }

        switch (update.Kind)
        {
            case CharacterPresentationUpdateKind.HoldEliminated:
            case CharacterPresentationUpdateKind.HoldActiveAction:
            case CharacterPresentationUpdateKind.HoldRollContinuation:
                return;
            case CharacterPresentationUpdateKind.Locomotion:
                SetLocomotion(
                    new Vector2(
                        update.NormalizedLocalRightVelocity,
                        update.NormalizedLocalForwardVelocity),
                    update.Airborne,
                    update.DeltaSeconds);
                return;
            case CharacterPresentationUpdateKind.CrouchIdle:
                Play(CharacterAnimationIds.CrouchIdle);
                return;
            case CharacterPresentationUpdateKind.CrouchMove:
                Play(CharacterAnimationIds.CrouchMove);
                return;
            case CharacterPresentationUpdateKind.RollStart:
                PlayForDuration(
                    CharacterAnimationIds.RollForward,
                    update.RollDurationSeconds);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(update));
        }
    }

    public void SetLocomotion(Vector2 normalizedLocalVelocity, bool airborne, double delta)
    {
        if (_animationTree is null)
        {
            throw new InvalidOperationException("The locomotion animation graph has not been configured.");
        }

        var locomotionWeight = 1f - Mathf.Exp(-10f * (float)delta);
        var airborneWeight = 1f - Mathf.Exp(-8f * (float)delta);
        _locomotionBlendPosition = _locomotionBlendPosition.Lerp(
            normalizedLocalVelocity.LimitLength(2.1f),
            locomotionWeight);
        _airborneBlend = Mathf.Lerp(_airborneBlend, airborne ? 1f : 0f, airborneWeight);
        var normalizedSpeed = normalizedLocalVelocity.Length();
        var targetPlaybackSpeed = normalizedSpeed <= 1f
            ? Mathf.Lerp(0.85f, 1f, Mathf.Clamp(normalizedSpeed, 0f, 1f))
            : Mathf.Lerp(1f, 1.55f, Mathf.Clamp((normalizedSpeed - 1f) / 1.08f, 0f, 1f));
        _locomotionPlaybackSpeed = Mathf.Lerp(
            _locomotionPlaybackSpeed,
            airborne ? 1f : targetPlaybackSpeed,
            locomotionWeight);

        _animationTree.Active = true;
        _animationTree.Set("parameters/Locomotion/blend_position", _locomotionBlendPosition);
        _animationTree.Set("parameters/AirBlend/blend_amount", _airborneBlend);
        _animationTree.Set("parameters/PlaybackSpeed/scale", _locomotionPlaybackSpeed);
        _currentSemanticAnimation = null;
    }

    public bool TryGetWeaponSocket(out Skeleton3D skeleton, out StringName? boneName)
    {
        if (_skeleton is null || string.IsNullOrWhiteSpace(_definition.WeaponSocketBone))
        {
            skeleton = null!;
            boneName = default;
            return false;
        }

        var candidate = new StringName(_definition.WeaponSocketBone);
        if (_skeleton.FindBone(candidate) < 0)
        {
            skeleton = null!;
            boneName = default;
            return false;
        }

        skeleton = _skeleton;
        boneName = candidate;
        return true;
    }

    public void Flash(Color color, double durationSeconds = 0.08d)
    {
        if (durationSeconds <= 0d)
        {
            return;
        }

        var overlay = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var meshes = EnumerateDescendants(_model).OfType<MeshInstance3D>().ToArray();
        foreach (var mesh in meshes)
        {
            mesh.MaterialOverlay = overlay;
        }

        var timer = GetTree().CreateTimer(durationSeconds);
        timer.Timeout += () =>
        {
            foreach (var mesh in meshes)
            {
                if (GodotObject.IsInstanceValid(mesh) && mesh.MaterialOverlay == overlay)
                {
                    mesh.MaterialOverlay = null;
                }
            }
        };
    }

    private bool TryResolveAnimation(string semanticId, out AnimationClipBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticId);
        return _definition.Animations.TryGetValue(semanticId, out binding!);
    }

    private void ValidateImportedRig()
    {
        foreach (var (semanticId, binding) in _definition.Animations)
        {
            if (!_animationPlayer.HasAnimation(binding.ClipName))
            {
                throw new InvalidDataException(
                    $"Character presentation '{_definition.Id}' maps '{semanticId}' to missing clip '{binding.Clip}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(_definition.WeaponSocketBone) &&
            (_skeleton is null || _skeleton.FindBone(_definition.WeaponSocketBone) < 0))
        {
            throw new InvalidDataException(
                $"Character presentation '{_definition.Id}' maps its weapon socket to missing bone " +
                $"'{_definition.WeaponSocketBone}'.");
        }
    }

    private void ImportExternalAnimations()
    {
        if (_skeleton is null)
        {
            return;
        }

        foreach (var external in _definition.ExternalAnimations)
        {
            var sourceScene = ResourceLoader.Load<PackedScene>(external.ScenePath)
                ?? throw new InvalidDataException(
                    $"External animation scene '{external.ScenePath}' could not be loaded.");
            var sourceRoot = sourceScene.Instantiate<Node3D>();
            try
            {
                var sourcePlayer = FindFirstDescendant<AnimationPlayer>(sourceRoot)
                    ?? throw new InvalidDataException(
                        $"External animation scene '{external.ScenePath}' has no AnimationPlayer.");
                var sourceSkeleton = FindFirstDescendant<Skeleton3D>(sourceRoot)
                    ?? throw new InvalidDataException(
                        $"External animation scene '{external.ScenePath}' has no Skeleton3D.");
                var sourceName = sourcePlayer.GetAnimationList().FirstOrDefault(
                    name => string.Equals(
                        name.ToString().Split('/').Last(),
                        external.SourceClip,
                        StringComparison.Ordinal));
                if (sourceName == default || sourcePlayer.GetAnimation(sourceName) is not { } sourceAnimation)
                {
                    throw new InvalidDataException(
                        $"External animation scene '{external.ScenePath}' has no clip '{external.SourceClip}'.");
                }

                var animation = sourceAnimation.Duplicate(true) as Animation
                    ?? throw new InvalidDataException(
                        $"External animation '{external.SourceClip}' could not be duplicated.");
                var animationRoot = _animationPlayer.GetNode(_animationPlayer.RootNode);
                RetargetSkeletonTracks(
                    animation,
                    animationRoot.GetPathTo(_skeleton),
                    sourceSkeleton,
                    _skeleton,
                    external.BoneMap,
                    external.DiscardUnmappedBones);
                var libraryName = new StringName(external.TargetLibrary);
                if (!_animationPlayer.HasAnimationLibrary(libraryName))
                {
                    _animationPlayer.AddAnimationLibrary(libraryName, new AnimationLibrary());
                }

                var library = _animationPlayer.GetAnimationLibrary(libraryName);
                var targetName = new StringName(external.TargetClip);
                if (library.HasAnimation(targetName))
                {
                    library.RemoveAnimation(targetName);
                }

                library.AddAnimation(targetName, animation);
            }
            finally
            {
                sourceRoot.Free();
            }
        }
    }

    private static void RetargetSkeletonTracks(
        Animation animation,
        NodePath targetSkeletonPath,
        Skeleton3D sourceSkeleton,
        Skeleton3D targetSkeleton,
        IReadOnlyDictionary<string, string> boneMap,
        bool discardUnmappedBones)
    {
        for (var track = animation.GetTrackCount() - 1; track >= 0; track--)
        {
            var sourcePath = animation.TrackGetPath(track);
            var sourcePathText = sourcePath.ToString();
            var subnameSeparator = sourcePathText.IndexOf(':');
            if (subnameSeparator < 0 || subnameSeparator == sourcePathText.Length - 1)
            {
                animation.RemoveTrack(track);
                continue;
            }

            var sourceBone = sourcePathText[(subnameSeparator + 1)..];
            var targetBone = boneMap.TryGetValue(sourceBone, out var mappedBone)
                ? mappedBone
                : sourceBone;
            if (targetSkeleton.FindBone(targetBone) < 0)
            {
                if (discardUnmappedBones)
                {
                    animation.RemoveTrack(track);
                    continue;
                }

                throw new InvalidDataException(
                    $"External animation bone '{sourceBone}' maps to missing target bone '{targetBone}'.");
            }

            var sourceBoneIndex = sourceSkeleton.FindBone(sourceBone);
            var targetBoneIndex = targetSkeleton.FindBone(targetBone);
            if (sourceBoneIndex < 0)
            {
                throw new InvalidDataException(
                    $"External animation track references missing source bone '{sourceBone}'.");
            }

            if (!RetargetTrackValues(
                    animation,
                    track,
                    sourceSkeleton,
                    sourceBoneIndex,
                    targetSkeleton,
                    targetBoneIndex,
                    targetBone))
            {
                animation.RemoveTrack(track);
                continue;
            }

            animation.TrackSetPath(track, new NodePath($"{targetSkeletonPath}:{targetBone}"));
        }
    }

    private static bool RetargetTrackValues(
        Animation animation,
        int track,
        Skeleton3D sourceSkeleton,
        int sourceBoneIndex,
        Skeleton3D targetSkeleton,
        int targetBoneIndex,
        string targetBone)
    {
        var trackType = animation.TrackGetType(track);
        var sourceRest = sourceSkeleton.GetBoneRest(sourceBoneIndex);
        var targetRest = targetSkeleton.GetBoneRest(targetBoneIndex);
        switch (trackType)
        {
            case Animation.TrackType.Rotation3D:
            {
                var sourceRestRotation = sourceRest.Basis.GetRotationQuaternion();
                var targetRestRotation = targetRest.Basis.GetRotationQuaternion();
                for (var key = 0; key < animation.TrackGetKeyCount(track); key++)
                {
                    var sourceRotation = animation.TrackGetKeyValue(track, key).AsQuaternion();
                    var relativeRotation = sourceRestRotation.Inverse() * sourceRotation;
                    animation.TrackSetKeyValue(
                        track,
                        key,
                        targetRestRotation * relativeRotation);
                }

                return true;
            }
            case Animation.TrackType.Position3D:
            {
                if (targetBone is not ("root" or "hips"))
                {
                    return false;
                }

                var sourceLength = Math.Max(0.001f, sourceRest.Origin.Length());
                var targetLength = Math.Max(0.001f, targetRest.Origin.Length());
                var positionScale = targetLength / sourceLength;
                for (var key = 0; key < animation.TrackGetKeyCount(track); key++)
                {
                    var sourcePosition = animation.TrackGetKeyValue(track, key).AsVector3();
                    animation.TrackSetKeyValue(
                        track,
                        key,
                        targetRest.Origin + ((sourcePosition - sourceRest.Origin) * positionScale));
                }

                return true;
            }
            case Animation.TrackType.Scale3D:
                return false;
            default:
                return true;
        }
    }

    private void ApplyAuthoredAnimationPolicies()
    {
        foreach (var binding in _definition.Animations.Values)
        {
            var animation = _animationPlayer.GetAnimation(binding.ClipName);
            if (animation is not null)
            {
                animation.LoopMode = binding.Loop
                    ? Animation.LoopModeEnum.Linear
                    : Animation.LoopModeEnum.None;
            }
        }
    }

    private void ConfigureLocomotionGraph()
    {
        var locomotion = new AnimationNodeBlendSpace2D
        {
            AutoTriangles = true,
            MinSpace = new Vector2(-2.1f, -1.1f),
            MaxSpace = new Vector2(2.1f, 2.1f),
            Sync = true,
            XLabel = "Lateral velocity",
            YLabel = "Forward velocity",
        };
        locomotion.AddBlendPoint(CreateAnimationNode(CharacterAnimationIds.Idle), Vector2.Zero);
        locomotion.AddBlendPoint(CreateAnimationNode(CharacterAnimationIds.Walk), new Vector2(0f, 0.45f));
        locomotion.AddBlendPoint(CreateAnimationNode(CharacterAnimationIds.Run), new Vector2(0f, 1f));
        locomotion.AddBlendPoint(CreateAnimationNode(CharacterAnimationIds.Sprint), new Vector2(0f, 2.08f));
        locomotion.AddBlendPoint(CreateAnimationNode(CharacterAnimationIds.WalkBackward), new Vector2(0f, -0.75f));
        locomotion.AddBlendPoint(CreateAnimationNode(CharacterAnimationIds.StrafeLeft), new Vector2(-1f, 0f));
        locomotion.AddBlendPoint(CreateAnimationNode(CharacterAnimationIds.StrafeRight), new Vector2(1f, 0f));

        var blendTree = new AnimationNodeBlendTree();
        blendTree.AddNode("Locomotion", locomotion, new Vector2(0f, 0f));
        blendTree.AddNode("Airborne", CreateAnimationNode(CharacterAnimationIds.Airborne), new Vector2(0f, 180f));
        blendTree.AddNode("AirBlend", new AnimationNodeBlend2(), new Vector2(300f, 60f));
        blendTree.AddNode("PlaybackSpeed", new AnimationNodeTimeScale(), new Vector2(520f, 60f));
        blendTree.ConnectNode("AirBlend", 0, "Locomotion");
        blendTree.ConnectNode("AirBlend", 1, "Airborne");
        blendTree.ConnectNode("PlaybackSpeed", 0, "AirBlend");
        blendTree.ConnectNode("output", 0, "PlaybackSpeed");

        _animationTree = new AnimationTree
        {
            Name = "CharacterAnimationTree",
            TreeRoot = blendTree,
        };
        AddChild(_animationTree);
        _animationTree.AnimPlayer = _animationTree.GetPathTo(_animationPlayer);
        _animationTree.Active = true;
    }

    private AnimationNodeAnimation CreateAnimationNode(string semanticId)
    {
        if (!TryResolveAnimation(semanticId, out var binding))
        {
            throw new InvalidDataException(
                $"Character presentation '{_definition.Id}' requires locomotion binding '{semanticId}'.");
        }

        return new AnimationNodeAnimation { Animation = binding.ClipName };
    }

    private void HideAuthoredNodes(Node root)
    {
        var hiddenNames = _definition.HiddenNodeNames.ToHashSet(StringComparer.Ordinal);
        foreach (var node in EnumerateDescendants(root))
        {
            if (node is Node3D node3D && hiddenNames.Contains(node.Name.ToString()))
            {
                node3D.Visible = false;
            }
        }
    }

    private static T? FindFirstDescendant<T>(Node root)
        where T : Node
    {
        foreach (var node in EnumerateDescendants(root))
        {
            if (node is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<Node> EnumerateDescendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
