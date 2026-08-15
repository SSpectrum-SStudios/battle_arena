# Remote Movement Prediction Architecture

Status: Confirmed; implementation Phases 3-8 complete, remote Steam acceptance pending  
Last updated: August 8, 2026

## Purpose

This document defines how Battle Arena will make remote players appear responsive
without weakening the listen server's authority. It covers the shared application
interfaces, the ENet and Steam transport adapters, synchronized timing, direct
peer prediction hints, authority relays, reconciliation, and the initial quality
targets.

The design has two non-negotiable outcomes:

- Every player controls their own character through immediate local prediction.
  Their movement and camera never wait for a network round trip.
- Remote movement should appear almost perfectly smooth on stable connections at
  or below 80 milliseconds RTT. Higher latency remains playable, but more visible
  prediction error and correction is accepted.

## Locked Decisions

1. The hosting authority remains authoritative over position, collision results,
   movement-state transitions, attacks, hits, damage, lives, and world state.
2. Every client immediately simulates its own complete movement command stream.
3. All movement inputs and movement state may be distributed as untrusted
   prediction information. This includes run, sprint, facing, jump, crouch, roll,
   airborne state, ledge grab, climb, drop, and future movement capabilities.
4. Attack requests and combat outcomes are not sent through the peer prediction
   mesh. Attack button bits and combat-action state are removed from peer movement
   bundles and continue to use the authority path.
5. An owning client sends the same semantic movement bundle to the authority and
   every directly connected peer.
6. The authority validates ownership and message bounds, then relays an accepted
   canonical movement bundle to every other client. This confirmation is useful
   even when a direct copy arrived first.
7. Direct peer messages are presentation hints. They can never authorize gameplay
   outcomes or overwrite authoritative state.
8. The authority sends a compact movement frame at 60 Hz and a broader world
   snapshot at 30 Hz. Structural events remain event-driven, and a more complete
   checkpoint continues at a lower reliable cadence.
9. Prediction timing is adaptive per remote peer. It is based on synchronized
   authority time, direct-route RTT, authority-route RTT, jitter, packet loss,
   packet age, buffer underruns, and recent correction quality.
10. Prediction is bounded. When fresh information is unavailable, movement first
    becomes conservative and then stops rather than extrapolating indefinitely.
11. ENet and Steam use the same gameplay protocol and prediction services. Their
    connection establishment and route descriptors remain adapter-specific.
12. A prediction mesh is never required to start or continue a match. Missing,
    incomplete, or failed peer routes use the authority relay automatically.
13. Movement commands and authority acceptances are event-driven: each is sent
    during the simulation tick that creates or accepts it, without waiting for
    a separate state heartbeat. The 60 Hz authority frame and lower-cadence
    rollback baseline remain independent repair streams.
14. Rollback baselines contain movement simulation state only. They do not
    carry health, equipment, effects, attacks, lives, or world state.

## Authority and Prediction Planes

The network is divided into two logical planes.

### Authority plane

The authority plane is mandatory and retains the existing star topology. Every
client has one authenticated connection to the host. It carries:

- join, roster, start, reconnect, and resynchronization messages;
- authoritative movement commands from each owning client;
- authority-accepted movement confirmations;
- compact authoritative movement frames;
- actions, combat events, structural events, snapshots, and checkpoints;
- clock synchronization and network-health messages.

Losing this plane means the client has lost the match authority even when one or
more direct peer routes remain connected.

### Peer prediction plane

The peer prediction plane is a best-effort full mesh used only for remote movement
presentation. Each participant attempts to create a direct route to every other
participant. It carries:

- movement prediction bundles;
- direct-route clock probes and replies;
- prediction-route health information;
- mesh authentication and route-generation messages.

A direct route failure does not prevent the match from starting or continuing.
The affected pair falls back to authority-relayed accepted movement bundles and
authoritative movement frames.

## Identity Model

Transport connection handles must not be used as player identity.

### `TransportConnectionId`

An ephemeral identifier owned by a transport adapter. The current
`NetworkPeerId` type was renamed to `TransportConnectionId` during the first
implementation stage because it contains an ENet peer ID in one adapter and a
Steam connection handle in the other. It may change after a
reconnect and has no gameplay meaning.

### `SessionPeerId`

A stable, authority-assigned participant identity for one match session. It is
used to address prediction information and survives replacement of a transport
connection during an allowed reconnect. A session peer is associated with the
existing player and controlled-combatant identities, but those concepts remain
separate so spectators or multiple controlled entities can be supported later.

### `PeerSessionGeneration`

A monotonically increasing generation for a session peer's authenticated match
presence. It changes when that participant reconnects to the authority and
invalidates every packet and prediction route from the previous presence. The
existing authority-plane `ConnectionGeneration` is the first implementation of
this concept and will be renamed at the application boundary without reusing
its Protobuf field number.

### `PredictionRouteGeneration`

A monotonically increasing generation for one authorized peer pair. It changes
when that pair's direct route is recreated without requiring either participant
to leave the match. A valid direct packet must match both endpoints' current
peer-session generations and the pair's prediction-route generation.

### Pair route credential

The authority creates a cryptographically random 256-bit credential for each
authorized peer pair and route generation. It is scoped to the match session,
both peer IDs, both peer-session generations, and the prediction-route
generation. It is sent reliably to the two peers over their authority
connections and is never transmitted over the direct route. Instead, each peer
proves possession with HMAC-SHA-256 over the full route scope and fresh nonces.
Reconnect, route replacement,
expiration, or revocation creates a new credential. The credential proves route
authorization; it is not a player identifier.

### `LifeId`

The existing life generation remains part of movement state. A delayed movement
bundle from an earlier life cannot affect presentation after respawn.

## Transport Interfaces

The following signatures describe responsibilities. Exact method shapes may be
adjusted during implementation to cooperate with Godot's polling lifecycle, but
the boundaries must remain.

### Raw packet transport

```csharp
public interface INetworkTransport
{
    event Action<InboundTransportPacket>? PacketReceived;
    event Action<TransportConnection>? ConnectionOpened;
    event Action<TransportConnectionClosed>? ConnectionClosed;

    TransportKind Kind { get; }
    void Send(OutboundTransportPacket packet);
}
```

This remains a byte-and-connection abstraction. It knows transport connection
handles, channels, delivery requirements, and remote platform identities. It
does not know combatants, movement rules, prediction policy, or Protobuf message
meaning.

The common delivery contract should use the actual lowest common denominator:

```csharp
public enum TransportDelivery
{
    Unreliable,
    ReliableOrdered,
}
```

The former name `UnreliableOrdered` was removed during the first implementation
stage. Steam's current no-delay unreliable send does not promise the same
ordering semantics as an ENet ordered channel. Replaceable streams already carry
application sequence numbers, so the application layer—not the adapter—discards
reordered and obsolete packets.

### Connection bootstrap

```csharp
public interface IAuthorityConnectionBootstrap
{
    void StartAuthority(AuthorityListenOptions options);
    void ConnectToAuthority(AuthorityRouteDescriptor route);
    void Stop();
}
```

The launcher uses this interface to create the mandatory authority route. ENet
options contain an address and UDP port. Steam options contain a Steam identity
and virtual port. Gameplay systems never inspect either representation.

### Prediction mesh transport

```csharp
public interface IPredictionMeshTransport
{
    event Action<PredictionRouteChanged>? RouteChanged;
    event Action<InboundPredictionPacket>? PacketReceived;

    TransportKind Kind { get; }
    bool SupportsDirectRoutes { get; }
    PredictionRouteDescriptor CreateLocalRouteDescriptor();
    void ConfigureLocalPeer(SessionPeerId localPeer, PeerSessionGeneration generation);
    void AddOrUpdateRoute(AuthorizedPeerRoute route);
    void RemoveRoute(SessionPeerId peer);
    void SendControl(SessionPeerId recipient, ReadOnlyMemory<byte> payload);
    bool TrySendMovement(SessionPeerId recipient, ReadOnlyMemory<byte> payload);
    void Stop();
}
```

Control messages use reliable ordered delivery. Movement messages use
unreliable delivery with application sequencing and redundancy.

This interface owns direct route establishment and session-peer-to-connection
mapping. It accepts only authority-approved routes. It does not decide what the
prediction payload means.

`PredictionRouteDescriptor` is an opaque, versioned adapter payload. The session
protocol can carry it, but only the matching ENet or Steam adapter interprets it.
This avoids `if Steam` and `if ENet` branches in prediction services.

### Session peer directory

```csharp
public interface ISessionPeerDirectory
{
    SessionPeerId LocalPeer { get; }
    SessionPeerId AuthorityPeer { get; }
    IReadOnlyCollection<SessionPeer> Peers { get; }

    bool TryGetPeer(SessionPeerId id, out SessionPeer peer);
    bool TryResolveAuthorityConnection(
        TransportConnectionId connection,
        out SessionPeerId peer);
}
```

The directory is populated only from authority-authenticated roster messages.
It is the single mapping between transport connections and stable session peers.

### Mesh coordinator

```csharp
public interface IPredictionMeshCoordinator
{
    PredictionMeshStatus Status { get; }
    void ApplyRoster(PeerRoster roster);
    void ApplyRouteAuthorization(AuthorizedPeerRoute route);
    void RemovePeer(SessionPeerId peer);
}
```

The coordinator applies the deterministic connection rule, manages retries, and
reports fallback status. To avoid duplicate simultaneous connections, the lower
`SessionPeerId` initially opens the route for each pair. The other endpoint
listens and authenticates it.

The authority issues a short-lived, pair-specific route credential. Steam remote
identity must also match the advertised Steam identity. ENet peers prove the
pair credential during the mesh handshake. A route is not accepted merely
because its payload claims a valid player ID.

The authority plane and non-authority prediction clients expose different
application interfaces. `IAuthorityPredictionRouteBroker` owns roster,
authorization, credential, generation, and revocation decisions.
`IClientPredictionMesh` owns direct route establishment, retry, health, and
fallback. `IAuthorityMovementDistribution` validates owner bundles and emits
canonical accepted bundles, while `IClientMovementPublisher` publishes one
semantic movement bundle to the authority and every available peer. The raw
mesh transport remains symmetric because every non-authority client may both
listen and initiate according to the deterministic peer-ID rule.

### Authority-mediated route lifecycle

1. Each authenticated non-authority client sends a versioned, opaque prediction
   route advertisement to the authority.
2. The authority publishes a reliable roster containing stable peer identity,
   player/combatant identity, and peer-session generation.
3. For each non-authority peer pair, the authority creates a route generation,
   expiration tick, and random 256-bit pair credential.
4. Each endpoint receives a reliable authorization containing the other peer's
   descriptor, shared route generation, expiry, and credential. The initiator
   is derived as the lower `SessionPeerId`; it is not a separately trusted flag.
5. The lower `SessionPeerId` opens the route. The other endpoint listens.
6. The initiator sends `PredictionMeshHello` with a fresh 256-bit nonce. The raw
   credential never crosses the direct route.
7. The responder returns a fresh nonce and `PredictionMeshChallenge` containing
   a responder-role HMAC bound to the session, both peer IDs and generations,
   route generation, and both nonces. The initiator verifies it and returns an
   initiator-role HMAC in `PredictionMeshProof`. Only after verifying that proof
   does the responder authenticate the route and send `PredictionMeshAccepted`
   with a responder key-confirmation HMAC bound to both nonces and the proof
   packet sequence. The initiator authenticates only after verifying that
   confirmation. Every connection attempt has its own monotonically increasing
   attempt ID so delayed success or failure callbacks cannot affect a retry.
   A captured hello or proof cannot answer a fresh responder challenge. A
   rejection or timeout
   leaves the pair on authority relay and schedules a bounded retry.
8. Direct movement, accepted authority commands, and authority frames feed the
   same remote timeline; route establishment never changes gameplay ownership.
9. Reconnect, departure, expiry, or route replacement reliably revokes the old
   generation before a new authorization is issued.
10. Match start and continuation never wait for any step in this lifecycle.

The authority host does not create a second prediction-plane connection to its
clients. It already receives their movement over the mandatory authority route
after one network leg and sends its own movement directly on that route. The
full mesh therefore covers non-authority client pairs.

## ENet Adapter Plan

The current ENet server/client connection remains the authority plane.

The prediction plane uses a separate ENet mesh adapter so a mesh failure cannot
disturb the authority connection. Godot's ENet API supports mesh mode, but each
peer connection must be created and added manually. The adapter will:

1. bind a prediction UDP port;
2. advertise a versioned IP/port route descriptor through the authority;
3. establish the pair selected by the deterministic initiator rule;
4. authenticate the session, peer IDs, generations, and pair credential;
5. add the connected route to the prediction mesh;
6. expose it as a `SessionPeerId` route.

ENet direct prediction is guaranteed initially for localhost and ordinary LAN
testing. Raw ENet Internet NAT traversal is not a launch requirement. An ENet
Internet session that cannot establish a direct pair continues using authority
relay. The Steam adapter is the intended Internet path.

Multiple local processes must bind different prediction ports. The local test
launcher will allocate or request those ports automatically and display them in
diagnostics.

## Steam Adapter Plan

The current Steam Networking Sockets connection to the lobby owner remains the
authority plane.

The prediction plane uses a separate Steam P2P virtual port and poll group. Every
lobby participant owns a prediction listen socket. The adapter will:

1. initialize Steam relay network access during Steam startup;
2. advertise the authenticated Steam identity and prediction virtual port;
3. connect to the Steam identity selected by the deterministic pair rule;
4. accept only lobby members with a matching authority-approved route;
5. bind Steam connection handles to stable `SessionPeerId` values;
6. use direct routing or Steam Datagram Relay as selected by Steam;
7. report connection quality without exposing Steam APIs to gameplay code.

Authority and prediction connections use separate virtual ports so their
callbacks and policies cannot be confused. Both may share one adapter object or
runtime callback owner internally, provided they expose the segregated
interfaces above.

## Movement Protocol

### Complete movement command

A movement command is a complete tick-scoped input state, not a raw key event.
It includes:

- input sequence and client simulation tick;
- estimated authority tick;
- movement axes and analog magnitudes;
- view yaw and pitch;
- held, pressed, and released movement bits;
- movement-state revision and capability revision;
- source session peer, combatant, and life identity.

Pressed and released bits trigger edges only once. Held state allows recovery
from packet loss without leaving a remote player stuck in a direction.

A command is created on a physics tick and transmitted immediately. Arbitrary
render-frame or operating-system input events are not assigned speculative
simulation ticks. At the initial 60 Hz simulation rate, input sampling therefore
adds at most one 16.67 ms physics tick. A future higher simulation rate may send
changed commands immediately while batching unchanged history into the network
cadence.

### Predicted movement state

Movement bundles may contain a compact owner rollback baseline. The initial
policy sends it at 10 Hz and immediately on route connection, life change,
movement-mode transition, or movement capability/profile revision. The
baseline contains only movement simulation state:

- position and velocity;
- body facing and view orientation;
- locomotion, posture, jump, and movement-capability modes;
- state/action instance IDs and start ticks;
- roll, jump, mantle, grounded, and timer state required for replay;
- last command sequence included in the state;
- movement-attribute and capability revisions.

Timer values in this untrusted baseline are relative to `client_tick`, never
bare absolute ticks from an ambiguous clock domain. The baseline carries mode
elapsed ticks, ticks since grounded, buffered-jump ticks remaining, and roll
cooldown ticks remaining. Optional traversal state has a stable instance ID,
typed phase, ledge geometry, phase elapsed ticks, and phase duration.

The baseline is the post-simulation state of `last_included_input_sequence`.
That command must appear in the same redundant bundle, and its client tick,
estimated authority tick, profile revision, and capability revision must match
the baseline exactly.

It explicitly excludes health, items, effects, attack state, damage, lives, and
world objects. Commands remain the normal prediction source; the baseline is a
lower-cadence initialization and history-repair source.

This state is an untrusted initialization or repair hint. A receiver should replay
commands through the shared movement simulator when sufficient history exists.
It may use the predicted checkpoint when history is missing, but always replaces
it when newer authority state arrives.

Combat action state is intentionally absent. If an authoritative attack applies
movement such as a lunge, peers learn the action from the authority event and the
resulting displacement from authoritative movement frames.

### Shared semantic bundle

```csharp
public interface IMovementBundleFactory
{
    MovementPredictionBundle Create(
        SessionPeerId source,
        MovementCommand currentCommand,
        MovementRuntimeState predictedState,
        MovementCommandHistory history);
}
```

The owning client creates one immutable semantic bundle. That bundle is encoded
for the authority plane and for every prediction peer. The host validates fields
and relays a canonical accepted form; it should not blindly forward client bytes.

The protocol gains these message families:

- `MovementPredictionBundle`: owner input and predicted state sent to the host
  and direct peers;
- `AuthorityAcceptedMovementBundle`: host-confirmed canonical commands relayed
  immediately after validation;
- `AuthorityMovementFrameBatch`: compact authoritative state sent at 60 Hz;
- `AuthoritySnapshot`: broader repair state sent at 30 Hz;
- `ClockSyncProbe` and `ClockSyncReply`;
- `PeerRouteAdvertisement`, `PeerRosterUpdate`, and
  `PredictionRouteAuthorization`;
- `PredictionMeshHello` and `PredictionMeshAccepted`.

Authority-plane mesh management uses the existing `PacketEnvelope` and reliable
connection channel. Direct peer traffic uses a smaller dedicated
`PredictionPacketEnvelope` containing protocol and session identity, source and
destination session peers, peer-session generation, prediction-route
generation, packet sequence, client tick, and estimated authority tick. Control
payloads use reliable delivery; movement and direct timing payloads use the
prediction transport's declared policy.

The initial movement bundle repeats the current command and previous two
commands. Its rollback baseline is optional according to the 10 Hz and
transition policy above. Attack and block bits are invalid in direct movement
commands and continue exclusively on the authority action path.

The direct and authority copies are reconciled by source peer, connection
generation, life ID, and input sequence. Comparison is field-based; raw Protobuf
bytes are not treated as a canonical hash.

### Resolved movement configuration

Items and effects remain fully data-driven, but prediction does not re-run the
item/effect compiler on remote peers. The authority reliably publishes a
tick-effective `AuthorityMovementConfigurationUpdate` containing the exact
typed values consumed by the shared movement simulator:

- ground speed, acceleration, braking, turning, slope, snap, and step values;
- air acceleration, speed caps, high-speed control, and turning values;
- jump launch, momentum contribution, gravity curve, fall cap, coyote time,
  and input-buffer values;
- crouch/roll thresholds, distances, durations, steering, cooldown, and
  collision dimensions;
- explicit movement capabilities and bounded jump/air-roll counts.

The configuration is keyed by combatant, life, profile revision, capability
revision, and effective authority tick. Both revisions also travel on accepted
commands, compact movement frames, and broader snapshots. A receiver missing a
referenced authority configuration uses the authority presentation path; it
does not guess from an untrusted direct hint. Adding a fundamentally new
movement behavior requires an explicit typed field and simulator support, while
ordinary item tuning requires no protocol change.

### Datagram budgets and evolution

Unreliable direct movement and timing packets have a 1,200-byte encoded ceiling
to stay below conservative Internet MTUs. Reliable direct handshake/control
packets have a separate 4 KiB ceiling. Field-level bounds are validated after
decoding, and the maximum legitimate movement bundle has an encoded-size test.

During the alpha, all builds require the exact same protocol version. Protobuf
field additions remain wire-safe, but mixed-version admission is deliberately
rejected until a compatibility policy and join-rejection response are designed.

## Replication Services

### Owning-client prediction

```csharp
public interface ILocalMovementPrediction
{
    MovementPredictionBundle SimulateAndRecord(MovementCommand command);
    LocalReconciliationResult Reconcile(AuthoritativeMovementState state);
}
```

The local owner always simulates on the next local physics tick. It has no remote
presentation buffer. Reconciliation restores the authoritative state, removes
acknowledged commands, and replays the rest. Camera orientation remains locally
owned. Small body corrections are consumed by a visual offset; gameplay state is
corrected immediately.

No remote latency measurement is allowed to slow the owning player's input,
camera, jump, roll, or movement animation start.

### Movement distribution

```csharp
public interface IMovementDistributionService
{
    void Publish(MovementPredictionBundle bundle);
    event Action<ReceivedMovementBundle>? DirectBundleReceived;
    event Action<AcceptedMovementBundle>? AcceptedBundleReceived;
    event Action<AuthoritativeMovementFrame>? AuthorityFrameReceived;
}
```

On a client, `Publish` sends to the authority and attempts every active direct
route. On the authority, the service validates the owning connection, canonicalizes
accepted commands, queues them for simulation, and broadcasts the accepted form.

### Remote prediction timeline

```csharp
public interface IRemoteMovementTimeline
{
    void ObserveDirect(MovementPredictionBundle bundle);
    void ObserveAccepted(AcceptedMovementBundle bundle);
    void ObserveAuthority(AuthoritativeMovementFrame frame);
    RemotePresentationSample Sample(AuthorityTimeEstimate time);
}
```

There is one timeline per remote combatant and life. It stores direct commands,
accepted commands, predicted checkpoints, authoritative frames, provenance, and
reconciliation results. Repeated copies are deduplicated. An authority-accepted
copy confirms or replaces a direct copy. An authoritative state corrects both.
Semantic fields are compared after decoding and normalization; Protobuf bytes
are never compared as canonical data. One mismatch is corrected and recorded.
After three mismatches, the timeline stops sampling all direct state hints for
that peer and uses authority-accepted commands and frames only. Further direct
packets remain bounded reconciliation evidence for diagnostics; they cannot
drive presentation or gameplay. Authority-relay gameplay continues normally.

### Remote simulation

```csharp
public interface IRemoteMovementPredictor
{
    PredictedRemoteState Replay(
        AuthoritativeMovementState baseline,
        IReadOnlyList<MovementCommand> commands,
        SimulationTick targetTick);
}
```

It uses the same fixed-tick movement rules as local prediction and authority
simulation. Godot world queries are exposed through a prediction-only adapter.
Remote prediction must not invoke combat, consume items, create authoritative
world objects, or emit authoritative collision outcomes.

### Reconciliation policy

```csharp
public interface IRemoteReconciliationPolicy
{
    ReconciliationDecision Decide(
        PredictedRemoteState predicted,
        AuthoritativeMovementState authoritative,
        NetworkPathEstimate network);
}
```

The decision may retain the current visual path, blend a positional/rotational
offset, replay from a corrected baseline, or snap for discontinuities. Teleport,
respawn, life change, invalid state, and large divergence always use explicit
snap/transition policies rather than ordinary smoothing.

## Clock and Adaptive Timing Interfaces

### Monotonic time source

```csharp
public interface INetworkTimeSource
{
    long GetTimestampMicroseconds();
}
```

Tests use a controllable implementation. Godot uses its monotonic tick clock.
Wall-clock time is never used for simulation ordering.

### Authority clock synchronizer

```csharp
public interface IAuthorityClockSynchronizer
{
    void Observe(AuthorityClockExchange exchange);
    AuthorityTimeEstimate Estimate(long localTimestampMicroseconds);
}
```

Synchronization is continuous, not a one-time lobby operation. Probe/reply
exchanges estimate offset, RTT, and drift. Authority snapshots and movement
frames provide additional tick observations. Abrupt offset changes are rejected
or converged gradually unless a new match baseline explicitly resets the clock.

### Per-path network estimator

```csharp
public interface INetworkPathEstimator
{
    void Observe(NetworkPathSample sample);
    NetworkPathEstimate Current { get; }
}
```

Each client maintains separate estimates for its authority route and every
direct peer route. Estimates include smoothed RTT, one-way-delay approximation,
jitter, loss, reordering, silence, and sample confidence.

The initial direct-route probe cadence is four probes per second. Rollback
baselines begin at 10 Hz, movement packets carry three-command redundancy, and
route retry begins quickly before applying bounded backoff. These are runtime
tuning policies rather than wire-protocol constants.

### Adaptive prediction timing policy

```csharp
public interface IPredictionTimingPolicy
{
    PredictionTimingDecision Evaluate(PredictionTimingContext context);
}
```

The decision supplies:

- target authority render tick;
- presentation delay in ticks;
- maximum permitted prediction horizon;
- conservative extrapolation threshold;
- freeze threshold;
- time-scale adjustment used to grow or shrink the buffer smoothly.

The initial bounded range is:

- zero to approximately 150 ms of normal prediction;
- conservative extrapolation from approximately 150 to 250 ms;
- no continued input acceleration after approximately 250 ms without usable
  movement information.

These thresholds are tuning defaults, not protocol constants. Stable low-jitter
connections below 80 ms RTT should normally use zero to two presentation ticks
and predict only the remaining age of the newest command. The controller raises
the buffer faster during instability and lowers it slowly after sustained
stability.

## Collision Boundary

The owning player has a predicted collision body because immediate local movement
requires it. A remote player has two distinct representations:

- an authority-confirmed collision proxy used by local prediction and other
  gameplay-adjacent client queries;
- a non-authoritative predicted visual root driven by the remote timeline.

Direct peer hints must not move a collision proxy that can redirect the owning
player. Otherwise a malicious peer could change another client's local movement
by broadcasting false positions. A prediction-only Godot query body may collide
with static level geometry to improve remote visual prediction, but it must not
participate in gameplay collision layers or authoritative queries.

This boundary may create a small visible overlap before authority correction in
player-to-player collisions. The alternative—letting an untrusted visual hint
push the local predicted body—is not recommended.

This policy is confirmed: remote peer prediction affects visuals only. The
owning player's predicted body collides with authority-confirmed remote proxies.

## Quality Targets and Diagnostics

### Owning player

- Input begins simulation no later than the next local physics tick.
- Camera movement is applied every render frame and is never reconciled backward.
- Network jitter never inserts an interpolation buffer into local control.
- Small authority corrections do not move the camera or visibly snap the model.
- Host and client controls, animation starts, and movement policies remain equal.

### Remote player at stable RTT at or below 80 ms

- A direct movement transition appears after approximately one-way peer delay,
  not after a peer-to-authority-to-peer round trip.
- Motion remains continuous without routine snaps.
- Ordinary authoritative corrections are visually blended.
- No fixed 100 ms interpolation delay is added.
- Buffer underruns and conservative extrapolation are exceptional rather than
  normal.

Diagnostics must expose per remote peer:

- direct and authority RTT, jitter, loss, and route state;
- synchronized authority-tick offset and confidence;
- direct, accepted, and authoritative packet ages;
- current presentation delay and prediction horizon;
- buffer underruns and late-command counts;
- correction distance, angle, frequency, and chosen correction policy;
- direct-versus-authority command mismatches;
- fallback from direct prediction to authority relay.

The diagnostic data is also the foundation for later statistical cheat detection,
but the first party-game version records anomalies without automatically punishing
players.

## Composition and SOLID Boundaries

`NetworkArena` currently owns transport routing, protocol validation, clock
measurement, authority simulation, local prediction, remote interpolation,
combat replication, and presentation. The new work must split these concerns.

The intended composition root creates:

- authority connection and prediction mesh adapters;
- protocol codec and validators;
- session peer directory and mesh coordinator;
- clock synchronizer and per-path estimators;
- movement distribution service;
- local prediction controller;
- one remote movement timeline per remote combatant;
- authority movement publisher or client movement consumer;
- diagnostics collector;
- Godot avatar, collision, and visual adapters.

Pure timing, sequencing, deduplication, adaptive-policy, and timeline logic lives
in `BattleArena.Multiplayer`. Shared deterministic movement rules remain in
`BattleArena.Core`. Godot and Steam APIs remain in the Godot adapter project.
Dependencies point inward through interfaces and are manually composed in the
launcher for now.

## Implementation Sequence

The remaining work is executed through critic-gated delivery phases:

- **Phase 3:** pure application services for authority route brokering, peer
  directories, client coordination, credential rotation, challenge-response
  handshake state, movement distribution, and configuration timelines.
- **Phase 4 (implemented):** separate low-level ENet direct prediction
  transport, versioned numeric-IP/port descriptors, localhost/LAN integration,
  mutual-proof handshake driving, and forced direct-route failure fallback.
- **Phase 5 (implemented; remote validation pending):** separate Steam direct
  prediction listen socket, virtual port, and poll group behind the same
  application contracts, with authority-connection Steam identity and lobby
  membership checks. A two-account remote Steam playtest is still required.
- **Phase 6 (implemented):** one semantic movement bundle is now published to
  the authority and every authenticated direct route. Remote timelines consume
  direct commands immediately for visual prediction, replace or confirm them
  with authority-accepted commands, and prune both against authoritative
  movement frames. Direct evidence cannot change a combatant life, collision,
  combat, or gameplay state. The three-process gate requires live direct bundle
  delivery in addition to route authentication and fallback recovery.
- **Phase 7 (implemented):** authenticated direct routes exchange four
  timestamp probes per second and maintain per-peer RTT, jitter, loss,
  reordering, route-health, failure, packet-age, and arrival-sequence data.
  The client overlay exposes route/fallback state plus direct confirmations,
  mismatches, duplicates, and late commands. Deterministic tests cover stable
  0/40/80 ms paths, bounded 120/200 ms behavior, loss, duplication, reordering,
  authority repair, and repeated malicious mismatches. Anomalies are surfaced
  for testers and future statistical detection. Three mismatches automatically
  disable that peer's direct presentation hints without punishing or
  disconnecting the player; authority-relay gameplay continues.
- **Phase 8 (implemented; remote playtest pending):** direct revision metadata
  now survives the final timeline adapter, each combatant's adaptive timing uses
  its own authenticated direct-path measurement with authority-path fallback,
  and repeated semantic disagreement actively removes direct hints from replay.
  The automated parity gate verifies both authority-only match entry after an
  optional prediction startup failure and live direct bundles/fallback/RTT. The
  credential-free Steam readiness gate produces and validates a release Windows
  PE, Steam runtime, AppID `3820160`, DepotID `3820161`, resolved content root,
  depot exclusions, and a recursive secret-name scan.
  The remaining external acceptance step is a visible two-account Steam test on
  separate machines; it cannot be truthfully replaced by localhost automation.

1. Add characterization tests for the current authority/client flow and
   three-instance client-to-client latency.
2. Introduce stable session-peer identity, rename the ephemeral connection ID,
   and correct the common unreliable delivery contract.
3. Extract packet routing and replication responsibilities from `NetworkArena`.
4. Add 60 Hz compact authority movement frames while retaining 30 Hz snapshots.
5. Add continuous authority-clock synchronization, per-path measurement, and
   diagnostic overlays.
6. Implement the adaptive timing policy and remote timeline using authority
   movement frames only. This establishes a measurable fallback baseline.
7. Add canonical accepted-movement relay from the authority and use it for remote
   prediction.
8. Add the separate ENet prediction mesh for localhost and LAN, including
   three-process tests and forced-route-failure fallback.
9. Add the Steam prediction mesh using lobby identities, P2P sockets, and Steam
   relay support.
10. Feed direct bundles into the same remote timeline and reconcile direct,
    accepted, and authoritative copies.
11. Add latency, jitter, loss, duplication, reordering, and malicious-mismatch
    simulations at 0, 40, 80, 120, and 200 ms RTT.
12. Run the multiplayer parity gate and visible three-instance tests after every
    relevant stage, then publish a Steam alpha for geographically distributed
    testing.

## Implementation Progress

Completed through August 8, 2026:

- stable session-peer identity, peer-session generation, and ephemeral transport
  connection identity;
- common unreliable delivery semantics with application sequencing;
- protocol version 8, including authority-plane route management, a dedicated
  direct prediction envelope, movement prediction bundles, movement-only
  rollback baselines, direct timing probes, and explicit estimated authority
  ticks;
- authority-published, tick-effective resolved movement configuration with
  typed ground, air, jump, crouch/roll, and capability groups;
- rollback baselines with relative timers and strict correlation to an included
  command, while redundant command history may span revisions;
- direct packets and revocations bound to both endpoint peer-session generations;
- mutual HMAC-SHA-256 nonce proofs that never transmit the pair credential over
  the direct route;
- separate 1,200-byte unreliable and 4 KiB control packet budgets;
- target-life-safe damage events and explicit attack lifecycle/phase fields;
- Phase 3 authority route broker, stable peer directory, client route
  coordinator, four-message mutual-proof handshake state machine, canonical
  movement distribution, and tick-effective configuration timeline;
- compact `AuthorityMovementFrameBatch` sent at 60 Hz;
- four-timestamp continuous authority clock synchronization;
- per-stream RTT, arrival jitter, loss, and reordering estimates;
- adaptive one-to-six-tick remote presentation policy with gradual recovery;
- local-owner reconciliation from the 60 Hz movement stream while the 30 Hz
  snapshot remains broader metadata and repair state;
- headless smoke verification that the client receives the movement stream and
  successfully synchronizes authority time;
- canonical `AuthorityAcceptedMovementBatch` relay containing the authenticated
  session peer, peer-session generation, combatant, life, applied authority tick,
  and exact movement command used by the authority;
- three-command per-combatant relay redundancy with application-level
  deduplication and life-boundary rejection of delayed commands;
- focused `AuthorityMovementRelayBuffer` and generic `RemoteMovementTimeline`
  services in the pure multiplayer layer;
- remote replay through the shared fixed-tick movement rules, limited to 150 ms
  and frozen after 250 ms without newer authoritative state;
- visual-only remote prediction: the rendered character may advance while its
  collision body remains on the newest authority-confirmed pose;
- movement-only local reconciliation separated from attack advancement so
  replay cannot execute an action state machine twice.
- an extracted, transport-independent `AuthorityMovementInputBuffer` with a
  bounded pending set, newest-intent compaction, recent one-shot edge
  preservation, and separately authorized attack edges;
- a three-process headless regression that deliberately accumulates Combatant
  3 input while Combatant 2 remains active, then verifies authority recovery,
  owner responsiveness, and observer presentation without FIFO delay.

Still pending:

- two-account remote validation of the implemented Steam P2P prediction mesh;

## Confirmed Route and Fallback Policies

1. Remote peer prediction affects visuals only and never moves a collision proxy
   that can influence the owning player's predicted movement.
2. A failed or incomplete prediction mesh never blocks match start. Authority
   relay is the automatic fallback and remains the only mandatory connection.
3. ENet exists primarily for localhost and LAN development testing. Steam is the
   supported Internet and NAT-traversing path. The ENet path may be removed in a
   future release without changing prediction or gameplay services.
