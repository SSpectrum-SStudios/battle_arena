namespace BattleArena.Core.Common;

public readonly record struct InstallationSequence : IComparable<InstallationSequence>
{
    public InstallationSequence(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An installation sequence cannot be negative.");
        }

        Value = value;
    }

    public long Value { get; }

    public int CompareTo(InstallationSequence other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
