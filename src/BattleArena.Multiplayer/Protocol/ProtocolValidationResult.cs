namespace BattleArena.Multiplayer.Protocol;

public readonly record struct ProtocolValidationResult(ProtocolViolation? Violation)
{
    public static ProtocolValidationResult Valid => new(null);

    public bool IsValid => Violation is null;

    public static ProtocolValidationResult Invalid(ProtocolViolationCode code, string message) =>
        new(new ProtocolViolation(code, message));
}
