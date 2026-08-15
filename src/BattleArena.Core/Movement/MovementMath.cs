namespace BattleArena.Core.Movement;

public static class MovementMath
{
    public const double Epsilon = 0.000_001d;
    public const double EpsilonSquared = Epsilon * Epsilon;

    public static double MoveTowards(double current, double target, double maximumDelta)
    {
        if (maximumDelta < 0d || !double.IsFinite(maximumDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelta));
        }

        if (Math.Abs(target - current) <= maximumDelta)
        {
            return target;
        }

        return current + (Math.Sign(target - current) * maximumDelta);
    }

    public static double WrapAngle(double radians)
    {
        var wrapped = Math.IEEERemainder(radians, Math.Tau);
        return wrapped == -Math.PI ? Math.PI : wrapped;
    }

    public static double MoveAngleTowards(double current, double target, double maximumDelta)
    {
        var difference = WrapAngle(target - current);
        return Math.Abs(difference) <= maximumDelta
            ? WrapAngle(target)
            : WrapAngle(current + (Math.Sign(difference) * maximumDelta));
    }

    public static HorizontalVector RotateInputByViewYaw(HorizontalVector input, double yawRadians)
    {
        var cosine = Math.Cos(yawRadians);
        var sine = Math.Sin(yawRadians);
        return new HorizontalVector(
            (cosine * input.X) + (sine * input.Z),
            (-sine * input.X) + (cosine * input.Z));
    }

    public static HorizontalVector RotateTowards(
        HorizontalVector currentDirection,
        HorizontalVector targetDirection,
        double maximumAngle)
    {
        var current = currentDirection.Normalized;
        var target = targetDirection.Normalized;
        if (current == HorizontalVector.Zero || target == HorizontalVector.Zero)
        {
            return target;
        }

        var dot = Math.Clamp(current.Dot(target), -1d, 1d);
        var cross = (current.X * target.Z) - (current.Z * target.X);
        var difference = Math.Atan2(cross, dot);
        var appliedAngle = Math.Clamp(difference, -maximumAngle, maximumAngle);
        var cosine = Math.Cos(appliedAngle);
        var sine = Math.Sin(appliedAngle);
        return new HorizontalVector(
            (current.X * cosine) - (current.Z * sine),
            (current.X * sine) + (current.Z * cosine)).Normalized;
    }

    public static double FacingYawFromDirection(HorizontalVector direction)
    {
        var normalized = direction.Normalized;
        return normalized == HorizontalVector.Zero
            ? 0d
            : WrapAngle(Math.Atan2(-normalized.X, -normalized.Z));
    }
}
