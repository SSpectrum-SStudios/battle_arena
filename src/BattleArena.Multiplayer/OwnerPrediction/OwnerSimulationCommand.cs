using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// One immutable, allocation-free owner input sample for one explicit match
/// frame. Multiplayer owns delivery/control identity while <see cref="Input"/>
/// is the inward-facing Core simulation value.
/// </summary>
/// <remarks>
/// <see cref="MatchFrameEpoch"/> and <see cref="TargetFrame"/> together are the
/// command's frame identity. A bare tick is ambiguous: after an authority
/// timeline reset the numbering restarts, so tick 400 of epoch 1 and tick 400 of
/// epoch 2 are different moments. The epoch travels on the wire in
/// <c>OwnerPredictionScopeDraft.match_frame_epoch</c> at batch level; carrying it
/// on the domain command keeps the pair inseparable once a batch is unpacked.
/// It is deliberately not part of <see cref="OwnerIntentScope"/>, which is a
/// journal-lifetime scope (session + life + control) rather than a time domain.
/// </remarks>
public readonly record struct OwnerSimulationCommand
{
    public OwnerSimulationCommand(
        OwnerInputIdentity identity,
        AuthorityDiscontinuityId authorityDiscontinuity,
        MatchFrameEpochId matchFrameEpoch,
        SimulationInstant targetFrame,
        CharacterSimulationInput input)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }

        if (!authorityDiscontinuity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityDiscontinuity));
        }

        if (!matchFrameEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(matchFrameEpoch));
        }

        if (!input.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        Identity = identity;
        AuthorityDiscontinuity = authorityDiscontinuity;
        MatchFrameEpoch = matchFrameEpoch;
        TargetFrame = targetFrame;
        Input = input;
    }

    public OwnerInputIdentity Identity { get; }
    public LifeEpoch Life => Identity.Scope.Life;
    public OwnerControlEpoch ControlEpoch => Identity.Scope.OwnerControl;
    public InputSequence Sequence => Identity.Sequence;
    public AuthorityDiscontinuityId AuthorityDiscontinuity { get; }
    public MatchFrameEpochId MatchFrameEpoch { get; }
    public SimulationInstant TargetFrame { get; }
    public CharacterSimulationInput Input { get; }
    public bool IsValid =>
        Identity.IsValid &&
        AuthorityDiscontinuity.IsValid &&
        MatchFrameEpoch.IsValid &&
        Input.IsValid;
}
