# Multiplayer Movement and Combat Playtest Plan

Status: Implemented; awaiting hands-on feel test and Steam publication  
Target: Steam friends test build

## Goal

The main launch flow must allow a host and invited Steam friends to enter one
persistent test arena where every player can:

- use the accepted run, sprint, jump, crouch, and roll controller;
- see every other player's Knight and movement presentation;
- perform the Fighter's basic sword attacks and grounded combo;
- receive authoritative hits and damage;
- see health, elimination, and automatic respawn;
- continue moving and fighting long enough to collect useful playtest notes.

This milestone intentionally excludes round scoring, lives, item selection,
equipment replacement, and build progression. Those systems should be added
after movement and combat are credible under real network conditions.

## Confirmed Scope and Working Defaults

- This is a movement-and-fighting test, not a test of the round or item loop.
- Use the existing movement playtest arena and add several eligible random
  respawn locations around it.
- Player bodies collide with one another.
- Players must join the lobby before the host starts. Late joining is excluded
  from this slice.
- The only equipped item is the starter sword.
- The starter sword deals Physical damage only and grants no block or parry.
- Base health is 100.
- Initial combo damage is 20, 20, and 30.
- The shared crouched/airborne attack initially deals 20 damage.
- The proposed attack timings and lunge distances are starting values that
  require playtesting.
- The starter sword uses a weapon-owned attack-input policy:
  - tapping performs combo step one;
  - continuing to hold through step one's continuation point queues step two;
  - a separate press in step one's normal continuation window may also queue
    step two;
  - holding alone never queues step three;
  - step three requires a release and fresh press during an initial
    220-millisecond finisher window near the end of step two;
  - only one continuation may be queued.
- Attacks preserve incoming momentum. Authored per-phase deceleration may
  gradually reduce it, but attack movement does not clamp velocity to a
  percentage of normal run speed.
- Steps one and two begin with 60% normal movement acceleration/steering
  authority, step three begins with 40%, and all steps begin with an additional
  3 m/s² deceleration. These are tunable authored values.
- Attack facing and turning policy belong to the weapon/action definition.
- Jump and Roll may cancel startup and recovery but not commitment. A
  cancellation defeats a queued continuation. Rolls remain committed.
- The starter sword has zero knockback and no gameplay hit stun. Hit
  presentation must not interrupt an otherwise legal attack.
- Simultaneous legal hits both resolve and may cause mutual elimination.
- The test arena uses unlimited automatic respawns after two seconds, at full
  health, with a new life generation and no spawn protection.
- On death, the local camera remains at the death location for the first second,
  then relocates to the selected respawn area before the character reappears.
- Minimum feedback includes names, health bars, target flash, attacker hit
  confirmation, floating damage, death presentation, respawn countdown, and
  placeholder swing/impact sounds.
- The first network test remains at 60 authoritative simulation ticks per
  second, 60 input transmissions per second, and 30 snapshots per second.
- Actions and accepted combat events are sent immediately on their simulation
  tick; they never wait for the next periodic snapshot or checkpoint.
- Eligible melee actions use host-measured latency compensation. The authority
  grants approximately half the smoothed RTT plus a small jitter margin, capped
  to at most 200 milliseconds of historical hit-proxy rewind.
- Current input defaults are Ctrl/L3 Sprint, Shift/B Crouch/Roll, Space/A Jump,
  and left mouse/right trigger Attack.
- The expected Steam beta branch is `alpha`, to be verified before publishing.

## Implementation Status

### Implemented and Automatically Verified

- ENet and Steam transport adapters behind `INetworkTransport`.
- Join authentication, authority-assigned player/combatant identity, lobby
  hosting, invitations, and match-start messages.
- Versioned Protocol Buffer envelopes, validation, sequencing, and separate
  transport channels.
- Client prediction, reconciliation, and remote interpolation concepts in the
  current network arena.
- Plain C# movement rules for acceleration, braking, turning, jumping, falling,
  crouching, and rolling.
- Data-driven Fighter movement values.
- Godot step traversal, collision-profile changes, and Knight presentation.
- Core combatants, action executions, one-hit-per-target policy, typed damage,
  health, elimination, periodic effects, and respawn.
- Three Knight ground-attack animations and one shared simple attack
  presentation binding.
- Windows Steam export, upload, and beta-branch publishing scripts.
- One shared Godot movement driver now serves the offline player, authority,
  and owning-client prediction. The obsolete network motor has been removed.
- Network avatars use the Knight presentation and complete movement state,
  including held/pressed/released input edges and replayable jump/roll state.
- The starter sword is an immutable JSON-authored attack set with a
  weapon-owned input policy and tick-authored phases, combo windows, movement,
  facing, damage, and presentation IDs.
- Client attacks predict immediately, travel as separately authenticated
  numbered action requests, and are repaired by action events and snapshots.
- The host performs one-hit-per-action melee queries and submits legal
  same-tick hits as a simultaneous combat batch.
- Damage, health, elimination, smooth respawn-camera travel, countdown, random
  respawn, and new-life identity are authoritative and replicated.
- The host measures RTT/jitter through snapshot acknowledgements and caps
  historical target-pose rewind at 200 milliseconds. F10 disables compensation
  for comparison testing.
- A headless two-process ENet combat smoke test has proven join, match start,
  movement packets, action authorization, attack resolution, 20 Physical
  damage, snapshots, and clean shutdown.

### Awaiting Hands-On Acceptance

- Judge movement and camera parity in two visible instances.
- Judge the authored attack timing, lunges, momentum loss, reach, and arc.
- Confirm the placeholder Knight attack/death presentation is understandable.
- Exercise death and respawn several times from both host and client.
- Test real Steam transport with two accounts, including a high-latency friend.
- Tune lag compensation from recorded RTT, jitter, requested age, and rewind
  results.
- Add or source placeholder swing and impact audio after the mechanical timing
  is accepted.

Launch two visible localhost instances from PowerShell with:

```powershell
.\tools\testing\launch_local_playtest.ps1
```

The script uses the custom Steam-enabled Godot .NET editor by default, starts
the host first, then joins a client and starts the test automatically. Escape
releases the mouse, clicking a game window recaptures it, F9 compares buffered
and raw remote presentation, and F10 enables/disables host lag compensation.

## Delivery Order

Multiplayer movement is implemented first because authoritative attack
validation depends on correct positions, postures, action ticks, and prediction
state.

### Milestone 1: Shared Character Simulation Driver

- Extract the accepted offline movement orchestration into a reusable Godot
  character simulation driver.
- Keep plain C# movement simulators independent of Godot.
- Make both offline and network avatars use the same profile, transition rules,
  collision handling, and step traversal.
- Keep local input sampling, authority command consumption, and replay as
  adapters feeding the same tick method.
- Retire `NetworkMovementMotor` after parity is proven.

Acceptance:

- One deterministic command stream produces equivalent movement state in the
  offline, authority, and prediction paths, subject only to reported Godot
  collision facts.
- Existing movement probes and unit tests continue passing.

### Milestone 2: Complete Movement Protocol State

Extend input frames with held, pressed, and released bits for:

- Jump
- Sprint
- Crouch/Roll
- Attack intent or action correlation, depending on the confirmed action
  transport decision

Extend authoritative combatant snapshots with the state required for correct
reconciliation and remote presentation:

- position and full velocity;
- facing and view direction;
- locomotion, posture, jump, and action modes;
- movement/action instance ID and mode start tick;
- last grounded tick and buffered-input deadlines;
- jump-cut state;
- roll direction, entry speed, boost distance, duration, cooldown deadline, and
  queued landing roll;
- last processed input sequence;
- movement definition/capability revision;
- health, life generation, and elimination state.

Protocol values use explicit enums and integer ticks. Validation rejects unknown
button bits, enum values, impossible ranges, oversized batches, and invalid
action timing.

Acceptance:

- Jumping, landing, crouching, short/held rolls, and landing rolls reconcile
  without losing their runtime phase.
- Input-edge redundancy cannot retrigger an action.
- A protocol round trip preserves the complete replay state.

### Milestone 3: Network Knight Avatar and Presentation

- Replace the placeholder capsule model with the generic Knight presentation.
- Keep the authoritative collision capsule separate from the model.
- Enable collision only where the local process owns authoritative/predicted
  physics; remote interpolated views remain presentation proxies.
- Add combatant hurt proxies tied to stable combatant and life-generation IDs.
- Drive locomotion animation from collision-resolved local velocity.
- Drive jump and roll presentation from replicated mode and start tick.
- Preserve the local camera during reconciliation.
- Smooth small visual corrections without smoothing authoritative collision
  state.

Acceptance:

- Host and client see the same movement actions.
- The owning client has immediate response.
- Remote animations begin from replicated action timing rather than guessing
  from position alone.
- No camera snapping, jump jerking, or half-second remote delay returns.

### Milestone 4: Authored Fighter Sword Timeline

Add immutable data definitions for:

- weapon attack set;
- weapon-owned attack-input policy, including press, hold, release, continuation,
  and future charge interpretations;
- grounded combo steps;
- shared crouched/airborne attack;
- startup, committed, active-hit, and recovery tick ranges;
- combo queue and continuation windows;
- animation semantic ID;
- movement scaling and code-driven lunge;
- attack-facing and turn-rate policy;
- jump/roll cancellation permissions;
- authoritative hit-query shape and sweep samples;
- effect payload and per-target hit policy.

Add runtime state for:

- action execution ID;
- source life generation;
- combo step;
- action start tick;
- queued continuation;
- targets already accepted;
- completion or interruption reason.

The timeline, not the animation, opens hit windows and applies movement rules.
The movement policy applies authored deceleration and lunge to the current
momentum. A percentage in an action definition scales acceleration/deceleration
or input authority; it must not silently clamp a fast character down to a
percentage of ordinary run speed.

The architecture must support weapons with no combo, any number of combo steps,
hold-driven sequences, precisely timed continuations, charged attacks,
sprint-context attacks, and other authored sets. The starter sword proves only
one definition; these alternatives do not become branches in the player
controller.

Acceptance:

- A three-step ground combo and the shared crouched/air attack are completely
  data-driven.
- Missing the continuation window resets the combo.
- One click cannot accidentally advance multiple steps.
- Startup/recovery cancellation and committed-phase rejection have unit tests.

### Milestone 5: Authoritative Attack Networking

- The owning client predicts the attack state and animation immediately.
- The local request is emitted on the input's next 60 Hz simulation tick and
  does not wait for the 30 Hz snapshot stream.
- Every attack attempt receives a monotonically increasing action sequence.
- The client resends unacknowledged action intent through the selected action
  delivery policy.
- The host authenticates combatant ownership, validates life generation,
  sequence, action state, timing, equipped weapon, and transition legality.
- The host creates the authoritative action execution and publishes its
  identity/start tick.
- Rejection repairs predicted action state without allowing the client to
  declare hits or damage.
- Remote clients start presentation from authority facts/snapshots.

Acceptance:

- Duplicate action requests create only one execution.
- A late request cannot act for an earlier life.
- Local attack animation begins without waiting for round-trip time.
- Host and client converge on the same combo step and action phase.

### Milestone 6: Server Hit Detection and Bounded Rewind

- Record compact authoritative hurt-proxy history by simulation tick.
- During authored active ticks, sweep the authored melee shape through the
  weapon/action path on the host.
- Use combatant and life IDs rather than node paths.
- Deduplicate targets through the action execution's hit policy.
- Validate eligible melee actions against a bounded historical pose near the
  client's requested tick.
- Measure RTT and jitter from authority-originated traffic acknowledged by the
  client. Never accept a client-declared ping value.
- Map the client's action tick onto the authority timeline, then clamp it to
  approximately half the host-measured smoothed RTT plus a small jitter margin
  and an absolute maximum rewind of 200 milliseconds.
- Expose RTT, granted rewind allowance, requested age, and actual rewind used in
  diagnostics.
- Provide a host debug toggle that disables lag compensation so testers can
  compare results.
- Keep static-world checks and final action legality at current authority time.

Acceptance:

- Each basic swing hits a target at most once.
- Crouched/rolling hurt-proxy geometry matters; rolling grants no automatic
  invulnerability.
- Targets outside the authored reach/arc do not take damage.
- Artificial-latency tests measure attacker and defender results.
- Artificial packet delay cannot grant more rewind than the host-measured
  allowance or the absolute cap.

### Milestone 7: Health, Hit Feedback, Elimination, and Respawn

- Register every spawned network combatant with the authoritative combat
  facade.
- Convert accepted hit candidates into the sword's authored damage operations.
- Publish authoritative action, hit, damage, health, elimination, and respawn
  facts.
- Replicate health redundantly in snapshots/checkpoints so missed presentation
  events repair themselves.
- Show readable player identity and health.
- Add minimum viable hit confirmation, target reaction, and elimination
  presentation.
- Disable combat/movement input while eliminated.
- Respawn automatically after two seconds at a randomly selected eligible spawn
  with full health, no protection, and a new life generation.
- Keep the local camera at the death location for the first second. Relocate it
  to the chosen spawn area during the second half of the countdown so the
  character does not reappear at an unrelated camera location.

Acceptance:

- Only the host changes health.
- All peers converge after dropped replaceable packets.
- Dead players cannot attack.
- Respawn creates a new life generation and stale attacks cannot hit it.

### Milestone 8: Main Launch Integration

- Keep `network_launcher.tscn` as the main scene.
- Make Offline Test, Host IP, Join IP, Host Steam, and Join Steam enter the same
  gameplay arena and avatar implementation.
- Preserve the existing Steam lobby/invite flow.
- Require all players to join before Start Match for this slice.
- Use the existing movement test arena with multiple authority-selected random
  spawn locations.
- Support colliding player bodies and the confirmed maximum player count.
- Prevent joining an incompatible protocol/build with a readable error.
- Add a compact developer overlay that can be hidden and records:
  - RTT and snapshot age;
  - packet loss and interpolation underruns;
  - local correction distance/frequency;
  - movement/action mode and authority tick;
  - local and targeted combatant health;
  - last accepted/rejected action result.

### Milestone 9: Verification

Automated:

- Plain C# movement and combat tests.
- Attack timeline, combo, cancel, and hit-policy tests.
- Protocol codec/validator tests for all new fields and messages.
- Authority command tests for duplicates, stale lives, illegal phases, and
  spoofed combatants.
- Headless Godot movement, attack sweep, death, and respawn probes.
- Two-process localhost ENet test.
- Artificial latency, jitter, packet loss, duplication, and reordering tests.

Manual:

- Host/client parity for every movement action.
- Camera stability during reconciliation.
- Short/long/landing roll presentation.
- Ground combo timing and missed continuation.
- Crouched and airborne attack.
- Attack while approaching, retreating, strafing, jumping, landing, and rolling.
- Simultaneous attacks and mutual elimination.
- Death during an active attack.
- Respawn while delayed packets from the previous life still exist.
- Two different Steam accounts on separate computers.

### Milestone 10: Steam Alpha Publication

- Build with the custom Steam-enabled Godot .NET executable.
- Run the Windows release export.
- Verify executable name, required DLLs, managed assemblies, PCK, and launch
  option.
- Run a local exported-build smoke test.
- Upload through SteamPipe without exposing credentials.
- Assign the build to the existing password-protected tester branch.
- Install/update through Steam on the host and at least one tester account.
- Record the Steam build ID and source commit in the playtest notes.

## Recommended Technical Boundaries

```text
Godot/Steam input and physics
        -> tick commands and identified spatial results
Shared character simulation driver
        -> complete movement/action runtime state
Authoritative match/combat application
        -> committed health and combat facts
Protocol mapping
        -> snapshots, actions, events, and repair baselines
Presentation adapters
        -> Knight animation, UI, sound, particles, and smoothing
```

No gameplay rule should depend on:

- an animation callback;
- `AreaEntered` arrival order;
- a Godot node path as network identity;
- a client-reported hit or damage value;
- wall-clock seconds instead of simulation ticks;
- Steam transport details.

## Related Documents

This plan specializes and supersedes older prototype notes in
`MOVEMENT_ARCHITECTURE.md`, `MULTIPLAYER_ARCHITECTURE.md`, and
`INPUT_AND_CONTROLS.md`. `DEVELOPMENT_BACKLOG.md` records implementation and
playtest follow-up without changing this plan's confirmed behavior.
