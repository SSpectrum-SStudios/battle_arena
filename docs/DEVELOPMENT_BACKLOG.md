# Development Backlog and Revisit Notes

## Purpose

This is the living checklist for work that is incomplete, intentionally
temporary, or worth revisiting after a playable slice exists. Detailed design
decisions remain in the focused architecture documents; this file records what
still needs attention and why.

The active delivery sequence for the next friends build is tracked in
`MULTIPLAYER_COMBAT_PLAYTEST_PLAN.md`. That plan covers shared multiplayer
movement, the starter sword, authoritative damage, test-arena respawns, and
Steam alpha publication. Items below remain follow-up work unless that plan
explicitly includes them.

When new work is discovered, add it here before relying on memory or scattered
comments.

## Status Legend

- `[ ]` Not started
- `[-]` In progress or partially proven
- `[x]` Accepted for the current milestone
- `[?]` Requires a design decision

An accepted item can be moved back to incomplete when playtesting exposes a
problem. Notes should describe observable behavior rather than only saying that
something "feels wrong."

## Immediate Movement Slice

- [x] Third-person camera and camera-relative ground locomotion.
- [x] Run, sprint, acceleration, braking, turning, slopes, stairs, and generic
  step traversal.
- [x] Variable-height jumping, momentum preservation, faster falling, apex
  treatment, coyote time, buffered input, and limited air control.
- [x] Contextual hold-to-crouch and moving roll input.
- [x] Tap-to-short-roll, hold-to-extend-roll, momentum-scaled boost, steering,
  collision profile, cooldown, and landing-roll input buffer.
- [x] Data-driven Fighter movement profile and plain C# movement rules.
- [x] Initial automated movement tests and Godot integration probes.
- [-] Roll presentation is acceptable as a temporary prototype, but is not
  final-quality animation.
- [x] Replicate the complete current movement state through 60 Hz authority
  frames, authority-accepted input replay, reconciliation, and visual-only
  remote prediction.
- [ ] Test run, sprint, jump, crouch, and roll with host and remote client under
  latency, jitter, and packet-loss simulation.

## Multiplayer Combat Friends Slice

- [x] Replace the multiplayer capsule placeholder with the generic Knight view.
- [x] Add complete movement input edges and authority snapshot replay state.
- [x] Add the JSON-authored starter sword and weapon-owned combo input policy.
- [x] Add separately authorized client attack requests and immediate authority
  action/damage events with snapshot repair.
- [x] Add authoritative melee queries, one-hit policy, Physical damage, health,
  simultaneous legal-hit acceptance, elimination, and unlimited respawn.
- [x] Add host-measured RTT/jitter and bounded target-pose rewind with an F10
  comparison toggle.
- [x] Pass the two-process headless ENet combat smoke test.
- [x] Establish the automated and visible multiplayer parity gate in
  `MULTIPLAYER_PARITY_CHECKLIST.md`; apply it after every relevant change.
- [x] Replace authority FIFO input replay with bounded newest-intent scheduling
  and cover the Combatant 3 multi-second delay with a three-process regression.
- [ ] Complete visible two-instance movement/combat/respawn feel testing.
- [ ] Complete a two-account Steam transport playtest.
- [ ] Add placeholder swing and impact audio.
- [ ] Publish the accepted Windows build to the `alpha` Steam beta branch.

## Movement Revisit

Movement must receive another dedicated polish pass after final or purchased
character models are available. The current mechanics are good enough to
continue development; acceptance here does not mean final production quality.

### Animation and Rigging

- [ ] Import the purchased full-resolution models and retain their Blender-ready
  source files in the asset workflow.
- [ ] Establish a stable production skeleton and bone-naming convention.
- [ ] Create or refine custom Blender animations for the chosen production rig.
- [ ] Replace temporary or approximate crouch idle and crouch movement clips.
- [ ] Improve run and sprint animation cadence, foot contact, directional
  blending, and transitions.
- [ ] Refine jump anticipation, ascent, apex, fall, and landing animations.
- [ ] Add landing variations driven by impact speed.
- [ ] Verify weapon sockets and held-weapon alignment in every movement pose.
- [ ] Test animation behavior at extreme item-modified movement values.

### Roll-Specific Revisit

- [ ] Create a custom tuck-and-roll animation designed for the production
  character proportions, armor, cape, and weapon.
- [ ] Keep the head, shoulders, knees, weapon, and cape clear of obvious ground
  penetration.
- [ ] Author distinct entry, tuck, contact, recovery, and crouched-exit poses.
- [ ] Make short-tap and long-held rolls read as intentional variations of the
  same action.
- [ ] Ensure animation progress matches the simulated duration without allowing
  animation root motion to control authoritative displacement.
- [ ] Evaluate whether a held roll should stretch one animation, hold a contact
  phase, or use authored continuation frames.
- [ ] Verify that landing directly into a buffered roll has no visual pop.
- [ ] Verify high-speed steering does not twist the lower and upper body
  unnaturally.
- [ ] Test rolling uphill, downhill, off ledges, into walls, beneath obstacles,
  and immediately after landing.
- [ ] Revisit roll distance, boost curve, minimum/maximum duration, steering,
  entry threshold, and cooldown after the custom animation is playable.

### Remaining Traversal

- [ ] Implement ledge detection using Godot spatial queries and plain-C# rules.
- [ ] Implement ledge hanging without shimmying.
- [ ] Implement climb-up/mantle movement, clearance validation, and animation.
- [ ] Make crouch drop the player from a ledge.
- [ ] Add multiplayer prediction and validation for ledge grab and mantle.
- [ ] Decide whether vaulting is part of the first traversal release or a later
  capability.

## Combat Slice

- [ ] Replace the prototype attack with the authored three-phase action
  timeline: cancelable startup, committed phase, and cancelable recovery.
- [ ] Implement the Fighter's default three-hit ground combo.
- [ ] Implement the shared crouched/airborne non-combo attack with no lunge.
- [ ] Drive combo length, phase timing, movement, turn limits, hit windows, and
  effect payloads from weapon data.
- [ ] Add authoritative swept melee hit detection.
- [ ] Predict local attack presentation while leaving hit confirmation and
  damage authoritative.
- [ ] Add hit reactions, damage feedback, death, and respawn presentation.
- [ ] Validate attack-to-jump and attack-to-roll cancellation windows.
- [ ] Test attacks and movement together in multiplayer.

## Items, Effects, and Abilities

- [x] Record the core effect/modification architecture and deterministic
  resolution policies.
- [x] Record the initial catalog of item-modifiable qualities in
  `ITEM_MODIFIABLE_QUALITIES.md`.
- [ ] Implement the compiled item-definition pipeline in C#.
- [ ] Implement JSON schemas, validation diagnostics, and versioning for
  externally authored items.
- [ ] Implement equipment slots, replacement, flexible-item selection, and
  universal per-item cooldowns.
- [ ] Implement immediate effects, timed effects, triggers, modifiers, and
  active-effect scheduling.
- [ ] Implement typed damage, resistance, weakness, over-resistance healing,
  healing, life steal, and overkill records.
- [ ] Implement effect selectors by tag, damage type, source, target, runtime
  identity, and spatial query result.
- [ ] Implement effect-chain activation budgets and deterministic loop
  protection.
- [ ] Build automated tests for every registered effect, modifier, policy, and
  serialization path.
- [ ] Build the standalone data-driven item creation application.

## Round and Selection Game Loop

- [ ] Implement lobby settings and validated match configuration.
- [ ] Implement lives, respawning, elimination, and round victory.
- [ ] Reset per-life player state while preserving authored world objects.
- [ ] Implement reverse-elimination selection order: the first eliminated
  chooses last.
- [ ] Show each losing player a private random offering that every player can
  see.
- [ ] Select one card from the default five possibilities.
- [ ] Guarantee an open-slot offering while any eligible slot is empty.
- [ ] Replace an equipped same-slot item when selected.
- [ ] Implement sequential extra selections: each extra pick receives a newly
  generated offering and the timer resets.
- [ ] Implement optional host-configured selection timers, with unlimited time
  as the default and random selection on expiration.
- [ ] Implement item-driven selection modifiers such as extra cards and extra
  sequential picks.
- [ ] Implement match victory after the host-configured number of round wins.

## Multiplayer and Steam

- [x] Local ENet host/join handshake and multiplayer movement baseline.
- [x] Steam application, depot, export, upload, lobby, and transport foundations.
- [-] Steam lobby hosting launches without the previously observed crash; a
  real remote connection still needs tester validation.
- [ ] Test Steam lobby invite, join, gameplay traffic, disconnect, and reconnect
  with at least one remote tester.
- [ ] Integrate all completed movement and combat state into the authoritative
  protocol.
- [-] Implement the remote-prediction architecture in
  `REMOTE_MOVEMENT_PREDICTION_ARCHITECTURE.md`. Stable identity, 60 Hz movement
  frames, authority-clock synchronization, path measurement, and adaptive
  authority-stream presentation, accepted-input relay, bounded prediction
  replay, and collision-safe visual separation are complete. The separate ENet
  direct-prediction mesh now authenticates in the three-process localhost gate.
  The Steam peer adapter is implemented with separate virtual-port/poll-group
  ownership and authenticated Steam identity checks; remote tester validation
  remains. Hardened protocol version 8 route-management, mutual-proof
  handshake, resolved movement configuration, direct-envelope, movement-bundle,
  correlated rollback-baseline, and direct-timing contracts are complete.
- [ ] Meet the remote-movement smoothness target on stable connections at or
  below 80 ms RTT while preserving immediate local-owner control.
- [ ] Add three-process latency, jitter, loss, reordering, route-failure, and
  direct-versus-authority mismatch tests for remote prediction.
- [ ] Add tester-visible diagnostics for sustained packet loss and missing
  cadence checkpoints without disconnecting healthy gameplay traffic.
- [ ] Validate Windows Steam builds after every network milestone.
- [ ] Create and validate the Linux export, depot, and Steam build path.
- [ ] Document the repeatable alpha-tester release process.

## General Polish and Release Work

- [ ] Replace developer-only arena UI with player-facing menus and diagnostics.
- [ ] Add a complete remappable keyboard, mouse, and controller settings screen.
- [ ] Add audio, particles, impact feedback, accessibility options, and camera
  comfort settings.
- [ ] Establish performance budgets for server simulation, rendering, effects,
  and network bandwidth.
- [ ] Test extreme host settings such as high life counts, round targets, item
  slots, and long matches.
- [ ] Create save-compatible content versioning and useful mod/item load-error
  reports.
- [ ] Perform repeated family-and-friends playtests and record findings below.

## Playtest Notes

Use one entry per distinct observation.

### Template

#### YYYY-MM-DD — Short title

- Build/commit:
- Player count and connection type:
- What happened:
- Expected behavior:
- Reproduction:
- Severity:
- Proposed follow-up:

## Deferred Ideas

- Team-based play may become a separate game mode or loop.
- Item forging or combining remains undecided.
- A future dedicated-server or ranked mode must preserve server authority, but
  ranked infrastructure is not part of the current party-game milestone.
- First-person presentation may return later; third-person remains the active
  movement target.
