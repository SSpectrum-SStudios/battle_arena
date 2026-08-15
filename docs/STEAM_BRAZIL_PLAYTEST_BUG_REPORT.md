# Steam Brazil Playtest Bug Report

Status: Open; investigation and design discussion pending  
Playtest date: August 8, 2026  
Environment: Steam alpha build, two players, long-distance connection involving
Brazil

## Purpose

This report records problems observed during the first long-distance Steam
playtest. It intentionally documents player-visible behavior separately from
possible technical causes. No diagnosis or implementation decision should be
treated as confirmed until the relevant networking, simulation, animation, and
Steam invitation paths have been inspected.

## Summary

The Steam session was playable, but the remote client's locally controlled
character did not feel sufficiently local or stable. Movement corrections and
attack presentation were especially disruptive. Melee hit outcomes also seemed
to favor whichever participant was the client. Steam invitations only joined
correctly when the recipient accepted the invitation while the game was closed.
The playtest also exposed an unwanted coupling between camera direction,
character facing, and attack direction.

## BA-NET-001: Client-Owned Movement Is Jerky and Unstable

Severity: Critical for external playtesting  
Status: Open

### Observed behavior

- The client experiences visible jumps, snaps, and smaller positional
  corrections while controlling their own character.
- Jumping is particularly unstable.
- The problem is severe enough that ordinary client movement feels bad over the
  tested long-distance connection.
- The authority remains authoritative as intended, but the owning client's
  character does not consistently appear to follow the player's immediate
  intent.

### Desired behavior

The locally owned character should feel as though it is running locally, even
when latency is substantial. Authority corrections should normally be hidden or
blended without making controls feel delayed. Interactions with authoritative
world state may visibly correct when necessary, but routine locomotion and
jumping should remain smooth and responsive.

### Investigation notes

Do not assume that raising snapshot frequency alone will solve this. Inspect
local prediction, reconciliation thresholds, replayed movement configuration,
grounded-state disagreement, jump-edge handling, clock alignment, correction
presentation, and whether visual and collision transforms are being corrected
at the same layer.

### Acceptance criteria

- Local run, sprint, turning, and air control remain visually continuous under
  representative Brazil latency.
- A predicted jump does not restart, snap vertically, or visibly fight the
  authority during ordinary play.
- Small corrections are blended without changing the player's intended input.
- Large invalid predictions still converge safely to authority state.

## BA-ANIM-002: Client Attack Animation Is Not Fluid

Severity: High  
Status: Open

### Observed behavior

- Attacks performed by the client look substantially less fluid than attacks
  performed by the authority.
- The animation appears discontinuous or interrupted rather than playing as one
  coherent attack.
- The result makes the client's attack timing and combo state difficult to read.

### Desired behavior

The owning client should begin attack presentation immediately. Later authority
confirmation should not restart, double-advance, rewind, or otherwise visibly
damage an already-correct prediction. Authority and client attack presentation
should be functionally indistinguishable during normal play.

### Investigation notes

Inspect predicted attack startup, reliable action authorization, replicated
attack events, combo-state reconciliation, animation-tree transitions, duplicate
animation triggers, and whether movement reconciliation is resetting attack
presentation.

### Acceptance criteria

- Client attack startup is immediate.
- Each combo step starts its animation once.
- Authority confirmation does not restart or interrupt the predicted animation.
- The same attack policy produces comparable presentation for authority and
  client.

## BA-COMBAT-003: Melee Hit Outcomes Appear to Favor the Client

Severity: Critical for combat fairness  
Status: Open

### Observed behavior

- The client could hit the authority more easily than the authority could hit
  the client.
- When the two players exchanged authority/client roles, the apparent advantage
  followed the client role.
- This suggests a role-dependent asymmetry rather than a particular player's
  timing or skill.

### Desired behavior

Neither authority nor client role should provide a systematic melee advantage.
Lag compensation should make reasonable client attacks possible without making
the client unusually difficult for the authority to hit.

### Investigation notes

Measure rather than infer. Compare the rewind applied to client attack requests
with the authority's local attack query, the timestamps used for both cases,
target pose history, attacker pose selection, one-way latency estimation, and
the maximum rewind cap. Check whether only the target is rewound, whether the
attacker is evaluated at a different time, and whether authority attacks receive
an equivalent fairness policy.

### Acceptance criteria

- Equivalent spatial and timing situations produce equivalent hit decisions for
  authority and client.
- Rewind amount is visible in diagnostics and remains within the configured cap.
- High latency does not permit obviously stale or out-of-range client hits.
- Authority attacks do not evaluate against a uniquely disadvantaged target
  pose.

## BA-STEAM-004: Accepting an Invite While Already Running Does Not Join

Severity: High for usability  
Status: Open

### Observed behavior

- The recipient had the game open when the host sent a Steam invitation.
- Accepting that invitation did not place the running game into the host's lobby.
- Closing the game and accepting a newly sent invitation launched the game and
  joined successfully.

### Desired behavior

Accepting a Steam invitation should join the requested lobby whether the game is
closed or already running. If joining requires leaving another session or
returning to the launcher, the game should perform that transition or present a
clear confirmation instead of silently doing nothing.

### Investigation notes

Inspect both startup `+connect_lobby` handling and the Steam callback used when a
running game receives a rich-presence or lobby join request. Confirm callback
registration lifetime, callback pumping, lobby ID parsing, launcher state
transitions, and cleanup of any current connection before joining.

### Acceptance criteria

- Accepting an invitation while the game is closed launches and joins.
- Accepting an invitation while sitting in the launcher joins without restart.
- Accepting an invitation while in another session produces a defined transition
  or confirmation.
- Join failures display a useful error instead of being ignored.

## BA-DESIGN-005: Camera Rotation Should Not Automatically Redirect Attacks

Severity: High for combat feel  
Status: Open design correction

### Observed behavior

- Rotating the camera turns the character and causes attacks to follow the new
  camera direction.
- During the playtest, both players preferred the character to retain the
  direction they were already facing and attack in front of the character.

### Desired behavior

Free camera orbit should not automatically change character facing or melee
attack direction. An attack should use the character's established facing at
attack commitment. Any action that intentionally changes facing should be
explicitly defined by the movement or weapon policy.

### Questions to resolve before implementation

- Which inputs establish facing while idle, running, strafing, airborne, and
  attacking?
- Should movement direction rotate the character immediately, gradually, or only
  beyond an angular threshold?
- At which attack phase is facing captured, and can the early cancellable phase
  still redirect it?
- Can individual weapons author turn assistance or a limited attack-facing arc?

### Acceptance criteria

- Orbiting the camera alone does not rotate the character.
- Starting an attack does not snap the character to camera yaw.
- The attack hit volume and animation use the same committed facing.
- Authority and client resolve the same attack direction.

## Evidence to Capture During the Next Reproduction

- Which participant is authority and which is client.
- Approximate authority-to-client and peer-to-peer RTT, jitter, loss, and route
  state from the debug overlay.
- Video from both players for the same movement, jump, and attack exchange.
- Logs from both machines covering connection through shutdown.
- Whether the prediction route is authenticated or using authority fallback.
- Rewind requested and actual rewind applied for each disputed hit.
- Whether each problem changes after exchanging authority/client roles.

## Recommended Discussion Order

1. Client-owned movement prediction and reconciliation.
2. Client attack prediction and animation ownership.
3. Symmetric melee lag-compensation policy.
4. Character-facing and attack-direction rules.
5. Steam invitation handling while already running.

The first three are tightly related and should be designed together before code
changes. Invitation handling is independent and can be addressed separately
after its callback path is confirmed.
