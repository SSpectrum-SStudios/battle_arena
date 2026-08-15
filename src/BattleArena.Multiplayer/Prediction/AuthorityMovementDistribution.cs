using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Replication;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed class AuthorityMovementDistribution : IAuthorityMovementDistribution
{
    public AuthorityAcceptedMovementCommand AcceptAppliedCommand(
        SessionPeer authenticatedSource,
        ulong expectedLifeId,
        MovementPredictionBundle bundle,
        ulong appliedInputSequence,
        ulong appliedAuthorityTick)
    {
        ArgumentNullException.ThrowIfNull(authenticatedSource);
        ArgumentNullException.ThrowIfNull(bundle);
        var validation = InboundMessageValidator.ValidateMovementPredictionBundle(bundle);
        if (!validation.IsValid || authenticatedSource.IsAuthority || expectedLifeId == 0 ||
            bundle.SourceCombatantId != authenticatedSource.CombatantId ||
            bundle.SourceLifeId != expectedLifeId || appliedAuthorityTick == 0)
        {
            throw new ArgumentException(
                validation.Violation?.Message ?? "Movement bundle ownership or life identity is invalid.",
                nameof(bundle));
        }

        var input = bundle.Commands.SingleOrDefault(
            command => command.InputSequence == appliedInputSequence) ??
            throw new ArgumentException("Applied command is not present in the owner bundle.", nameof(appliedInputSequence));
        return CreateAccepted(
            authenticatedSource,
            expectedLifeId,
            RevisionedMovementCommand.FromProtocol(input),
            appliedAuthorityTick);
    }

    public AuthorityAcceptedMovementCommand AcceptAppliedCommand(
        SessionPeer authenticatedSource,
        ulong expectedLifeId,
        RevisionedMovementCommand appliedCommand,
        ulong appliedAuthorityTick)
    {
        ArgumentNullException.ThrowIfNull(authenticatedSource);
        ArgumentNullException.ThrowIfNull(appliedCommand);
        if (authenticatedSource.IsAuthority || expectedLifeId == 0 ||
            appliedCommand.Command.Sequence == 0 || appliedAuthorityTick == 0)
        {
            throw new ArgumentException(
                "Applied command ownership, life, sequence, or authority tick is invalid.",
                nameof(appliedCommand));
        }

        return CreateAccepted(
            authenticatedSource,
            expectedLifeId,
            appliedCommand,
            appliedAuthorityTick);
    }

    private static AuthorityAcceptedMovementCommand CreateAccepted(
        SessionPeer authenticatedSource,
        ulong expectedLifeId,
        RevisionedMovementCommand appliedCommand,
        ulong appliedAuthorityTick)
    {
        var input = MovementCommandProtocolMapper.ToProtocol(appliedCommand.Command);
        input.MovementProfileRevision = appliedCommand.MovementProfileRevision;
        input.MovementCapabilityRevision = appliedCommand.MovementCapabilityRevision;
        return new AuthorityAcceptedMovementCommand
        {
            SourceSessionPeerId = authenticatedSource.Id.Value,
            PeerSessionGeneration = authenticatedSource.PeerSessionGeneration.Value,
            CombatantId = authenticatedSource.CombatantId,
            LifeId = expectedLifeId,
            AppliedAuthorityTick = appliedAuthorityTick,
            Input = input,
            AppliedMovementProfileRevision = input.MovementProfileRevision,
            AppliedMovementCapabilityRevision = input.MovementCapabilityRevision,
        };
    }
}
