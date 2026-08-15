# Movement and Combat Feel Architecture

## Status

This document is a working architectural decision record. Proposed decisions are explicitly marked and are not final until reviewed. The first implementation target is a polished, server-authoritative, client-predicted third-person fighter movement slice.

## Accepted Working Decisions

### Character Facing

The initial controller uses a hybrid facing model:

- Ordinary movement is camera-relative, and the character turns toward the movement direction.
- With no movement input, the camera may orbit without rotating the character.
- Attacks turn the character toward the camera's horizontal aim direction, subject to an authored turn-rate limit.
- Individual attacks may restrict movement, preserve momentum, permit strafing, or change their facing policy.
- A future soft-targeting or lock-on capability may switch the character to camera-facing strafing without replacing the controller.

This is a tunable working decision. Play feel takes precedence, and the policy may be revised after testing.

### Prototype Character Assets

Use KayKit Adventurers for the initial movement slice. The automated baseline is the official CC0 Adventurers 1.0 GitHub/Godot Asset Library package, which includes rigged characters, weapons, textures, and 75 animations. Import one fighter, its required texture and weapon, and only the animations used by the slice. Preserve third-party license and source information alongside the promoted assets. The newer itch.io character and animation packs remain candidates if a durable reproducible source becomes available or they are manually evaluated and pinned.

### Camera Scope for the Movement Slice

Third-person is the only presentation target for the initial polished movement slice. The architecture must not prevent later first-person support, but first-person arms, weapons, attack presentation, roll presentation, mantle camera motion, body visibility, and camera-specific animation are outside the current slice. The existing runtime camera toggle may be disabled while this work is evaluated.

### Initial Climbing Scope

The first traversal slice includes low vaulting and ledge traversal. A player may jump toward a valid ledge, catch it at a fixed anchor, hang, and climb onto the upper surface. Lateral ledge shimmying, ladders, and unrestricted free climbing are not part of the initial system.

Ledge traversal is divided into explicit phases so it can be predicted, validated, animated, interrupted, and modified:

- approach and ledge acquisition;
- ledge catch;
- stationary hang;
- climb-up or drop;
- recovery onto the destination surface.

The ledge anchor does not move laterally during a hang. Level construction will provide readable, suitably dimensioned ledges, while geometric queries and optional surface tags determine actual eligibility. Items and runtime abilities may modify reach, permitted height, acquisition tolerance, hang rules, climb duration, vault duration, clearance policy, and eligible surface policy.

### Ledge Inputs and Base Behavior

- While airborne, movement toward a ledge with Jump held creates ledge-grab intent.
- Grab intent has a short configurable tick buffer and does not require frame-perfect contact.
- A successful query enters a visible minimum catch phase before climb-up is allowed.
- Holding Forward after the catch or pressing Jump while hanging initiates climb-up.
- Neutral input leaves the character hanging indefinitely by default.
- Pressing Backward or Roll drops from the ledge.
- The initial implementation has no lateral movement, jump-away, or hanging attack.
- A held Jump that acquired the ledge cannot skip the minimum catch phase.

Items and abilities may alter the input buffer, minimum catch duration, hang duration policy, climb inputs, reach, climb speed, drop behavior, or allowed actions while hanging.

### Crouch and Roll Input

The initial keyboard defaults are:

- Left Shift: contextual Crouch/Roll;
- Left Ctrl: Sprint.

Crouch/Roll is one authored input intent with contextual resolution:

- Pressing it while grounded and moving above the roll-entry threshold starts a roll.
- Pressing it while grounded without sufficient movement enters crouch.
- Beginning to move after entering crouch keeps the character crouched and permits crouch walking; it does not retroactively start a roll without a new press.
- Pressing it while hanging drops from the ledge.
- A roll is grounded-only in the base fighter.

Controller defaults will be finalized with the complete control layout, but the contextual action should occupy the conventional dodge/crouch face-button position if possible.

### Base Roll Policy

- Roll direction is selected from movement input with character-forward as the fallback.
- Direction is mostly committed once the roll begins.
- Available steering is an authored inverse function of current speed: slow movement permits more redirection, while high speed permits only a small correction.
- Displacement uses a deterministic, code-driven speed curve influenced by entry movement speed.
- A released input completes at the authored 0.32-second minimum. Holding the input extends the roll up to the authored 0.85-second maximum. Entry momentum remains the baseline throughout the action, while an authored 3-to-5-meter boost curve is added temporarily on top. These are initial tuning values rather than hard-coded rules.
- The roll has no invulnerability. Hits resolve normally throughout it.
- A roll cannot be canceled by attacks, jumps, another roll, or ordinary locomotion once it begins. Releasing the roll input selects the earliest authored completion point rather than canceling the action immediately.
- A cooldown begins after completion and must expire before another roll. Cooldown values remain modifiable.
- The roll changes the collision profile to represent the lower posture.
- If the standing profile has insufficient clearance at completion, the character remains crouched rather than intersecting the ceiling or being forced through geometry.
- The base fighter has no stamina resource or roll stamina cost.

Items and abilities may modify the curve, duration function, distance, steering-by-speed function, collision profile, entry threshold, cooldown, allowed starting states, and consequences of being hit. Invulnerability is not inherent to rolling but could still be an explicitly authored effect from an item or ability.

### Momentum-Scaled Roll

Roll boost distance and steering derive from entry movement speed, while input
hold time selects how long the committed action continues:

- Near the roll-entry threshold, the roll is shorter and slower with substantial steering.
- At normal running speed, entry momentum is preserved and roughly 3.8 meters of additional boost travel is layered over it. A tap produces a compact roll; holding continues the baseline movement and boost curve for substantially farther travel.
- At sprinting speed, the roll receives the largest boost and retains limited but useful steering.
- Scaling uses authored curves with diminishing returns so extreme speed modifiers do not produce unbounded duration or distance.
- Animation playback rate conforms to the resulting authored duration; animation timing does not determine simulation duration.

This deliberately makes a high-speed roll more effective for traversal. The
player chooses the period of commitment by releasing after the minimum duration
or holding through the maximum duration.

### Crouch Hold and Roll Completion

- Crouch is hold-based by default.
- Holding Crouch/Roll while stationary enters and maintains crouch.
- Moving after entering crouch produces crouch walking.
- Releasing Crouch/Roll attempts to stand.
- Starting a grounded roll requires the press edge. Releasing after the minimum duration completes the short form; holding prolongs it to the maximum duration.
- Holding Crouch/Roll while airborne with sufficient horizontal momentum queues a landing roll. Releasing before landing clears the queue.
- At roll completion, held Crouch/Roll requests crouch; otherwise the character attempts to stand.
- Failure of the taller-profile clearance query forces the character to remain crouched.

### Dynamic Collision Profiles

The base fighter has distinct standing, crouching, and rolling collision profiles. Crouching lowers the capsule, and rolling uses the lowest profile so a roll can physically pass beneath suitable obstacles. Every transition to a taller profile performs a clearance query before changing shape. Collision dimensions are authored values and may be modified, but a compiled profile must remain valid for Godot physics.

### Sprint Policy

- Left Ctrl is the default keyboard Sprint input.
- Sprint is hold-based initially; a later accessibility toggle produces the same internal sprint intent.
- Normal movement is a comfortable run, while sprint is a faster traversal speed reached through acceleration rather than an instantaneous velocity multiplier.
- Releasing Sprint returns to normal speed through authored braking/deceleration.
- Sprint may be requested in any movement direction because the hybrid facing model turns the character toward camera-relative movement input.
- Sprint is unavailable while crouched, hanging, mantling, rolling, stunned, or during an attack phase that disallows it.
- Jumping preserves existing sprint momentum.
- Sprint input while airborne intentionally accelerates toward sprint speed, providing additional maneuvering choice while preserving existing momentum.
- Entering a roll from sprint supplies the high-speed entry values used by the momentum-scaled roll.
- The base fighter has no sprint stamina cost or forced sprint duration.
- Controller analog magnitude remains part of the desired movement calculation.

Sprint maximum speed, acceleration, braking, permitted states, turn rate, and momentum preservation are compiled modifiable values and policies.

### No Stamina or Mana

Battle Arena does not use universal stamina or mana resources. Sprinting, jumping, crouching, hanging, rolling, attacking, and ordinary abilities are not gated by stamina or mana bars.

Balance comes from the authored behavior itself and from item trade-offs, including:

- speed, acceleration, distance, duration, and steering values;
- startup, commitment, recovery, and cancellation windows;
- cooldowns and per-item activation counts;
- damage, defenses, vulnerabilities, and other positive/negative effects;
- collision profile, allowed states, and contextual risk;
- synergies and desynergies with the rest of the build.

The movement controller, combat action model, item schema, and network protocol must not reserve stamina- or mana-specific state. An individual authored mechanic may still own explicit charges, counters, cooldowns, or another definition-specific value when that behavior requires it; these are not global stamina or mana systems.

### Jump Policy

- Space/A is the default Jump input.
- The base jump is responsive and arcade-like rather than strictly realistic.
- Initial tuning uses 120 milliseconds of coyote time and 120 milliseconds of pre-landing input buffering, compiled to integer ticks at the active simulation rate.
- Holding Jump produces full height. Releasing it early raises the authored rising gravity for a shorter hop without discontinuously changing velocity.
- Rising, apex, and falling gravity behavior are separately authored and smoothly interpolated through the apex velocity band. The fighter starts with a deliberately cartoonish roughly 3.9-meter standing jump, a brief soft apex, and a sharp 45 m/s² descent. These are tuning defaults, not hard-coded rules.
- Takeoff speed continuously contributes to vertical launch velocity. The base curve raises a 12.5 m/s sprint jump to roughly 4.4 meters while leaving the standing jump near 3.9 meters.
- Horizontal momentum is preserved at takeoff.
- Air input applies bounded acceleration instead of setting velocity directly. Forward/back authority is strong enough for a standing diagonal W+A/D jump to clear the 5.66-meter slalom-pillar spacing, while A/D-only authority remains weaker.
- Non-sprint input targets authored run speed and sprint input targets authored sprint speed. Existing takeoff momentum is always preserved, so a run jump does not manufacture sprint speed and releasing Sprint in the air does not erase sprint momentum.
- Air sprinting is an intentional base-fighter capability: pressing Sprint after takeoff may accelerate the player from run speed toward sprint speed while airborne. It provides additional maneuvering choice and is not treated as a prediction or momentum bug.
- Air steering changes velocity continuously and has no hidden takeoff-relative speed cap. Steering authority decreases with current horizontal speed; lateral authority falls more sharply than forward/back braking authority.
- Available redirection is inversely related to horizontal momentum: low-speed jumps permit more steering, while sprint jumps resist immediate reversal but may still curve.
- Ordinary landing does not lock movement; landing presentation blends without removing control.
- Landing severity is classified from impact velocity for animation, effects, and future authored mechanics.
- Jumping from crouch first requires standing-profile clearance. Success expands the profile and jumps; insufficient clearance rejects the jump.
- Ceiling contact terminates upward velocity cleanly.
- The base fighter has no double jump, wall jump, or air roll. These remain grantable authored capabilities.
- A buffered jump may trigger immediately on landing, while normal acceleration and speed policies prevent unintended momentum exploits.

Jump impulse/height, gravity segments, apex policy, coyote ticks, buffer ticks, jump-cut behavior, air acceleration, air speed, redirection-by-momentum curve, landing thresholds, and additional jump counts are compiled modifiable values or capabilities.

### Attack Commitment Phases

Every attack timeline has three semantic phases even when their durations differ by attack:

1. **Cancelable startup:** the attack has begun but may still be canceled by an allowed transition such as Jump or Roll.
2. **Committed phase:** the attack cannot be canceled by Jump or Roll. Active hit ticks usually occur within this phase, but phase and hit-window boundaries are authored independently.
3. **Cancelable recovery:** the attack is finishing and may again be canceled by an allowed Jump or Roll transition.

Canceling an attack into a roll does not make the resulting roll cancelable. A roll never cancels into an attack. Cancel permissions are authored policies and may be modified by items or runtime abilities without changing the fundamental phase identities.

### Weapon-Authored Attack Sets

Each equipped weapon item supplies an authored attack set rather than relying on a universal player combo. The set may contain:

- a grounded combo with any authored number of steps, including no combo;
- a contextual crouched/airborne attack;
- per-step animation identifiers;
- startup, committed, and recovery phase durations;
- combo input and continuation windows;
- movement scaling and code-driven lunge curves;
- facing and turn-rate policies;
- cancel permissions by phase and destination action;
- one or more active hit windows and query profiles;
- damage payloads and damage multipliers by step or hit window;
- authored trigger points for item abilities and effects;
- per-target hit limits and repeated-hit policy;
- landing, interruption, and completion behavior.

Combo length is data, not controller structure. One weapon may have a single attack, the default fighter sword may have three steps, and another weapon may have five or more. A particular combo step may deal additional damage, change damage types, apply an effect, or trigger an ability without the movement controller knowing the weapon's identity.

The initial fighter sword uses a three-hit grounded light combo. Its immutable
weapon-owned input policy interprets press, hold, and release as follows:

- a tap performs step one;
- continuing to hold through step one's continuation point queues step two;
- a separate press in step one's ordinary continuation window may also queue
  step two;
- holding after step two never queues step three;
- step three requires a release followed by a fresh press during an initial
  220-millisecond finisher window near the end of step two;
- only one continuation may be queued.

Missing the relevant continuation window resets the sequence. The policy is
owned by the weapon definition so future weapons may use a different number of
steps, charge behavior, sprint-context actions, or different input grammar
without adding weapon-specific branches to the player controller.

The default crouched and airborne attack use the same non-combo attack definition. It is a simple weapon-appropriate swing or jab with no lunge. Attacking while crouched does not attempt to stand. The same definition may select presentation variants if later required, while retaining the same gameplay timeline and payload.

Hit detection uses server-evaluated authored swept shapes or arc samples driven by the action timeline. It does not depend on animation callbacks or `AreaEntered`. The owning client predicts presentation immediately, while authoritative hit confirmation determines effects and damage. A target is hit once per action by default unless the attack explicitly grants a different repeated-hit policy.

Attacks preserve incoming momentum. Attack movement values scale input
acceleration and steering authority; they do not cap velocity to a percentage
of normal run speed. The starter sword begins steps one and two at 60% normal
movement authority and step three at 40%, with an additional authored
3 m/s² deceleration during each step. Code-driven lunges add to that motion.
The weapon does not provide sprint acceleration during these attacks. The
shared crouched/airborne attack preserves ordinary posture/air physics and has
no lunge.

Physical posture remains relevant: a crouched or rolling hurtbox may pass beneath an attack whose authored query does not overlap it. Rolling itself provides no invulnerability.

Impact presentation may use animation pause, camera response, particles, sound, and provisional local feedback, but it does not pause or rescale the authoritative simulation clock.

## Product Goal

Moving, jumping, rolling, mantling, turning, and attacking must feel responsive enough to be enjoyable before the item-selection game loop exists. The same mechanics must remain convincing for the owning client, the authority, and remote observers under realistic latency and packet loss.

The movement system must also support the item design. Items may change numeric values such as acceleration or jump impulse, add capabilities such as an air roll, alter transition rules such as attack-to-roll cancellation, or apply temporary runtime effects. These extensions must not require rewriting the Godot character controller.

## Current-State Audit

The current multiplayer slice successfully proves input transmission, server simulation, local prediction, reconciliation, and remote interpolation. It is intentionally not a production movement controller.

Current limitations include:

- Horizontal velocity changes instantaneously.
- Ground and air movement share almost all behavior.
- Jumping has no input buffer, coyote time, variable height, apex treatment, or landing response.
- Movement has no explicit runtime state beyond `is_grounded`.
- Input contains only movement axes, view angles, jump, and sprint.
- Prediction history stores inputs but not a complete movement-state result.
- Reconciliation restores position, velocity, view, and a one-use grounded override only.
- Remote snapshots do not identify movement actions, transition ticks, or animation state.
- The avatar is a capsule and sphere without a skeleton, `AnimationPlayer`, or `AnimationTree`.
- The sword swing is a rotated node. Its timing is not integrated with the multiplayer simulation.
- The offline player and network player use separate movement implementations.

The existing networking transport, sequencing, prediction-history concept, snapshot buffering, visual smoothing concept, input-remapping support, and `CharacterBody3D` scene boundary should be preserved. The current `NetworkMovementMotor` should be replaced rather than expanded into a large Godot-dependent class.

## Design Principles

1. **Immediate local response.** Local input begins visual and predicted motion on the next simulation tick without waiting for the server.
2. **Server authority.** The server validates transitions, environment queries, movement results, hit windows, and damage.
3. **One simulation model.** Authority simulation, client prediction, and replay after reconciliation use the same movement rules and fixed tick rate.
4. **Physics and presentation are separate.** The collision capsule is authoritative. The rig, camera, weapon, animation blending, lean, and correction smoothing are presentation.
5. **Simulation does not depend on animation callbacks.** Gameplay timelines drive hit windows and movement locks. Animation follows those timelines.
6. **Code-driven competitive displacement.** Jump, roll, knockback, and mantle displacement are simulated by code. Animations may use visual root motion, but imported root motion does not independently move the authoritative body.
7. **Explicit state.** Transitions, timers, action instances, and cancellation rules are represented in data and runtime state rather than inferred from scattered booleans.
8. **Items modify declared values and policies.** Equipment may modify compiled movement attributes or grant authored capabilities through stable extension points.
9. **Fixed-tick decisions.** Gameplay timing uses integer simulation ticks. Render interpolation and animation blending may use frame delta.
10. **Testability.** State transitions and numeric integration are unit-tested; Godot spatial queries and full prediction are exercised in headless integration tests.

## Proposed Architectural Boundaries

### Input Layer

`PlayerInputSampler` converts remappable keyboard, mouse, and controller actions into a tick-scoped `MovementCommand`.

The command contains:

- sequence and client tick;
- camera-relative movement axes;
- view yaw and pitch;
- held, pressed, and released action bits;
- optional analog action strength where a mechanic needs it.

Pressed and released edges are explicit so redundant input packets cannot accidentally retrigger one-shot actions. The server consumes a command at most once by sequence.

### Plain C# Movement Rules

`MovementStateMachine` owns transition rules and produces a movement intent from:

- the current `MovementRuntimeState`;
- the current `MovementCommand`;
- compiled movement attributes;
- an immutable capability set;
- environment facts returned by the Godot adapter;
- the fixed simulation tick.

This layer does not own a Godot node and does not perform collision queries. It can therefore be tested without launching Godot.

### Godot Kinematic Adapter

`GodotCharacterMotor` applies the movement intent to `CharacterBody3D`, calls `MoveAndSlide`, and reports resulting environment facts. Dedicated query adapters perform floor, ceiling, obstacle, vault, and mantle checks with Godot shape casts and ray casts.

Godot remains the source of truth for collision and spatial results. The rules layer remains the source of truth for what the player is attempting and whether a transition is permitted.

Ground step traversal is geometry-independent. Before committing blocked
planar motion, the Godot adapter uses body test-motion sweeps to establish
upward clearance, improved forward travel in the requested direction, and a
walkable downward landing within the compiled step height. A small compiled
forward-assist distance resolves tangent contact at convex corners. Authored
stair ramps remain useful level geometry, but ordinary ledge traversal must not
depend on them.

### Simulation Driver

`CharacterSimulationDriver` coordinates one fixed tick:

1. accept the command;
2. collect required environment facts;
3. resolve state transitions;
4. calculate desired acceleration and displacement;
5. execute Godot movement;
6. finalize the runtime state from collision results;
7. emit simulation events for presentation, combat, and networking.

Authority and prediction use the same driver. The offline test scene also uses this driver so movement behavior cannot diverge between offline and multiplayer play.

### Prediction and Reconciliation

Prediction history stores both commands and enough resulting state to diagnose divergence. An authoritative snapshot must eventually include:

- position and velocity;
- locomotion mode and action mode;
- state/action instance identifier;
- state start tick and relevant remaining timers;
- grounded and surface information needed for replay;
- facing direction and view direction;
- last processed input sequence;
- movement-attribute revision and capability revision.

On correction, the client restores the complete authoritative simulation state and replays unacknowledged commands. The physics body may be corrected immediately while a separate visual root consumes small render offsets smoothly. Large or invalid corrections snap according to an explicit policy.

### Presentation and Animation

`CharacterPresentationController` reads simulation state and events. It owns:

- `AnimationTree` parameters and transitions;
- locomotion blend spaces;
- jump anticipation, rise, apex, fall, and landing presentation;
- roll, mantle, attack, hit reaction, and death animation playback;
- model-facing interpolation and turn-in-place behavior;
- weapon attachment sockets;
- local correction smoothing;
- first-person visibility policy;
- remote visual interpolation.

Continuous locomotion is driven by collision-resolved velocity transformed into
character-local forward and lateral components. The presentation adapter damps
those values into a synchronized directional blend space and separately blends
the airborne pose. This avoids clip identity changes at arbitrary speed
thresholds and gives strafing and diagonal corrections continuous visual input.

Presentation never decides whether a jump, roll, mantle, attack, or hit occurred.

### Combat Action Timeline

An attack is an authored tick timeline with wind-up, active, and recovery phases. The authority evaluates hit queries during active ticks. The owning client predicts the action and animation immediately but does not predict final damage as authoritative truth.

Attack definitions may specify movement scaling, rotation policy, cancel windows, combo windows, hit shape, hit query policy, and animation identifier. Item effects can modify declared values through the existing compiled-modifier approach.

## Proposed State Model

A single flat enum would make valid combinations such as an airborne attack or moving while blocking difficult. The proposed model uses coordinated channels.

### Locomotion Mode

- `Grounded`
- `Airborne`
- `Rolling`
- `Mantling`
- `Disabled`

Future authored capabilities may add modes such as wall sliding, wall running, swimming, flying, or grappling without changing the meaning of existing modes.

### Posture Mode

- `Standing`
- `Crouched`

Posture is separate from locomotion because a character can remain grounded and move while crouched. Rolling and ledge traversal temporarily own their collision profiles; on completion they resolve the safe resulting posture from input intent and available clearance.

### Action Mode

- `Ready`
- `Attacking`
- `Blocking`
- `UsingItem`
- `Stunned`

Each mode has an instance ID and start tick. Transition policies decide which locomotion/action combinations are legal and which actions may cancel others. Items may modify values or grant a specifically authored transition policy; they do not mutate the identity of an existing policy at runtime.

## Initial Movement Feature Slice

### Ground Locomotion

- camera-relative input;
- configurable acceleration, braking, maximum speed, sprint speed, and turning rate;
- slope handling and stable floor adhesion;
- separate behavior when input is released versus when direction reverses;
- controller analog magnitude support;
- animation speed derived from physical speed to minimize foot sliding.

### Jump

- jump input buffering;
- coyote time;
- configurable impulse;
- variable height from early button release;
- separate rising and falling gravity values;
- optional apex gravity/air-control treatment;
- landing classification from downward speed;
- no animation callback dependency.

### Roll

- contextual activation from the shared Crouch/Roll input;
- directional selection from movement input with a facing-direction fallback;
- speed-dependent duration and an independently modifiable cooldown;
- authored speed curve sampled deterministically by normalized tick progress and scaled from entry speed;
- steering allowance evaluated as an inverse function of current speed;
- code-driven collision movement;
- a lower collision profile with clearance-safe crouch fallback;
- no inherent invulnerability;
- no cancellation after the roll starts;
- explicit rotation, hit response, and attack interaction policies;
- one predicted action instance reconciled by the authority.

### Mantle

The initial climbing feature supports a fixed-anchor ledge hang followed by a climb-up. It uses a reproducible query bundle: forward obstruction, ledge face, ledge-top location, hand/anchor tolerance, body clearance at the hanging pose, body clearance at the destination, allowed surface tags, and maximum height/depth. The server reruns and validates the query. Catch and climb motion follow authored code-driven paths while animation visually conforms to them.

Low vaulting is a separate authored action sharing the query infrastructure. Shimmying, ladders, and free climbing should be separate later capabilities rather than hidden variants of the first ledge traversal.

### Basic Sword Attack

- a three-step one-handed grounded light combo;
- one shared non-combo crouched/airborne attack with no lunge;
- predicted animation and movement response;
- server-authoritative active hit ticks;
- one hit per target per action unless authored otherwise;
- movement and facing policy during each phase;
- replicated action instance and start tick for remote animation;
- recovery and roll-cancel windows defined explicitly.

## Character Asset Requirement

A rigged humanoid is required before movement can be judged. Capsule placeholders cannot expose foot sliding, weak anticipation, poor landings, roll readability, weapon alignment, upper/lower-body blending, or remote animation discontinuities.

The asset is a presentation dependency, not a simulation dependency. The collision capsule and movement rules must continue working if the model is replaced.

### Current Prototype Candidate

KayKit Adventurers plus KayKit Character Animations is the leading prototype candidate because it provides:

- a colorful stylized fantasy appearance;
- rigged GLTF/FBX characters and weapons;
- movement, jump, dodge, melee, hit, death, and other humanoid animations;
- a consistent skeleton across the supplied characters;
- Godot-compatible files;
- a CC0 license suitable for source-controlled game assets.

The prototype should import only the chosen character, texture, skeleton, and animations needed for the first slice. Raw download archives and unused variants should not be committed.

## Scene Composition

Proposed `NetworkAvatar` composition:

```text
NetworkAvatar (CharacterBody3D)
|- CollisionRoot
|  `- BodyCollision
|- EnvironmentQueries
|  |- GroundProbe
|  |- ObstacleShapeCast
|  |- LedgeTopRay
|  `- MantleClearanceShapeCast
|- Simulation
|  `- CharacterSimulationDriver
|- VisualRoot
|  |- RiggedCharacter
|  |  |- Skeleton3D
|  |  `- WeaponSocket
|  |- AnimationPlayer
|  `- AnimationTree
|- CameraRoot
|  |- SpringArm3D
|  `- Camera3D
|- CombatQueries
|  `- SwordHitShape
`- Diagnostics
```

The exact imported rig subtree may differ, but scene code accesses it through exported paths and a presentation adapter rather than hard-coded importer-generated child names.

## Configuration and Item Integration

Movement uses a compiled attribute snapshot assembled in acquisition order, matching the established item rules. Initial attributes include:

- walk and sprint maximum speed;
- ground acceleration and braking;
- air acceleration and maximum air speed;
- ground and air turning rates;
- jump impulse, coyote ticks, buffer ticks, and gravity values;
- roll duration, cooldown, speed curve scale, steering, and invulnerability window;
- mantle height, reach, duration, and allowed surface policy;
- attack phase durations, movement scales, and turn limits.

Base class values, persistent item modifiers, and per-life runtime effects remain distinct inputs to compilation. The simulator receives an immutable compiled snapshot plus a revision. Changes take effect on the authored tick and are included in prediction reconciliation.

The base fighter values are authored in a versioned movement profile rather
than constructed inside a Godot controller. Content loading validates the
profile and compiles units such as designer-facing degrees per second into the
immutable units consumed by the plain C# simulator. Equipment and runtime
effects will compile subsequent revisions without changing the underlying
movement policies.

## Testing Strategy

### Plain C# Tests

- state transition legality;
- coyote time and input buffer boundaries;
- variable jump release policy;
- roll direction fallback, cooldown, curve sampling, and cancellation;
- mantle eligibility from supplied environment facts;
- attack phase and cancel-window boundaries;
- modifier acquisition order and movement-attribute compilation;
- command edge deduplication;
- state serialization round trips.

### Godot Headless Tests

- slopes, stairs, ceilings, moving into walls, and edge departure;
- ledge query false positives and destination clearance;
- capsule motion across representative physics-tick rates;
- authority/client replay with the same command stream;
- reconciliation after deliberately injected divergence.

### Multiplayer Feel Tests

- zero-latency baseline;
- latency, jitter, duplication, reordering, and packet loss simulation;
- local jump, roll, mantle, and attack correction magnitude;
- remote animation start-time error and interpolation continuity;
- camera and visual-root behavior during correction;
- host and client symmetry.

### Human Evaluation

Each mechanic receives a small tuning arena and on-screen diagnostics. Values remain configurable without recompilation. A mechanic is not accepted solely because it is correct; it must also pass repeated controller and mouse/keyboard play sessions.

## Delivery Order

1. Import and validate one rigged prototype character and animation set.
2. Introduce the new input command, runtime state, configuration, and pure transition tests.
3. Replace instantaneous locomotion in the offline scene using the shared simulation driver.
4. Add jump buffering, coyote time, variable height, and landing state.
5. Add presentation blending and tune ground/jump feel.
6. Extend protocol snapshots and prediction to the complete movement state.
7. Validate locomotion and jumping as host and client under simulated network conditions.
8. Add roll simulation, animation, prediction, and authority validation.
9. Add mantle queries, path, animation, prediction, and validation.
10. Replace the placeholder sword swing with an authored attack timeline and animation.
11. Perform latency/jitter/loss tuning and tester-facing polish.

## Character Presentation Boundary

The first prototype uses the KayKit Knight and one-handed sword. This is an
asset choice, not a simulation dependency. Movement, combat, prediction, and
weapon definitions refer to stable semantic animation identifiers such as
`locomotion.run`, `roll.forward`, and `attack.light.2`. A versioned character
presentation definition maps those identifiers to imported clip names, model
paths, sockets, and model-specific visibility rules.

The generic rig view owns model instantiation and animation-name translation.
Replacing the Knight therefore requires a new presentation definition and, when
necessary, another rig adapter; it does not require changes to authoritative
movement or network state. Temporary fallbacks for crouching and mantling are
explicit in the Knight definition until purpose-built animations are available.

Presentation definitions may also import clips from external animation-only
scenes. Each binding declares its source clip, target animation library, and an
explicit bone-name map when skeleton conventions differ. Rotation tracks are
retargeted as deltas from the source rest pose onto the destination rest pose;
limited root and hip translation is rescaled, while unsupported scale and
unmapped-bone tracks are discarded.

The Knight currently maps `roll.forward` to the CC0 Quaternius Universal
Animation Library `Roll` clip. This provides a genuine tuck-and-roll instead of
rotating the whole model as one rigid object. The clip is presentation-only and
is timed against the simulated maximum roll; an early release transitions out
at the simulated minimum. Simulation still owns displacement, collision,
commitment, and cooldown. Visual or imported root motion never becomes
authoritative.

## Open Decisions

1. Target tuning metrics and the first movement test-course layout.
