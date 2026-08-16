using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

/// <summary>
/// A collision world made of axis-aligned boxes and bounded planes, with exact
/// authored geometry.
/// </summary>
/// <remarks>
/// <para>
/// This is the second implementation <see cref="ICharacterCollisionWorld"/>
/// exists for, and it is what makes the motor's rules testable as pure logic.
/// Against a headless engine a failing slide or step test leaves it ambiguous
/// whether the rule is wrong or the engine behaved differently than assumed;
/// here the geometry is exact and the expected contact is arithmetic.
/// </para>
/// <para>
/// The capsule is approximated as an axis-aligned box of width 2r and the
/// profile's height. That is deliberately cruder than a real capsule: these
/// tests are about ordering, classification, snapping, and stepping decisions,
/// not about the exact curvature of a hemisphere, and a swept-box test is
/// simple enough to be obviously correct.
/// </para>
/// </remarks>
public sealed class DeterministicCollisionWorld : ICharacterCollisionWorld
{
    private readonly List<Body> _bodies = [];
    private readonly CollisionProfileTable _profiles;

    public DeterministicCollisionWorld(CollisionProfileTable profiles) => _profiles = profiles;

    /// <summary>Number of sweeps issued, so tests can assert on query cost.</summary>
    public int SweepCount { get; private set; }

    public DeterministicCollisionWorld AddBox(
        ulong colliderId,
        WorldPosition min,
        WorldPosition max,
        int shapeIndex = 0)
    {
        _bodies.Add(new Body(colliderId, shapeIndex, min, max, null));
        return this;
    }

    /// <summary>
    /// A box whose upward face reports an authored normal, standing in for a
    /// ramp without needing a general convex sweep.
    /// </summary>
    public DeterministicCollisionWorld AddSloped(
        ulong colliderId,
        WorldPosition min,
        WorldPosition max,
        SurfaceNormal topNormal,
        int shapeIndex = 0)
    {
        _bodies.Add(new Body(colliderId, shapeIndex, min, max, topNormal));
        return this;
    }

    /// <summary>Ground plane spanning the usable test area.</summary>
    public DeterministicCollisionWorld AddGround(double y = 0d, ulong colliderId = 1) =>
        AddBox(
            colliderId,
            new WorldPosition(-1000d, y - 10d, -1000d),
            new WorldPosition(1000d, y, 1000d));

    public CapsuleSweepResult Sweep(
        in CapsuleSweepRequest request,
        Span<CollisionContactState> contacts)
    {
        SweepCount++;
        var dimensions = _profiles.For(request.Profile);
        var motion = new Motion(request.HorizontalMotion, request.VerticalMotion);
        var (min, max) = CapsuleBounds(request.Origin, dimensions);

        var earliest = 1d;
        var count = 0;
        foreach (var body in _bodies)
        {
            if (body.Matches(request.ExcludedCollider))
            {
                continue;
            }
            if (!TrySweepBox(min, max, motion, body, out var fraction, out var normal))
            {
                continue;
            }

            if (count < contacts.Length)
            {
                contacts[count] = new CollisionContactState(
                    request.Origin.Offset(
                        request.HorizontalMotion * fraction,
                        request.VerticalMotion * fraction),
                    normal,
                    fraction,
                    0d,
                    new SupportIdentity(body.ColliderId, body.ShapeIndex),
                    CollisionContactState.ClassifySurface(normal, WalkableSlopeRadians));
                count++;
            }

            earliest = Math.Min(earliest, fraction);
        }

        var achievedHorizontal = request.HorizontalMotion * earliest;
        var achievedVertical = request.VerticalMotion * earliest;
        return new CapsuleSweepResult(
            achievedHorizontal,
            achievedVertical,
            request.HorizontalMotion - achievedHorizontal,
            request.VerticalMotion - achievedVertical,
            count);
    }

    public GroundProbeResult ProbeGround(in GroundProbeRequest request)
    {
        var dimensions = _profiles.For(request.Profile);
        var (min, max) = CapsuleBounds(request.Origin, dimensions);
        var motion = new Motion(HorizontalVector.Zero, -request.MaximumDistance);

        var best = double.MaxValue;
        Body? found = null;
        SurfaceNormal normal = SurfaceNormal.Up;
        foreach (var body in _bodies)
        {
            if (!TrySweepBox(min, max, motion, body, out var fraction, out var hitNormal))
            {
                continue;
            }

            var distance = fraction * request.MaximumDistance;
            if (distance >= best)
            {
                continue;
            }

            best = distance;
            found = body;
            normal = hitNormal;
        }

        return found is { } support
            ? new GroundProbeResult(
                true,
                best,
                normal,
                new SupportIdentity(support.ColliderId, support.ShapeIndex))
            : GroundProbeResult.None;
    }

    public bool HasClearance(in ClearanceRequest request) =>
        !ResolveOverlap(request).IsOverlapping;

    public OverlapResolution ResolveOverlap(in ClearanceRequest request)
    {
        var dimensions = _profiles.For(request.Profile);
        var (min, max) = CapsuleBounds(request.Origin, dimensions);

        var deepest = 0d;
        var separationHorizontal = HorizontalVector.Zero;
        var separationVertical = 0d;
        foreach (var body in _bodies)
        {
            if (body.Matches(request.ExcludedCollider) || !body.Overlaps(min, max))
            {
                continue;
            }

            // Shortest exit along the least-penetrated axis, which is the
            // standard minimum-translation resolution for boxes.
            var pushX = body.Max.X - min.X < max.X - body.Min.X
                ? body.Max.X - min.X
                : -(max.X - body.Min.X);
            var pushY = body.Max.Y - min.Y < max.Y - body.Min.Y
                ? body.Max.Y - min.Y
                : -(max.Y - body.Min.Y);
            var pushZ = body.Max.Z - min.Z < max.Z - body.Min.Z
                ? body.Max.Z - min.Z
                : -(max.Z - body.Min.Z);

            var absX = Math.Abs(pushX);
            var absY = Math.Abs(pushY);
            var absZ = Math.Abs(pushZ);
            var depth = Math.Min(absX, Math.Min(absY, absZ));
            if (depth <= deepest)
            {
                continue;
            }

            deepest = depth;
            if (absY <= absX && absY <= absZ)
            {
                separationHorizontal = HorizontalVector.Zero;
                separationVertical = pushY;
            }
            else if (absX <= absZ)
            {
                separationHorizontal = new HorizontalVector(pushX, 0d);
                separationVertical = 0d;
            }
            else
            {
                separationHorizontal = new HorizontalVector(0d, pushZ);
                separationVertical = 0d;
            }
        }

        return deepest > 0d
            ? new OverlapResolution(true, separationHorizontal, separationVertical, deepest)
            : OverlapResolution.None;
    }

    public bool TryGetSupportMotion(
        SupportIdentity support,
        SimulationInstant fromFrame,
        SimulationInstant toFrame,
        out HorizontalVector horizontal,
        out double vertical)
    {
        // Every surface here is static, matching the arena today.
        horizontal = HorizontalVector.Zero;
        vertical = 0d;
        return false;
    }

    private const double WalkableSlopeRadians = 0.7853981633974483d; // 45 degrees

    private static (WorldPosition Min, WorldPosition Max) CapsuleBounds(
        WorldPosition foot,
        CollisionProfileDimensions dimensions) => (
            new WorldPosition(foot.X - dimensions.Radius, foot.Y, foot.Z - dimensions.Radius),
            new WorldPosition(
                foot.X + dimensions.Radius,
                foot.Y + dimensions.Height,
                foot.Z + dimensions.Radius));

    private static bool TrySweepBox(
        WorldPosition min,
        WorldPosition max,
        Motion motion,
        Body body,
        out double fraction,
        out SurfaceNormal normal)
    {
        fraction = 0d;
        normal = SurfaceNormal.Up;

        // Standard slab method: latest entry across axes, earliest exit.
        var entry = double.NegativeInfinity;
        var exit = double.PositiveInfinity;
        var axis = -1;
        var sign = 0d;

        Span<double> minA = [min.X, min.Y, min.Z];
        Span<double> maxA = [max.X, max.Y, max.Z];
        Span<double> minB = [body.Min.X, body.Min.Y, body.Min.Z];
        Span<double> maxB = [body.Max.X, body.Max.Y, body.Max.Z];
        Span<double> delta = [motion.Horizontal.X, motion.Vertical, motion.Horizontal.Z];

        for (var i = 0; i < 3; i++)
        {
            if (Math.Abs(delta[i]) < 1e-12d)
            {
                if (maxA[i] <= minB[i] || minA[i] >= maxB[i])
                {
                    return false;
                }
                continue;
            }

            var t1 = (minB[i] - maxA[i]) / delta[i];
            var t2 = (maxB[i] - minA[i]) / delta[i];
            var near = Math.Min(t1, t2);
            var far = Math.Max(t1, t2);
            if (near > entry)
            {
                entry = near;
                axis = i;
                sign = delta[i] > 0d ? -1d : 1d;
            }
            exit = Math.Min(exit, far);
        }

        // A negative entry time means the collision lies behind the motion, not
        // ahead of it — which is exactly what a capsule resting on the floor and
        // moving upward produces. Clamping it to zero instead would report the
        // floor as blocking the rise, and the step solver could never lift.
        // Already-overlapping cases are the recovery stage's job, not the
        // sweep's.
        if (axis < 0 || entry > exit || entry > 1d || entry < 0d)
        {
            return false;
        }

        fraction = Math.Clamp(entry, 0d, 1d);
        normal = axis switch
        {
            0 => new SurfaceNormal(sign, 0d, 0d),
            1 => sign > 0d && body.TopNormal is { } sloped ? sloped : new SurfaceNormal(0d, sign, 0d),
            _ => new SurfaceNormal(0d, 0d, sign),
        };
        return true;
    }

    private readonly record struct Motion(HorizontalVector Horizontal, double Vertical);

    private sealed record Body(
        ulong ColliderId,
        int ShapeIndex,
        WorldPosition Min,
        WorldPosition Max,
        SurfaceNormal? TopNormal)
    {
        public bool Matches(SupportIdentity identity) =>
            identity.IsValid && identity.ColliderId == ColliderId;

        public bool Overlaps(WorldPosition min, WorldPosition max) =>
            min.X < Max.X && max.X > Min.X &&
            min.Y < Max.Y && max.Y > Min.Y &&
            min.Z < Max.Z && max.Z > Min.Z;
    }
}
