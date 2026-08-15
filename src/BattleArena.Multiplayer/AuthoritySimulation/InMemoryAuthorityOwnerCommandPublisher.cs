using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.AuthoritySimulation;

/// <summary>Why a published owner command did or did not reach the scheduler.</summary>
public enum OwnerCommandPublishOutcome : byte
{
    /// <summary>Stored against its exact target frame.</summary>
    Admitted = 1,

    /// <summary>A byte-identical resend of a command already stored for that frame.</summary>
    Duplicate = 2,

    /// <summary>Its target frame was already consumed. It will never be simulated.</summary>
    Late = 3,

    /// <summary>Refused by the scheduler: foreign scope, past the horizon, conflicting, or skewed.</summary>
    Rejected = 4,

    /// <summary>
    /// The command did not belong to this publisher's authenticated owner. It is
    /// refused before the scheduler sees it, exactly as the wire path refuses a
    /// message whose scope does not match its authenticated session.
    /// </summary>
    ScopeMismatch = 5,
}

public readonly record struct OwnerCommandPublishResult
{
    internal OwnerCommandPublishResult(
        OwnerCommandPublishOutcome outcome,
        AuthorityInputAdmission admission)
    {
        Outcome = outcome;
        Admission = admission;
    }

    public OwnerCommandPublishOutcome Outcome { get; }

    /// <summary>The scheduler's own classification, absent for a scope mismatch.</summary>
    public AuthorityInputAdmission Admission { get; }
    public bool WasAdmitted => Outcome == OwnerCommandPublishOutcome.Admitted;
}

/// <summary>
/// Delivers one owner's commands to the authority scheduler.
/// </summary>
/// <remarks>
/// The remote implementation carries commands over the network; the listen-host
/// implementation hands them over in memory. Both exist so the host is a client
/// of its own authority rather than a special case that writes authority state
/// directly.
/// </remarks>
public interface IAuthorityOwnerCommandPublisher
{
    OwnerIntentScope Scope { get; }
    long PublishedCommandCount { get; }
    OwnerCommandPublishResult Publish(in OwnerSimulationCommand command);
}

/// <summary>
/// The listen-server host's publisher. It removes the network, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The point of this type is what it refuses to do. The host does not write
/// authority movement state, does not skip the scheduler, and does not get a
/// richer command than a remote client would: every command is put through the
/// same <see cref="IAuthorityOwnerCommandProjector"/> the wire path uses and
/// rebuilt from that projection, so any field the authority projection drops is
/// dropped for the host too. A host command therefore reaches
/// <see cref="AuthorityOwnerInputScheduler.TryAdmit"/> carrying exactly the
/// information a remote command carries.
/// </para>
/// <para>
/// The match-frame epoch is supplied by the publisher's authenticated scope
/// rather than by the caller, mirroring the wire, where the epoch travels once
/// in the batch scope rather than on each command. A host cannot name an epoch
/// it was not issued.
/// </para>
/// <para>
/// No artificial impairment is added. The host will naturally suffer fewer
/// corrections because its commands genuinely do not traverse a network; that is
/// a physical fact, not a rules advantage. Every scheduling, fallback, journal,
/// and validation rule is the shared one.
/// </para>
/// </remarks>
public sealed class InMemoryAuthorityOwnerCommandPublisher : IAuthorityOwnerCommandPublisher
{
    private readonly AuthorityOwnerInputScheduler _scheduler;
    private readonly IAuthorityOwnerCommandProjector _projector;
    private readonly CombatantAuthorityPredictionEpoch _authorityEpoch;
    private readonly OwnerIntentScope _scope;

    private long _publishedCommandCount;
    private long _admittedCommandCount;

    public InMemoryAuthorityOwnerCommandPublisher(AuthorityOwnerInputScheduler scheduler)
        : this(scheduler, new AuthorityOwnerCommandProjector())
    {
    }

    public InMemoryAuthorityOwnerCommandPublisher(
        AuthorityOwnerInputScheduler scheduler,
        IAuthorityOwnerCommandProjector projector)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(projector);

        _scheduler = scheduler;
        _projector = projector;
        _authorityEpoch = scheduler.AuthorityEpoch;
        _scope = OwnerIntentScope.From(_authorityEpoch);
    }

    public OwnerIntentScope Scope => _scope;
    public long PublishedCommandCount => _publishedCommandCount;
    public long AdmittedCommandCount => _admittedCommandCount;

    public OwnerCommandPublishResult Publish(in OwnerSimulationCommand command)
    {
        _publishedCommandCount++;

        // Authenticate before projecting, as the wire path authenticates before
        // decoding a body into a domain command. Every component of the scope is
        // checked the same way, including the match-frame epoch: the wire
        // validator rejects the whole batch on any scope mismatch, so coercing
        // one component here while rejecting another would give the host a rule
        // no remote client gets.
        if (!command.IsValid ||
            command.Identity.Scope != _scope ||
            command.MatchFrameEpoch != _authorityEpoch.MatchFrameEpoch ||
            command.AuthorityDiscontinuity != _authorityEpoch.AuthorityDiscontinuity)
        {
            return new OwnerCommandPublishResult(
                OwnerCommandPublishOutcome.ScopeMismatch,
                default);
        }

        var projection = _projector.Project(command);
        if (!projection.IsValid)
        {
            return new OwnerCommandPublishResult(
                OwnerCommandPublishOutcome.ScopeMismatch,
                default);
        }

        // Rebuilt from the projection, not forwarded whole. This is what keeps
        // the host from silently gaining input a remote client cannot send. The
        // epoch comes from the authenticated scope, mirroring the wire, where it
        // travels once in the batch scope rather than on each command — and it
        // has already been checked to match, so this is a restatement rather
        // than a substitution.
        var delivered = new OwnerSimulationCommand(
            projection.Identity,
            projection.AuthorityDiscontinuity,
            _authorityEpoch.MatchFrameEpoch,
            projection.TargetFrame,
            projection.Input);

        var admission = _scheduler.TryAdmit(delivered);
        if (admission.WasStored)
        {
            _admittedCommandCount++;
        }

        return new OwnerCommandPublishResult(OutcomeFor(admission), admission);
    }

    private static OwnerCommandPublishOutcome OutcomeFor(AuthorityInputAdmission admission) =>
        admission.Disposition switch
        {
            OwnerInputArrivalDisposition.NewCommandAccepted => OwnerCommandPublishOutcome.Admitted,
            OwnerInputArrivalDisposition.DuplicateCommand => OwnerCommandPublishOutcome.Duplicate,
            OwnerInputArrivalDisposition.LateCommand => OwnerCommandPublishOutcome.Late,
            _ => OwnerCommandPublishOutcome.Rejected,
        };
}
