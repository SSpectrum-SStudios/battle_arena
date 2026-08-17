using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// The Godot implementation of <see cref="ICharacterCollisionWorld"/>.
/// </summary>
/// <remarks>
/// <para>
/// Built on the approach the P01-09 feasibility probe validated: one precreated
/// kinematic body RID per collision profile, reusable query parameter and result
/// objects, and <c>PhysicsServer3D.BodyTestMotion</c> against an explicit
/// transform. The probe measured 6,528 historical-transform queries at about
/// 10.8 microseconds each, which is what makes per-frame replay affordable.
/// </para>
/// <para>
/// The critical property is that no live node moves. Every query supplies its
/// own <c>From</c> transform, so asking "what would the capsule hit starting from
/// that past position" never disturbs the character the player is looking at.
/// Moving a real node to answer a historical query — the obvious alternative —
/// would fight presentation and make replay visible.
/// </para>
/// <para>
/// Queries are masked to static geometry only. Dynamic bodies and other
/// characters are deliberately excluded: player-versus-player collision is
/// frame-aligned and belongs to Phase 9, and letting it leak in here would make
/// one character's replay depend on another's current position.
/// </para>
/// </remarks>
public sealed partial class GodotKinematicCollisionWorld : Node3D, ICharacterCollisionWorld
{
    private readonly Dictionary<CollisionProfileKind, ProfileBody> _profiles = [];
    private readonly PhysicsTestMotionResult3D _result = new();

    /// <summary>
    /// Collider ObjectID to its authored scene path, resolved once per collider.
    /// </summary>
    /// <remarks>
    /// Cached because <c>InstanceFromId</c> plus <c>GetPath</c> is far too expensive
    /// for a query issued several times per frame and multiplied again by replay
    /// depth. The set of static colliders is fixed for a level, so this fills once
    /// and then only reads.
    /// </remarks>
    private readonly Dictionary<ulong, string> _colliderPaths = [];

    private PhysicsTestMotionParameters3D _parameters = new();
    private bool _initialized;

    /// <summary>
    /// Creates the per-profile query bodies and shapes.
    /// </summary>
    /// <remarks>
    /// Shapes are created once and reused for the world's lifetime. Creating them
    /// per query would allocate on the hot path and, worse, make query cost
    /// depend on how many frames replay is resimulating.
    /// </remarks>
    public void Initialize(CollisionProfileTable profiles, uint staticWorldMask)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ReleaseProfiles();

        foreach (var kind in new[]
        {
            CollisionProfileKind.Standing,
            CollisionProfileKind.Crouching,
            CollisionProfileKind.Rolling,
        })
        {
            _profiles[kind] = CreateProfile(profiles.For(kind), staticWorldMask);
        }

        _parameters = new PhysicsTestMotionParameters3D
        {
            // Recovery is the motor's job, through ResolveOverlap, so the query
            // reports contacts rather than silently depenetrating. The probe ran
            // the same way.
            RecoveryAsCollision = false,
            MaxCollisions = MaximumReportedCollisions,
        };
        _initialized = true;
    }

    /// <inheritdoc />
    public CapsuleSweepResult Sweep(
        in CapsuleSweepRequest request,
        Span<CollisionContactState> contacts)
    {
        var body = RequireProfile(request.Profile);
        var motion = new Vector3(
            (float)request.HorizontalMotion.X,
            (float)request.VerticalMotion,
            (float)request.HorizontalMotion.Z);

        _parameters.From = new Transform3D(Basis.Identity, ToEngine(request.Origin));
        _parameters.Motion = motion;
        var collided = PhysicsServer3D.BodyTestMotion(body.Rid, _parameters, _result);

        var travel = _result.GetTravel();
        var remainder = _result.GetRemainder();
        var achievedHorizontal = new HorizontalVector(travel.X, travel.Z);
        var remainingHorizontal = new HorizontalVector(remainder.X, remainder.Z);

        var count = 0;
        if (collided)
        {
            var reported = Math.Min(_result.GetCollisionCount(), contacts.Length);
            for (var index = 0; index < reported; index++)
            {
                var normal = NormalFromEngine(_result.GetCollisionNormal(index));
                var point = FromEngine(_result.GetCollisionPoint(index));
                contacts[count++] = new CollisionContactState(
                    point,
                    normal,
                    ClampFraction(travel, motion),
                    Math.Max(0d, _result.GetCollisionDepth(index)),
                    IdentityFor(_result.GetColliderId(index), _result.GetColliderShape(index)),
                    CollisionContactState.ClassifySurface(normal, request.WalkableSlopeRadians));
            }
        }

        return new CapsuleSweepResult(
            achievedHorizontal,
            travel.Y,
            remainingHorizontal,
            remainder.Y,
            count);
    }

    /// <inheritdoc />
    public GroundProbeResult ProbeGround(in GroundProbeRequest request)
    {
        var body = RequireProfile(request.Profile);
        _parameters.From = new Transform3D(Basis.Identity, ToEngine(request.Origin));
        _parameters.Motion = new Vector3(0f, -(float)request.MaximumDistance, 0f);

        if (!PhysicsServer3D.BodyTestMotion(body.Rid, _parameters, _result))
        {
            return GroundProbeResult.None;
        }

        var travel = _result.GetTravel();
        return new GroundProbeResult(
            true,
            Math.Abs(travel.Y),
            NormalFromEngine(_result.GetCollisionNormal()),
            IdentityFor(_result.GetColliderId(), _result.GetColliderShape()));
    }

    /// <inheritdoc />
    public bool HasClearance(in ClearanceRequest request) =>
        !ResolveOverlap(request).IsOverlapping;

    /// <inheritdoc />
    /// <remarks>
    /// Probes with a negligible motion rather than zero: the motion test reports
    /// existing overlap through its recovery vector, and a strictly zero motion
    /// gives the solver nothing to report against. Penetration recovery runs
    /// before any motion exists and needs a separation direction and depth, not a
    /// travel fraction, which is why this is a distinct query rather than a
    /// sweep.
    /// </remarks>
    public OverlapResolution ResolveOverlap(in ClearanceRequest request)
    {
        var body = RequireProfile(request.Profile);
        _parameters.From = new Transform3D(Basis.Identity, ToEngine(request.Origin));
        _parameters.Motion = new Vector3(0f, -OverlapProbeMotion, 0f);
        _parameters.RecoveryAsCollision = true;
        try
        {
            if (!PhysicsServer3D.BodyTestMotion(body.Rid, _parameters, _result))
            {
                return OverlapResolution.None;
            }

            var depth = _result.GetCollisionDepth();
            if (depth <= 0d)
            {
                return OverlapResolution.None;
            }

            var normal = _result.GetCollisionNormal();
            return new OverlapResolution(
                true,
                new HorizontalVector(normal.X * depth, normal.Z * depth),
                normal.Y * depth,
                depth);
        }
        finally
        {
            _parameters.RecoveryAsCollision = false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns false for every surface in the arena today, which is entirely
    /// static. The contract exists so moving platforms can be added later
    /// without reopening the motor.
    /// </remarks>
    public bool TryGetSupportMotion(
        SupportIdentity support,
        SimulationInstant fromFrame,
        SimulationInstant toFrame,
        out HorizontalVector horizontal,
        out double vertical)
    {
        horizontal = HorizontalVector.Zero;
        vertical = 0d;
        return false;
    }

    /// <summary>
    /// Releases the precreated physics-server bodies and shapes.
    /// </summary>
    /// <remarks>
    /// These are server-side resources that outlive node freeing, so they are
    /// released explicitly. <c>_ExitTree</c> is the path the P01-09 probe
    /// verified, so it is the one used here.
    /// </remarks>
    public override void _ExitTree() => ReleaseProfiles();

    private const int MaximumReportedCollisions = 8;
    private const float OverlapProbeMotion = 0.0001f;

    /// <summary>
    /// Turns an engine collider into an identity that means the same thing in every
    /// process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason this is not simply <c>colliderRid.Id</c>, which it used to be: a
    /// RID is a physics-server allocation handle, stable only within one process.
    /// Support and contacts are discrete comparison fields checked before numeric
    /// ones, so a per-process identity would mismatch on every grounded frame
    /// between authority and owner and correct the player continuously.
    /// </para>
    /// <para>
    /// Resolves the collider's ObjectID to its node and takes the <em>owning body's</em>
    /// path. Deliberately not the shape node's path: shapes here are added as
    /// unnamed children, so Godot assigns them <c>@CollisionShape3D@&lt;counter&gt;</c>,
    /// which is instantiation order wearing a disguise.
    /// </para>
    /// <para>
    /// A collider that cannot be resolved yields <see cref="SupportIdentity.None"/>
    /// rather than a fabricated value. An invalid support reads as "not standing on
    /// anything", which is visibly wrong and gets investigated; a fabricated one
    /// would compare unequal across processes and produce a correction storm that
    /// looks like a network problem.
    /// </para>
    /// </remarks>
    private SupportIdentity IdentityFor(ulong colliderObjectId, int shapeIndex)
    {
        if (colliderObjectId == 0UL)
        {
            return SupportIdentity.None;
        }

        if (!_colliderPaths.TryGetValue(colliderObjectId, out var path))
        {
            path = ResolveBodyPath(colliderObjectId);
            _colliderPaths[colliderObjectId] = path;
        }

        return path.Length == 0
            ? SupportIdentity.None
            : SceneColliderIdentity.FromBodyPath(path, shapeIndex);
    }

    private static string ResolveBodyPath(ulong colliderObjectId)
    {
        var instance = GodotObject.InstanceFromId(colliderObjectId);
        return instance is Node node ? node.GetPath().ToString() : string.Empty;
    }
    private ProfileBody RequireProfile(CollisionProfileKind kind)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "The collision world must be initialized with a profile table before use.");
        }

        return _profiles.TryGetValue(kind, out var body)
            ? body
            : throw new ArgumentOutOfRangeException(nameof(kind));
    }

    private ProfileBody CreateProfile(CollisionProfileDimensions dimensions, uint staticWorldMask)
    {
        var shape = new CapsuleShape3D
        {
            Radius = (float)dimensions.Radius,
            Height = (float)dimensions.Height,
        };
        var rid = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(rid, PhysicsServer3D.BodyMode.Kinematic);

        // Layer zero: this body is a query probe and must never be collided
        // against by anything else.
        PhysicsServer3D.BodySetCollisionLayer(rid, 0);
        PhysicsServer3D.BodySetCollisionMask(rid, staticWorldMask);

        // The capsule shape is centred on its origin, while simulation treats the
        // position as the FOOT. Offsetting by half the height is what reconciles
        // the two, and getting it wrong sinks the character into the floor.
        PhysicsServer3D.BodyAddShape(
            rid,
            shape.GetRid(),
            new Transform3D(Basis.Identity, new Vector3(0f, (float)dimensions.HalfHeight, 0f)));
        PhysicsServer3D.BodySetSpace(rid, GetWorld3D().Space);
        return new ProfileBody(rid, shape);
    }

    private void ReleaseProfiles()
    {
        foreach (var profile in _profiles.Values)
        {
            if (profile.Rid.IsValid)
            {
                PhysicsServer3D.FreeRid(profile.Rid);
            }

            profile.Shape.Dispose();
        }

        _profiles.Clear();
        _initialized = false;
    }

    private static double ClampFraction(Vector3 travel, Vector3 motion)
    {
        var motionLength = motion.Length();
        return motionLength <= 1e-6f
            ? 0d
            : Math.Clamp(travel.Length() / motionLength, 0d, 1d);
    }

    /// <summary>
    /// Converts a simulation position into the engine's vector type.
    /// </summary>
    /// <remarks>
    /// The one place the conversion happens, and the reason simulation carries
    /// its own <see cref="WorldPosition"/>: <c>BattleArena.Core</c> stays free of
    /// any engine reference, and the precision boundary between the double-based
    /// simulation and the engine's float transforms is explicit and auditable
    /// rather than scattered. Positions are effectively on the float grid after
    /// any contact; the doubles buy protocol quantization convenience, not
    /// precision that survives a sweep.
    /// </remarks>
    private static Vector3 ToEngine(WorldPosition position) =>
        new((float)position.X, (float)position.Y, (float)position.Z);

    private static WorldPosition FromEngine(Vector3 position) =>
        new(position.X, position.Y, position.Z);

    private static SurfaceNormal NormalFromEngine(Vector3 normal) =>
        normal.LengthSquared() <= 1e-9f
            ? SurfaceNormal.Up
            : new SurfaceNormal(normal.X, normal.Y, normal.Z);

    private readonly record struct ProfileBody(Rid Rid, CapsuleShape3D Shape);
}
