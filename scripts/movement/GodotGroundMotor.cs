#nullable enable

using BattleArena.Core.Movement;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Applies proposed velocity to Godot. Traversable ledges are resolved with
/// geometry-independent up, forward, and down shape sweeps before committing a
/// transform; ordinary collision remains delegated to MoveAndSlide.
/// </summary>
public sealed class GodotGroundMotor
{
    private const float MinimumUsefulProgress = 0.005f;
    private const float MinimumStepRise = 0.005f;
    private const float SweepTolerance = 0.002f;

    private readonly PhysicsTestMotionParameters3D _testParameters = new()
    {
        MaxCollisions = 8,
        RecoveryAsCollision = false,
        CollideSeparationRay = false,
        Margin = 0.001f,
    };

    private readonly PhysicsTestMotionResult3D _directResult = new();
    private readonly PhysicsTestMotionResult3D _upResult = new();
    private readonly PhysicsTestMotionResult3D _forwardResult = new();
    private readonly PhysicsTestMotionResult3D _downResult = new();

    public StepTraversalRejectionReason LastRejectionReason { get; private set; }

    public StepTraversalOutcome Move(
        CharacterBody3D body,
        HorizontalVector proposedHorizontalVelocity,
        double proposedVerticalVelocity,
        double delta,
        GroundMovementAttributes attributes,
        bool? groundedOverride = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(attributes);
        if (!double.IsFinite(delta) || delta <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        body.FloorSnapLength = (float)attributes.FloorSnapDistance;
        body.FloorMaxAngle = (float)attributes.MaximumFloorAngleRadians;
        LastRejectionReason = StepTraversalRejectionReason.None;

        var requestedVelocity = new Vector3(
            (float)proposedHorizontalVelocity.X,
            (float)proposedVerticalVelocity,
            (float)proposedHorizontalVelocity.Z);
        var horizontalMotion = new Vector3(
            requestedVelocity.X * (float)delta,
            0f,
            requestedVelocity.Z * (float)delta);

        if ((groundedOverride ?? body.IsOnFloor()) &&
            horizontalMotion.LengthSquared() > MovementMath.EpsilonSquared)
        {
            var startingTransform = body.GlobalTransform;
            var direction = new Vector2(horizontalMotion.X, horizontalMotion.Z).Normalized();
            var directCollision = TestMotion(body, startingTransform, horizontalMotion, _directResult);
            var directTravel = directCollision ? _directResult.GetTravel() : horizontalMotion;
            var directProgress = DirectionalProgress(directTravel, direction);
            var floorNormalThreshold = MathF.Cos((float)attributes.MaximumFloorAngleRadians);

            if (directCollision &&
                HasBlockingCollision(_directResult, floorNormalThreshold) &&
                directProgress + MinimumUsefulProgress < horizontalMotion.Length() &&
                TryResolveStep(
                    body,
                    startingTransform,
                    horizontalMotion,
                    direction,
                    directProgress,
                    floorNormalThreshold,
                    attributes,
                    out var landingTransform))
            {
                body.GlobalTransform = landingTransform;

                // Register the landing with CharacterBody3D without applying the
                // horizontal request twice. The caller restores resolved planar
                // velocity to the simulation state after this method returns.
                body.Velocity = Vector3.Down * 0.5f;
                body.MoveAndSlide();
                body.Velocity = new Vector3(requestedVelocity.X, body.Velocity.Y, requestedVelocity.Z);
                return StepTraversalOutcome.Accepted;
            }

            if (directCollision && HasBlockingCollision(_directResult, floorNormalThreshold))
            {
                body.Velocity = requestedVelocity;
                body.MoveAndSlide();
                return StepTraversalOutcome.Blocked;
            }
        }

        body.Velocity = requestedVelocity;
        body.MoveAndSlide();
        return StepTraversalOutcome.NotNeeded;
    }

    private bool TryResolveStep(
        CharacterBody3D body,
        Transform3D startingTransform,
        Vector3 horizontalMotion,
        Vector2 requestedDirection,
        float directProgress,
        float floorNormalThreshold,
        GroundMovementAttributes attributes,
        out Transform3D landingTransform)
    {
        landingTransform = startingTransform;
        var stepHeight = (float)attributes.MaximumStepHeight;
        var upwardMotion = Vector3.Up * stepHeight;
        var upwardCollision = TestMotion(body, startingTransform, upwardMotion, _upResult);
        var upwardTravel = upwardCollision ? _upResult.GetTravel() : upwardMotion;
        if (upwardTravel.Y < stepHeight - SweepTolerance)
        {
            LastRejectionReason = StepTraversalRejectionReason.UpwardClearance;
            return false;
        }

        var raisedTransform = WithOrigin(startingTransform, startingTransform.Origin + upwardMotion);
        var forwardAssist = new Vector3(
            requestedDirection.X,
            0f,
            requestedDirection.Y) * (float)attributes.StepForwardAssistDistance;
        var assistedForwardMotion = horizontalMotion + forwardAssist;
        var forwardCollision = TestMotion(body, raisedTransform, assistedForwardMotion, _forwardResult);
        var forwardTravel = forwardCollision ? _forwardResult.GetTravel() : assistedForwardMotion;
        var forwardProgress = DirectionalProgress(forwardTravel, requestedDirection);
        if (forwardProgress <= directProgress + MinimumUsefulProgress)
        {
            LastRejectionReason = StepTraversalRejectionReason.InsufficientForwardProgress;
            return false;
        }

        var forwardTransform = WithOrigin(raisedTransform, raisedTransform.Origin + forwardTravel);
        var downwardMotion = Vector3.Down * (stepHeight + (float)attributes.FloorSnapDistance);
        if (!TestMotion(body, forwardTransform, downwardMotion, _downResult))
        {
            LastRejectionReason = StepTraversalRejectionReason.NoLanding;
            return false;
        }

        if (!HasWalkableCollision(_downResult, floorNormalThreshold))
        {
            LastRejectionReason = StepTraversalRejectionReason.UnwalkableLanding;
            return false;
        }

        var downwardTravel = _downResult.GetTravel();
        var landingOrigin = forwardTransform.Origin + downwardTravel;
        var rise = landingOrigin.Y - startingTransform.Origin.Y;
        if (rise < MinimumStepRise || rise > stepHeight + SweepTolerance)
        {
            LastRejectionReason = StepTraversalRejectionReason.InvalidRise;
            return false;
        }

        landingTransform = WithOrigin(startingTransform, landingOrigin);
        return true;
    }

    private bool TestMotion(
        CharacterBody3D body,
        Transform3D from,
        Vector3 motion,
        PhysicsTestMotionResult3D result)
    {
        _testParameters.From = from;
        _testParameters.Motion = motion;
        return PhysicsServer3D.BodyTestMotion(body.GetRid(), _testParameters, result);
    }

    private static bool HasBlockingCollision(
        PhysicsTestMotionResult3D result,
        float floorNormalThreshold)
    {
        for (var index = 0; index < result.GetCollisionCount(); index++)
        {
            if (result.GetCollisionNormal(index).Y < floorNormalThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasWalkableCollision(
        PhysicsTestMotionResult3D result,
        float floorNormalThreshold)
    {
        for (var index = 0; index < result.GetCollisionCount(); index++)
        {
            if (result.GetCollisionNormal(index).Y >= floorNormalThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static float DirectionalProgress(Vector3 travel, Vector2 requestedDirection) =>
        new Vector2(travel.X, travel.Z).Dot(requestedDirection);

    private static Transform3D WithOrigin(Transform3D transform, Vector3 origin) =>
        new(transform.Basis, origin);
}
