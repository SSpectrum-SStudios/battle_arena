using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.AuthoritySimulation;

/// <summary>
/// Complete authority identity for exactly one active combatant on exactly one
/// match frame. Match-frame epoch is part of the identity, so frame zero after
/// a timeline reset cannot alias frame zero from the prior timeline.
/// </summary>
public readonly record struct AuthorityInputFrameIdentity
{
    public AuthorityInputFrameIdentity(
        CombatantAuthorityPredictionEpoch authorityEpoch,
        SimulationInstant frame)
    {
        if (!authorityEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityEpoch));
        }

        AuthorityEpoch = authorityEpoch;
        Frame = frame;
    }

    public CombatantAuthorityPredictionEpoch AuthorityEpoch { get; }
    public SimulationInstant Frame { get; }
    public bool IsValid => AuthorityEpoch.IsValid;
}

/// <summary>
/// Why authority supplied input/state policy instead of owner input. These are
/// application decisions, not client-command rejection reasons.
/// </summary>
public enum AuthorityInputOverrideReason : byte
{
    Eliminated = 1,
    Stunned = 2,
    Teleported = 3,
    TimelineFrozen = 4,
    GameplayPolicy = 5,
}

/// <summary>
/// Current-frame values which remain authority-safe when movement and combat
/// intent become neutral. Revisions come from the authority configuration
/// timeline, not from a stale owner command.
/// </summary>
public readonly record struct AuthorityFallbackInputBasis
{
    public AuthorityFallbackInputBasis(
        ViewOrientation safeView,
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision)
    {
        if (!movementRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(movementRevision));
        }
        if (!capabilityRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityRevision));
        }

        SafeView = safeView;
        MovementRevision = movementRevision;
        CapabilityRevision = capabilityRevision;
    }

    public ViewOrientation SafeView { get; }
    public MovementConfigurationRevision MovementRevision { get; }
    public MovementCapabilityRevision CapabilityRevision { get; }
    public bool IsValid => MovementRevision.IsValid && CapabilityRevision.IsValid;

    public static AuthorityFallbackInputBasis From(CharacterSimulationInput input)
    {
        if (!input.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        return new AuthorityFallbackInputBasis(
            input.View,
            input.MovementRevision,
            input.CapabilityRevision);
    }
}

/// <summary>
/// The one input application selected for an active combatant/frame. Arrival
/// decisions are deliberately separate: rejection, duplication, or lateness of
/// a packet can never become a second application for an already-consumed frame.
/// </summary>
public readonly record struct AuthorityInputFrameDecision
{
    private AuthorityInputFrameDecision(
        AuthorityInputFrameIdentity identity,
        AuthorityInputApplicationKind applicationKind,
        CharacterSimulationInput appliedInput,
        OwnerSimulationCommand? receivedCommand,
        uint consecutiveMissingFrames,
        AuthorityInputOverrideReason? overrideReason)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
        if (!appliedInput.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedInput));
        }

        Identity = identity;
        ApplicationKind = applicationKind;
        AppliedInput = appliedInput;
        ReceivedCommand = receivedCommand;
        ConsecutiveMissingFrames = consecutiveMissingFrames;
        OverrideReason = overrideReason;

        if (!IsValid)
        {
            throw new ArgumentException(
                "The authority input application fields do not form one valid frame decision.");
        }
    }

    public AuthorityInputFrameIdentity Identity { get; }
    public AuthorityInputApplicationKind ApplicationKind { get; }
    public CharacterSimulationInput AppliedInput { get; }
    public OwnerSimulationCommand? ReceivedCommand { get; }
    public InputSequence? AppliedInputSequence => ReceivedCommand?.Sequence;
    public uint ConsecutiveMissingFrames { get; }
    public AuthorityInputOverrideReason? OverrideReason { get; }

    public bool IsValid
    {
        get
        {
            if (!Identity.IsValid || !AppliedInput.IsValid)
            {
                return false;
            }

            return ApplicationKind switch
            {
                AuthorityInputApplicationKind.ReceivedCommand =>
                    ConsecutiveMissingFrames == 0 &&
                    OverrideReason is null &&
                    ReceivedCommand is { } command &&
                    IsMatchingReceivedCommand(Identity, command) &&
                    command.Input == AppliedInput,
                AuthorityInputApplicationKind.RepeatedContinuous =>
                    ConsecutiveMissingFrames > 0 &&
                    ReceivedCommand is null &&
                    OverrideReason is null &&
                    HasNoDiscreteReferences(AppliedInput),
                AuthorityInputApplicationKind.NeutralFallback =>
                    ConsecutiveMissingFrames > 0 &&
                    ReceivedCommand is null &&
                    OverrideReason is null &&
                    IsNeutral(AppliedInput),
                AuthorityInputApplicationKind.AuthorityOverride =>
                    ConsecutiveMissingFrames == 0 &&
                    ReceivedCommand is null &&
                    IsDefinedOverrideReason(OverrideReason) &&
                    HasNoDiscreteReferences(AppliedInput),
                _ => false,
            };
        }
    }

    public static AuthorityInputFrameDecision ApplyReceived(
        AuthorityInputFrameIdentity identity,
        OwnerSimulationCommand command)
    {
        if (!command.IsValid || !IsMatchingReceivedCommand(identity, command))
        {
            throw new ArgumentException(
                "The received command must target the exact authority combatant/frame epoch.",
                nameof(command));
        }

        return new AuthorityInputFrameDecision(
            identity,
            AuthorityInputApplicationKind.ReceivedCommand,
            command.Input,
            command,
            consecutiveMissingFrames: 0,
            overrideReason: null);
    }

    public static AuthorityInputFrameDecision ApplyAuthorityOverride(
        AuthorityInputFrameIdentity identity,
        CharacterSimulationInput overrideInput,
        AuthorityInputOverrideReason reason)
    {
        if (!IsDefinedOverrideReason(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        if (!overrideInput.IsValid || !HasNoDiscreteReferences(overrideInput))
        {
            throw new ArgumentException(
                "Authority overrides cannot invent owner transition or action references.",
                nameof(overrideInput));
        }

        return new AuthorityInputFrameDecision(
            identity,
            AuthorityInputApplicationKind.AuthorityOverride,
            overrideInput,
            receivedCommand: null,
            consecutiveMissingFrames: 0,
            reason);
    }

    internal static AuthorityInputFrameDecision ApplyRepeatedContinuous(
        AuthorityInputFrameIdentity identity,
        CharacterSimulationInput previousInput,
        AuthorityFallbackInputBasis currentBasis,
        uint consecutiveMissingFrames)
    {
        if (!previousInput.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(previousInput));
        }
        if (!currentBasis.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(currentBasis));
        }
        if (consecutiveMissingFrames == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutiveMissingFrames));
        }

        var repeated = new CharacterSimulationInput(
            previousInput.Movement,
            previousInput.View,
            previousInput.MovementHeld,
            default,
            previousInput.CombatInput,
            default,
            currentBasis.MovementRevision,
            currentBasis.CapabilityRevision);

        return new AuthorityInputFrameDecision(
            identity,
            AuthorityInputApplicationKind.RepeatedContinuous,
            repeated,
            receivedCommand: null,
            consecutiveMissingFrames,
            overrideReason: null);
    }

    internal static AuthorityInputFrameDecision ApplyNeutralFallback(
        AuthorityInputFrameIdentity identity,
        AuthorityFallbackInputBasis currentBasis,
        uint consecutiveMissingFrames)
    {
        if (!currentBasis.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(currentBasis));
        }
        if (consecutiveMissingFrames == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutiveMissingFrames));
        }

        var neutral = new CharacterSimulationInput(
            default,
            currentBasis.SafeView,
            default,
            default,
            default,
            default,
            currentBasis.MovementRevision,
            currentBasis.CapabilityRevision);

        return new AuthorityInputFrameDecision(
            identity,
            AuthorityInputApplicationKind.NeutralFallback,
            neutral,
            receivedCommand: null,
            consecutiveMissingFrames,
            overrideReason: null);
    }

    /// <remarks>
    /// The match-frame epoch is compared explicitly. <see cref="OwnerIntentScope"/>
    /// carries session, life, and owner control but deliberately not the epoch, so
    /// scope equality alone would let a command from a prior timeline be applied to
    /// the same tick number in a new one.
    /// </remarks>
    private static bool IsMatchingReceivedCommand(
        AuthorityInputFrameIdentity identity,
        OwnerSimulationCommand command) =>
        identity.IsValid &&
        command.IsValid &&
        command.TargetFrame == identity.Frame &&
        command.MatchFrameEpoch == identity.AuthorityEpoch.MatchFrameEpoch &&
        command.Identity.Scope == OwnerIntentScope.From(identity.AuthorityEpoch) &&
        command.AuthorityDiscontinuity ==
            identity.AuthorityEpoch.AuthorityDiscontinuity;

    private static bool HasNoDiscreteReferences(CharacterSimulationInput input) =>
        input.TransitionReferences.Count == 0 &&
        input.ActionReferences.Count == 0;

    private static bool IsNeutral(CharacterSimulationInput input) =>
        HasNoDiscreteReferences(input) &&
        input.Movement == default &&
        input.MovementHeld.Buttons == MovementHeldButtons.None &&
        input.CombatInput.HeldButtons == CombatHeldButtons.None;

    private static bool IsDefinedOverrideReason(AuthorityInputOverrideReason? reason) =>
        reason is { } value && IsDefinedOverrideReason(value);

    private static bool IsDefinedOverrideReason(AuthorityInputOverrideReason reason) =>
        reason is
            AuthorityInputOverrideReason.Eliminated or
            AuthorityInputOverrideReason.Stunned or
            AuthorityInputOverrideReason.Teleported or
            AuthorityInputOverrideReason.TimelineFrozen or
            AuthorityInputOverrideReason.GameplayPolicy;
}

/// <summary>
/// Bounded, immutable strategy for missing owner commands. It derives the gap
/// length from the immediately preceding committed frame, preventing callers
/// from extending stale held input by supplying a fabricated age.
/// </summary>
public readonly record struct AuthorityInputFallbackPolicy
{
    public const int DefaultMaximumRepeatedContinuousFrames = 2;
    public const int MaximumSupportedRepeatedContinuousFrames = 256;

    public AuthorityInputFallbackPolicy(int maximumRepeatedContinuousFrames)
    {
        if (maximumRepeatedContinuousFrames is < 0 or
            > MaximumSupportedRepeatedContinuousFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRepeatedContinuousFrames));
        }

        MaximumRepeatedContinuousFrames = maximumRepeatedContinuousFrames;
    }

    public int MaximumRepeatedContinuousFrames { get; }
    public bool IsValid => MaximumRepeatedContinuousFrames is >= 0 and
        <= MaximumSupportedRepeatedContinuousFrames;

    public AuthorityInputFrameDecision ResolveMissingFrame(
        AuthorityInputFrameIdentity identity,
        AuthorityInputFrameDecision? previousDecision,
        AuthorityFallbackInputBasis currentBasis)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
        if (!currentBasis.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(currentBasis));
        }
        if (!IsValid)
        {
            throw new InvalidOperationException("The fallback policy is invalid.");
        }

        if (previousDecision is not { } previous)
        {
            return AuthorityInputFrameDecision.ApplyNeutralFallback(
                identity,
                currentBasis,
                consecutiveMissingFrames: 1);
        }

        RequireImmediatePredecessor(identity, previous);
        var missingFrames = previous.ApplicationKind is
            AuthorityInputApplicationKind.RepeatedContinuous or
            AuthorityInputApplicationKind.NeutralFallback
                ? SaturatingIncrement(previous.ConsecutiveMissingFrames)
                : 1u;
        var mayRepeat = previous.ApplicationKind is
            AuthorityInputApplicationKind.ReceivedCommand or
            AuthorityInputApplicationKind.RepeatedContinuous;

        return mayRepeat && missingFrames <= MaximumRepeatedContinuousFrames
            ? AuthorityInputFrameDecision.ApplyRepeatedContinuous(
                identity,
                previous.AppliedInput,
                currentBasis,
                missingFrames)
            : AuthorityInputFrameDecision.ApplyNeutralFallback(
                identity,
                currentBasis,
                missingFrames);
    }

    private static void RequireImmediatePredecessor(
        AuthorityInputFrameIdentity current,
        AuthorityInputFrameDecision previous)
    {
        if (!previous.IsValid ||
            previous.Identity.AuthorityEpoch != current.AuthorityEpoch ||
            previous.Identity.Frame.Tick == long.MaxValue ||
            previous.Identity.Frame.Tick + 1 != current.Frame.Tick)
        {
            throw new ArgumentException(
                "Fallback may derive state only from the same combatant epoch's immediately preceding frame.",
                nameof(previous));
        }
    }

    private static uint SaturatingIncrement(uint value) =>
        value == uint.MaxValue ? value : value + 1;
}

/// <summary>
/// Per-command ingress classification. A rejected or late valid command is
/// recorded here but is never an input application. Malformed wire messages are
/// rejected earlier by the protocol validator and have no domain command.
/// </summary>
public readonly record struct AuthorityInputArrivalDecision
{
    public AuthorityInputArrivalDecision(
        CombatantAuthorityPredictionEpoch authorityEpoch,
        OwnerSimulationCommand command,
        OwnerInputArrivalDisposition disposition)
    {
        var identity = new AuthorityInputFrameIdentity(
            authorityEpoch,
            command.TargetFrame);
        if (!command.IsValid || !Matches(identity, command))
        {
            throw new ArgumentException(
                "The command does not belong to the authenticated authority epoch.",
                nameof(command));
        }
        if (!IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        FrameIdentity = identity;
        Command = command;
        Disposition = disposition;
    }

    public AuthorityInputFrameIdentity FrameIdentity { get; }
    public OwnerSimulationCommand Command { get; }
    public OwnerInputArrivalDisposition Disposition { get; }
    public bool IsTerminalWithoutApplication => Disposition is
        OwnerInputArrivalDisposition.LateCommand or
        OwnerInputArrivalDisposition.RejectedCommand;
    public bool IsValid =>
        FrameIdentity.IsValid &&
        Command.IsValid &&
        Matches(FrameIdentity, Command) &&
        IsDefined(Disposition);

    private static bool Matches(
        AuthorityInputFrameIdentity identity,
        OwnerSimulationCommand command) =>
        command.TargetFrame == identity.Frame &&
        command.MatchFrameEpoch == identity.AuthorityEpoch.MatchFrameEpoch &&
        command.Identity.Scope == OwnerIntentScope.From(identity.AuthorityEpoch) &&
        command.AuthorityDiscontinuity ==
            identity.AuthorityEpoch.AuthorityDiscontinuity;

    private static bool IsDefined(OwnerInputArrivalDisposition disposition) =>
        disposition is
            OwnerInputArrivalDisposition.NewCommandAccepted or
            OwnerInputArrivalDisposition.LateCommand or
            OwnerInputArrivalDisposition.DuplicateCommand or
            OwnerInputArrivalDisposition.RejectedCommand;
}

/// <summary>Outcome of attempting to occupy one authority combatant/frame cell.</summary>
public enum AuthorityInputFrameCommitResult : byte
{
    Committed = 1,
    ExactDuplicate = 2,
    RejectedDifferentFrame = 3,
    RejectedConflictingDecision = 4,
}

/// <summary>
/// Single-assignment cell used by the scheduler's bounded frame store. An exact
/// duplicate is idempotent; no second application can replace the committed
/// decision for the same combatant/frame.
/// </summary>
/// <remarks>
/// This is a mutable value type so a bounded scheduler ring can hold slots
/// inline with no per-frame allocation. Commit is therefore exposed only as
/// <see cref="TryCommit(ref AuthorityInputFrameDecisionSlot, AuthorityInputFrameDecision)"/>,
/// which takes the slot by reference. An instance method would silently succeed
/// against the temporary copy returned by a <c>List&lt;T&gt;</c> or
/// <c>Dictionary&lt;K,V&gt;</c> indexer and lose the commit, defeating the
/// single-assignment guarantee this type exists to provide. Passing an rvalue
/// by reference is a compile error, so that misuse cannot reach runtime; store
/// slots in an array, or use <c>CollectionsMarshal.AsSpan</c> over a list.
/// </remarks>
public struct AuthorityInputFrameDecisionSlot
{
    private readonly AuthorityInputFrameIdentity _identity;
    private AuthorityInputFrameDecision _decision;

    public AuthorityInputFrameDecisionSlot(AuthorityInputFrameIdentity identity)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }

        _identity = identity;
        _decision = default;
        HasDecision = false;
    }

    public AuthorityInputFrameIdentity Identity => _identity;
    public bool HasDecision { get; private set; }
    public AuthorityInputFrameDecision Decision => HasDecision
        ? _decision
        : throw new InvalidOperationException(
            "This authority combatant/frame does not have a committed decision.");

    public static AuthorityInputFrameCommitResult TryCommit(
        ref AuthorityInputFrameDecisionSlot slot,
        AuthorityInputFrameDecision decision)
    {
        if (!decision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }
        if (!slot._identity.IsValid)
        {
            throw new InvalidOperationException(
                "The authority input frame slot is not initialized.");
        }
        if (decision.Identity != slot._identity)
        {
            return AuthorityInputFrameCommitResult.RejectedDifferentFrame;
        }
        if (!slot.HasDecision)
        {
            slot._decision = decision;
            slot.HasDecision = true;
            return AuthorityInputFrameCommitResult.Committed;
        }

        return slot._decision == decision
            ? AuthorityInputFrameCommitResult.ExactDuplicate
            : AuthorityInputFrameCommitResult.RejectedConflictingDecision;
    }
}
