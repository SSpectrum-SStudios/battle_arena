namespace BattleArena.Core.Movement;

public readonly record struct HorizontalVector(double X, double Z)
{
    public static readonly HorizontalVector Zero = new(0d, 0d);

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Z);

    public double LengthSquared => (X * X) + (Z * Z);

    public double Length => Math.Sqrt(LengthSquared);

    public HorizontalVector Normalized => LengthSquared > MovementMath.EpsilonSquared
        ? this / Length
        : Zero;

    public HorizontalVector ClampLength(double maximumLength)
    {
        if (!double.IsFinite(maximumLength) || maximumLength < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        return LengthSquared > maximumLength * maximumLength
            ? Normalized * maximumLength
            : this;
    }

    public double Dot(HorizontalVector other) => (X * other.X) + (Z * other.Z);

    public static HorizontalVector operator +(HorizontalVector left, HorizontalVector right) =>
        new(left.X + right.X, left.Z + right.Z);

    public static HorizontalVector operator -(HorizontalVector left, HorizontalVector right) =>
        new(left.X - right.X, left.Z - right.Z);

    public static HorizontalVector operator *(HorizontalVector vector, double scalar) =>
        new(vector.X * scalar, vector.Z * scalar);

    public static HorizontalVector operator /(HorizontalVector vector, double scalar) =>
        new(vector.X / scalar, vector.Z / scalar);
}
