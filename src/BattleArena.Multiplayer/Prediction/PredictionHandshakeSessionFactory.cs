using BattleArena.Multiplayer.Protocol;

namespace BattleArena.Multiplayer.Prediction;

public sealed class PredictionHandshakeSessionFactory : IPredictionHandshakeSessionFactory
{
    private readonly IPredictionHandshakeAuthenticator _authenticator;
    private readonly IPredictionRouteCredentialGenerator _credentialGenerator;
    private readonly PredictionInboundMessageValidator _validator;

    public PredictionHandshakeSessionFactory(
        IPredictionHandshakeAuthenticator authenticator,
        IPredictionRouteCredentialGenerator credentialGenerator,
        PredictionInboundMessageValidator validator)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _credentialGenerator = credentialGenerator ??
            throw new ArgumentNullException(nameof(credentialGenerator));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public IPredictionHandshakeSession Create(AuthorizedPredictionRoute route) =>
        new PredictionHandshakeSession(
            route,
            _authenticator,
            _credentialGenerator,
            _validator);
}
