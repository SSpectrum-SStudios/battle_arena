using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

/// <summary>
/// P06-02: the diagnostic hash, and the properties that make it worth having.
/// </summary>
/// <remarks>
/// The hash is never a correction trigger, so these are not about correctness of
/// movement — they are about whether a trace comparison means anything. A hash that
/// disagrees between two endpoints that simulated the same thing is worse than no
/// hash, because it sends someone investigating a divergence that did not happen.
/// </remarks>
public sealed class CanonicalMovementStateHashTests
{
    private static readonly CombatantAuthorityPredictionEpoch Epoch = new(
        10,
        new MatchFrameEpochId(1),
        new CombatantId(4),
        new LifeGenerationId(1),
        new AuthorityDiscontinuityId(1),
        new OwnerControlEpoch(3));

    [Fact]
    public void TheSameStateAlwaysHashesTheSame()
    {
        var state = State(x: 1.25d);
        Assert.Equal(
            CanonicalMovementStateHash.Compute(state, Epoch),
            CanonicalMovementStateHash.Compute(state, Epoch));
    }

    [Fact]
    public void DifferencesFinerThanQuantizationDoNotChangeTheHash()
    {
        // The point of quantizing. Two machines will differ in the last bits of
        // arithmetic every frame; without this the hash would report a mismatch
        // continuously and mean nothing.
        var left = CanonicalMovementStateHash.Compute(State(x: 1.0d), Epoch);
        var right = CanonicalMovementStateHash.Compute(State(x: 1.0d + 1e-9d), Epoch);

        Assert.Equal(left, right);
    }

    [Fact]
    public void ARealDivergenceStillChangesTheHash()
    {
        // And the quantization must not be so coarse that a difference worth
        // correcting disappears. A centimetre is far below any tolerance.
        Assert.NotEqual(
            CanonicalMovementStateHash.Compute(State(x: 1.0d), Epoch),
            CanonicalMovementStateHash.Compute(State(x: 1.01d), Epoch));
    }

    [Fact]
    public void ContactOrderDoesNotChangeTheHash()
    {
        // The defect this was written to avoid: contacts are stored in the motor's
        // insertion order, so hashing as stored would make two endpoints that agree
        // about a corner report different hashes -- classified as
        // DiagnosticHashOnly, which the design documents as "a bug to investigate".
        var floor = new FrameContactRecord(
            new SupportIdentity(10, 0), SurfaceNormal.Up, ContactSurfaceKind.WalkableGround);
        var wall = new FrameContactRecord(
            new SupportIdentity(20, 0), new SurfaceNormal(-1d, 0d, 0d), ContactSurfaceKind.Wall);

        var forward = default(FrameContactBuffer);
        forward.TryAdd(floor);
        forward.TryAdd(wall);

        var reversed = default(FrameContactBuffer);
        reversed.TryAdd(wall);
        reversed.TryAdd(floor);

        Assert.Equal(
            CanonicalMovementStateHash.Compute(State().WithContacts(forward), Epoch),
            CanonicalMovementStateHash.Compute(State().WithContacts(reversed), Epoch));

        // And the contacts genuinely participate, so the test above is not passing
        // because contacts are ignored entirely.
        Assert.NotEqual(
            CanonicalMovementStateHash.Compute(State().WithContacts(forward), Epoch),
            CanonicalMovementStateHash.Compute(State(), Epoch));
    }

    [Fact]
    public void TheEpochParticipates()
    {
        // Two states can be numerically identical and belong to different lives.
        // Comparing those is meaningless, so they must not hash alike.
        var other = new CombatantAuthorityPredictionEpoch(
            10,
            new MatchFrameEpochId(1),
            new CombatantId(4),
            new LifeGenerationId(2),
            new AuthorityDiscontinuityId(1),
            new OwnerControlEpoch(3));

        Assert.NotEqual(
            CanonicalMovementStateHash.Compute(State(), Epoch),
            CanonicalMovementStateHash.Compute(State(), other));
    }

    [Fact]
    public void DiscreteFieldsParticipate()
    {
        var grounded = State();
        var airborne = grounded.WithKinematic(
            grounded.Kinematic.WithGround(false, SurfaceNormal.Up, SupportIdentity.None));

        Assert.NotEqual(
            CanonicalMovementStateHash.Compute(grounded, Epoch),
            CanonicalMovementStateHash.Compute(airborne, Epoch));
    }

    [Fact]
    public void TheCanonicalEncodingIsPinnedByteForByte()
    {
        // A hash test alone cannot tell "the field set changed" from "the hash
        // function changed", and only the first is a correctness problem. Pinning a
        // prefix of the bytes makes a field-set change fail loudly and specifically.
        Span<byte> bytes = stackalloc byte[CanonicalMovementStateHash.MaximumCanonicalBytes];
        var written = CanonicalMovementStateHash.WriteCanonicalBytes(State(), Epoch, bytes);

        Assert.True(written > 0);
        Assert.True(written <= CanonicalMovementStateHash.MaximumCanonicalBytes);

        // Schema version leads, little-endian.
        Assert.Equal(CanonicalMovementStateHash.SchemaVersion, BitConverter.ToUInt32(bytes[..4]));

        // Then match-frame epoch, so a version bump moves every subsequent field.
        Assert.Equal(1UL, BitConverter.ToUInt64(bytes[4..12]));
    }

    [Fact]
    public void ADestinationTooSmallIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> tooSmall = stackalloc byte[8];
            CanonicalMovementStateHash.WriteCanonicalBytes(State(), Epoch, tooSmall);
        });
    }

    [Fact]
    public void ADefaultHashIsNotValid()
    {
        // So an unset field cannot be mistaken for a computed hash that happened to
        // be zero.
        Assert.False(default(CanonicalMovementStateHash).IsValid);
        Assert.True(CanonicalMovementStateHash.Compute(State(), Epoch).IsValid);
    }

    private static CharacterSimulationState State(double x = 0d) =>
        CharacterSimulationState.CreateGrounded(
            new WorldPosition(x, 0d, 0d),
            0d,
            new SimulationInstant(100),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));
}
