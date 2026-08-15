using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public sealed class PredictionRouteEpochGate
{
    private readonly ulong _localSessionPeerId;
    private readonly ulong _remoteSessionPeerId;
    private uint _localPeerSessionGeneration;
    private uint _remotePeerSessionGeneration;
    private uint _predictionRouteGeneration;

    public PredictionRouteEpochGate(ulong localSessionPeerId, ulong remoteSessionPeerId)
    {
        if (localSessionPeerId == 0 || remoteSessionPeerId == 0 ||
            localSessionPeerId == remoteSessionPeerId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localSessionPeerId),
                "A route gate requires two different session peers.");
        }

        _localSessionPeerId = localSessionPeerId;
        _remoteSessionPeerId = remoteSessionPeerId;
    }

    public bool IsAuthorized { get; private set; }

    public bool TryApplyAuthorization(
        PredictionRouteAuthorization authorization,
        ulong currentAuthorityTick)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.LocalSessionPeerId != _localSessionPeerId ||
            authorization.RemoteSessionPeerId != _remoteSessionPeerId ||
            authorization.LocalPeerSessionGeneration == 0 ||
            authorization.RemotePeerSessionGeneration == 0 ||
            authorization.PredictionRouteGeneration == 0 ||
            authorization.ExpiresAuthorityTick <= currentAuthorityTick ||
            authorization.LocalPeerSessionGeneration < _localPeerSessionGeneration ||
            authorization.RemotePeerSessionGeneration < _remotePeerSessionGeneration)
        {
            return false;
        }

        var presenceChanged =
            authorization.LocalPeerSessionGeneration > _localPeerSessionGeneration ||
            authorization.RemotePeerSessionGeneration > _remotePeerSessionGeneration;
        if (!presenceChanged &&
            authorization.PredictionRouteGeneration <= _predictionRouteGeneration)
        {
            return false;
        }

        _localPeerSessionGeneration = authorization.LocalPeerSessionGeneration;
        _remotePeerSessionGeneration = authorization.RemotePeerSessionGeneration;
        _predictionRouteGeneration = authorization.PredictionRouteGeneration;
        IsAuthorized = true;
        return true;
    }

    public bool TryApplyRevocation(PredictionRouteRevoked revocation)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        if (!IsAuthorized ||
            revocation.LocalSessionPeerId != _localSessionPeerId ||
            revocation.RemoteSessionPeerId != _remoteSessionPeerId ||
            revocation.LocalPeerSessionGeneration != _localPeerSessionGeneration ||
            revocation.RemotePeerSessionGeneration != _remotePeerSessionGeneration ||
            revocation.PredictionRouteGeneration != _predictionRouteGeneration)
        {
            return false;
        }

        IsAuthorized = false;
        return true;
    }
}
