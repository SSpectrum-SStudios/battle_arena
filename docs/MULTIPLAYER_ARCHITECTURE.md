# Battle Arena — Multiplayer Architecture Decisions

Status: Discussion / living document
Last updated: July 14, 2026

## Purpose

This document records the multiplayer architecture decisions for the playable combat slice. Multiplayer must preserve the rules and boundaries established in `BattleArena.Core`: transports carry commands and state, while authoritative game rules remain independent of Godot, ENet, and Steam.

## Guiding Constraints

- The multiplayer model must support approximately eight players, while the first test targets two players.
- The host owns the canonical match simulation.
- Clients send player intent rather than authoritative outcomes.
- Godot owns physics, collision, and spatial queries through adapters around the core application layer.
- Combat outcomes are resolved through the existing authoritative combat facade.
- ENet and Steam must be replaceable transport adapters, not sources of game rules.
- Runtime entity IDs are assigned by the authority and replicated to clients.
- Network messages require explicit versions and must not depend on Godot node paths as identity.

## Decision 1: Authority Topology

Status: Confirmed

The initial game uses a **listen-server** topology. The hosting player participates in the match while the host process owns the authoritative simulation.

The host is authoritative over:

- Player spawning and runtime identity.
- Movement, physics, and collision results.
- Attack validation and hit detection.
- Damage, healing, effects, cooldowns, and item activations.
- Health, lives, elimination, and respawning.
- Round and match progression.

Remote clients send commands that describe intent, such as movement input, view direction, jumping, attacking, and activating an item. They do not tell the host that an attack hit, that damage occurred, or that an enemy died.

The first implementation will not support host migration. If the host disconnects, the match ends and the remaining players return to the lobby flow. The authority boundary should not assume that the host has a locally controlled player, allowing a dedicated server or host migration to be explored later without rewriting the game rules.

## Planned Delivery Sequence

1. Define transport-independent command, snapshot, and event contracts.
2. Add local Host and Join controls backed by Godot ENet.
3. Run two local game processes against one authoritative simulation.
4. Replicate movement and view state.
5. Replicate attacks, effects, health, elimination, and respawning.
6. Test across two computers.
7. Implement Steam lobbies and transport behind the same application boundary.
8. Create the Steam export and upload pipeline.

## Decision 2: Movement Replication

Status: Confirmed

Remote clients use **client-side prediction with server reconciliation**. A client immediately simulates its locally controlled character from its own inputs while transmitting an ordered input stream to the authoritative host. The host processes that same input stream and periodically returns authoritative state together with the latest processed input sequence.

When authoritative state arrives, the owning client:

1. Restores the acknowledged authoritative state.
2. Removes inputs the host has already processed.
3. Replays its remaining unacknowledged inputs.
4. Smooths small visual corrections while immediately correcting material simulation errors.

Remote characters are not predicted from guessed input. Clients render them by interpolating between authoritative snapshots received from the host.

The host's simulation always wins. Prediction grants responsiveness, not authority. Clients may immediately show safe presentation such as local movement or an attack animation, but they cannot declare hits, damage, effects, deaths, or other combat outcomes.

Prediction is limited initially to the locally controlled character. The game will not attempt full-world rollback. Because Godot physics can diverge slightly across machines and platforms, reconciliation is considered normal operation rather than an exceptional failure.

## Decision 3: Network Timing and Delivery

Status: Confirmed

The first multiplayer slice will run authoritative physics at 60 ticks per second. The architecture must not assume that 60 is permanent: a future target of approximately 144 authoritative physics ticks per second will be evaluated for improved input response and fast-action collision behavior.

Rendering remains independent of authoritative physics and networking. It may run at the display refresh rate or uncapped. Physics interpolation improves visual continuity between simulation ticks, but does not replace the responsiveness gained from a higher simulation rate.

The simulation rate is fixed for a particular match and chosen by the authoritative build or host configuration, not independently by each client. Clients must know the authoritative rate so prediction and reconciliation use the same fixed time step. Network packet and snapshot frequencies remain independent and do not need to increase to match a future physics rate.

Authored durations must not embed an assumed 60 Hz conversion. Content is converted deterministically into integer simulation ticks for the active match rate, with an explicit rounding policy. Runtime schedules continue to use integer ticks.

The initial network timing values are:

- Clients produce one numbered input frame per 60 Hz physics tick.
- Clients transmit input packets 60 times per second.
- The host transmits authoritative snapshots 30 times per second.
- Remote-player presentation begins with an interpolation buffer of approximately 100 milliseconds.
- A future 144 Hz simulation may batch two or three input frames into each 60 Hz network packet rather than increasing packet frequency to 144 Hz.

These values are configuration owned by the multiplayer application layer rather than constants embedded in combat or movement rules.

Movement inputs and replaceable snapshots use unreliable ordered delivery with sequence numbers and redundant recent input where appropriate. Critical structural messages, including joining, spawning, equipment changes, and round transitions, use reliable ordered delivery on separate channels.

Attacks and item activations are numbered actions. Clients resend an unacknowledged action as needed, and the host processes each action identity at most once. Prediction may begin local presentation immediately, but only the host confirms its gameplay outcome.

Multiplayer diagnostics will measure round-trip time, packet loss, prediction correction distance, correction frequency, and interpolation-buffer underruns. These measurements will guide later tuning.

## Decision 4: Replicated State and Events

Status: Confirmed

Replication uses a hybrid of event messages, frequent compact snapshots, and periodic authoritative checkpoints.

The word **reliable** has two distinct meanings that must not be conflated:

- **Reliable delivery** is a transport guarantee that messages eventually arrive and are processed in order.
- **Reliable cadence** means the authority schedules a message at a known frequency, allowing clients to detect missing sequence numbers or excessive silence even though individual packets may be discardable.

The periodic checkpoint uses reliable cadence, not necessarily reliable delivery. Each checkpoint carries a monotonically increasing sequence number, the authoritative simulation tick, and replicated-state revisions. A client can measure missed checkpoints, packet loss, jitter, and time since the last authority message. A newer checkpoint supersedes an older checkpoint.

The initial replication streams are:

- **Input stream:** client intent sent to the host with input and action identities.
- **Snapshot stream:** compact authoritative movement and vital state sent 30 times per second using unreliable ordered delivery.
- **Event stream:** state changes and presentation facts sent as they occur, with delivery policy chosen by importance.
- **Checkpoint stream:** a more complete authoritative state sent at a known interval with sequence and revision information.
- **Resynchronization stream:** a reliably delivered baseline sent on initial join or after a client detects that its replicated revisions disagree with the host.

Structural events such as spawn, despawn, equipment changes, effect creation/removal, respawn, and round transitions use reliable ordered delivery on suitable channels. Replaceable movement state and high-frequency presentation do not wait for obsolete messages.

Clients do not rerun authoritative combat resolution from replicated events. Events describe accepted outcomes for presentation, while snapshots and checkpoints repair observable state if an event is missed.

The checkpoint contains all state relevant to restoring the replicated match view, but references stable content definitions by ID instead of repeatedly transmitting immutable item definitions and assets.

## Decision 5: Connection Health and Recovery Thresholds

Status: Confirmed in principle; reconnect grace period and disconnected-combatant behavior remain open

The initial checkpoint cadence is approximately once per second. Missing individual checkpoints is expected network behavior and is recorded silently. Missing multiple checkpoints can trigger a checkpoint request or resynchronization check, but does not by itself prove that the connection is lost.

Every valid authenticated message counts as liveness traffic. A client must not warn or disconnect merely because checkpoint packets are absent while movement snapshots, events, acknowledgements, or other valid host traffic continue to arrive.

Connection health is evaluated from aggregate traffic, packet loss, jitter, and time since the last valid authority message. Sustained loss should exceed what prediction and interpolation can conceal before the player sees a warning. The initial target is to show a connection-interrupted or reconnecting indicator after approximately three seconds without any valid authority traffic.

Heartbeat and checkpoint messages detect silence and initiate recovery; they do not themselves recreate a dropped ENet or Steam connection. The transport adapter attempts reconnection, and the application performs a reconnect handshake using the existing match session and player identity. After successful authentication, the host sends a fresh authoritative baseline and the client resumes from host state rather than preserving unconfirmed prediction.

The host reserves the player's match identity for a grace period while reconnection is attempted. Missing traffic must not create a second player or reset equipment, lives, cooldowns, or effects. Host disconnection remains different: because the initial version has no host migration, losing the authoritative host ends the match.

## Decision 6: Disconnected Combatant Policy

Status: Confirmed

After a short input timeout, the authority stops applying the disconnected player's last command and substitutes neutral input. This prevents a character from continuing to run, turn, attack, or hold an activation merely because no newer input arrived.

The combatant remains in the authoritative world throughout the reconnect grace period. Physics, damage, healing, active effects, cooldowns, world interactions, lives, elimination, and respawning continue normally. The combatant remains vulnerable so intentionally disconnecting cannot grant protection or remove the player from an unfavorable interaction.

The host reserves the player's existing identity and match slot for an initial grace period of 30 seconds. A reconnect restores control of the combatant's current authoritative state. If the combatant died or exhausted its lives while the client was absent, the returning player receives that result rather than a restored pre-disconnect state.

The first implementation will not provide AI control for disconnected players. The final behavior after the grace period will integrate with match and round departure rules when those rules are implemented.

## Decision 7: Attack Lag Compensation

Status: Confirmed for the vertical slice; tuning requires latency playtests

The host uses bounded historical validation for attacks whose immutable policy permits lag compensation.

The host retains a compact history of authoritative combat hit-proxy transforms for approximately 200–250 milliseconds. An attack command carries its client simulation tick and sequence. The host maps that tick onto the authoritative timeline, clamps the permitted rewind to an initial maximum of approximately 200 milliseconds, and validates the attack against the corresponding historical hit proxies.

This does not rewind the entire Godot physics world. Static-world validation and compact historical combat shapes are coordinated by Godot adapters. The core application receives a validated target set and continues to own action, damage, and effect rules.

The host still validates action identity, ordering, player ownership, life identity, cooldown, action state, equipped source, and range. A delayed command cannot hit a combatant's previous life after respawn. Accepted damage and effects begin at the current authoritative tick; health and active-effect simulation are never rewound.

The initial action policies are:

- Melee and instantaneous targeted attacks may use bounded historical validation.
- Host-simulated projectiles do not rewind targets when the projectile later collides.
- Persistent areas, mines, and environmental volumes use current authoritative simulation.
- Self-targeted actions require no spatial rewind.

The rewind window is a tuning value, not a promised permanent constant. Artificial latency and packet-loss tests will compare attacker feedback, defender feedback, correction frequency, and suspicious timestamp behavior before finalizing it.

## Decision 8: Network Protocol Boundary

Status: Confirmed

ENet and Steam carry explicit versioned C# protocol messages through a transport-independent multiplayer application boundary. Godot RPC may be used internally to move serialized payloads, but RPC method names and Godot node paths do not define gameplay operations or network identity.

Initial message families include:

- `ClientInputBatch`
- `ClientActionRequest`
- `AuthoritySnapshot`
- `AuthorityEventBatch`
- `AuthorityCheckpoint`
- `ReconnectRequest`
- `StateBaseline`

Messages share an envelope containing the protocol version, message type, match/session identity, sequence information, sender or peer context, and relevant simulation tick. Concrete serialization remains behind a codec interface so contracts, validation, and transport do not depend on one byte encoding.

Clients send intent, never outcomes. There is no client message equivalent to `ApplyDamage`, `AddEffect`, `KillPlayer`, or `SetAuthoritativePosition`. Received messages are converted into validated application commands before reaching gameplay services.

Using C# contracts improves type safety, validation consistency, automated testing, protocol recording, and maintainability. The implementation language is not itself a security mechanism.

## Security and a Possible Ranked Future

The listen server is trusted as the authority for the initial party game. Remote clients cannot authoritatively declare hits or other outcomes, but a malicious host can modify its own authoritative process. Transport encryption does not prevent the host from cheating.

A trustworthy ranked mode would require dedicated authoritative servers or another independently trusted authority. The current boundaries should permit the same match authority and core rules to run without a locally controlled host player, but ranked hosting, matchmaking, anti-cheat, signed content, and operational infrastructure are intentionally outside the initial scope.

Even in the party-game slice, the host validates peer-to-player ownership, session identity, protocol version, message shape, sequence ordering, duplicate actions, input ranges, tick windows, cooldowns, and action legality. Repeated invalid traffic can be rate-limited or disconnected and recorded for diagnostics.

## Decision 9: Initial Protocol Encoding

Status: Confirmed

The initial wire format uses schema-first Protocol Buffers with generated C# message types. Protobuf provides compact binary payloads, explicit schemas, generated encoding and decoding, cross-language support, and a practical schema-evolution path.

Generated Protobuf types are network DTOs and remain outside `BattleArena.Core`. The multiplayer application boundary maps decoded DTOs into validated immutable application commands and maps authoritative state or facts into outbound DTOs.

Protocol rules include:

- Never reuse removed field numbers or names; reserve them in the schema.
- Prefer additive, wire-safe changes and reject incompatible protocol versions explicitly.
- Bound packet size before parsing and validate repeated-field counts afterward.
- Validate enum values, numeric ranges, identities, tick windows, ownership, and action legality after decoding.
- Avoid arbitrary dynamic payloads such as client-supplied `Any` messages.
- Treat successful parsing only as syntactic validity, never authorization.
- Do not use raw Protobuf bytes as a canonical deterministic hash because Protobuf serialization order is not guaranteed to be canonical.

Human-readable diagnostic output and packet recording accompany the binary wire format. The codec remains behind an interface so a measured hot path can receive a specialized representation later without changing transports or game rules.
