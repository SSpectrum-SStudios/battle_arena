namespace BattleArena.Multiplayer.Replication;

/// <summary>
/// Orders attack presentation observations that arrive on independent event
/// and snapshot streams. It prevents older repair state from rewinding a newer
/// event and protects an unconfirmed local prediction from pre-action state.
/// </summary>
public sealed class ReplicatedAttackPresentationTracker
{
    private bool _hasAuthorityObservation;

    public ulong LastAuthorityTick { get; private set; }

    public AttackPresentationIdentity? ConfirmedIdentity { get; private set; }

    public AttackPresentationDecision Observe(
        ulong authorityTick,
        AttackPresentationIdentity? observedIdentity,
        AttackPresentationIdentity? currentPresentation,
        bool hasUnconfirmedLocalPrediction)
    {
        if (authorityTick == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityTick));
        }

        if (_hasAuthorityObservation && authorityTick < LastAuthorityTick)
        {
            return AttackPresentationDecision.Ignore;
        }

        if (observedIdentity is null)
        {
            if (hasUnconfirmedLocalPrediction && currentPresentation is not null)
            {
                // Do not advance the cross-stream watermark. The confirming
                // action event may have been emitted before this snapshot was
                // delivered on its separate channel.
                return AttackPresentationDecision.PreserveLocalPrediction;
            }

            if (_hasAuthorityObservation &&
                authorityTick == LastAuthorityTick &&
                ConfirmedIdentity is not null)
            {
                return AttackPresentationDecision.Ignore;
            }

            Commit(authorityTick, null);
            return AttackPresentationDecision.Clear;
        }

        if (_hasAuthorityObservation &&
            authorityTick == LastAuthorityTick &&
            ConfirmedIdentity is { } confirmed &&
            confirmed != observedIdentity.Value)
        {
            return AttackPresentationDecision.Ignore;
        }

        var restart = currentPresentation != observedIdentity;
        Commit(authorityTick, observedIdentity);
        return new AttackPresentationDecision(
            AttackPresentationAction.Apply,
            restart);
    }

    public void Reset()
    {
        _hasAuthorityObservation = false;
        LastAuthorityTick = 0;
        ConfirmedIdentity = null;
    }

    private void Commit(ulong authorityTick, AttackPresentationIdentity? identity)
    {
        _hasAuthorityObservation = true;
        LastAuthorityTick = authorityTick;
        ConfirmedIdentity = identity;
    }
}
