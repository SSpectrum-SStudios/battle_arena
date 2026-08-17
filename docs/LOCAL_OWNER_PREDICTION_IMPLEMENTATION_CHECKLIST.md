# Local Owner Prediction Implementation Checklist

Status: Active implementation checklist  
Architecture source: `LOCAL_OWNER_PREDICTION_REDESIGN.md`  
Started: August 10, 2026

## Completion Rule

Every checkbox is an independently reviewable change with one declared purpose.
The listed files are the intended scope, with three files as the normal maximum;
generated files or an unavoidable integration seam may increase that count.

Later work must not silently broaden an earlier step. Discovered work is added as
a new checkbox rather than folded into the active one.

### Working pattern (adopted August 15, 2026)

Phases 0-4 used a per-item critic: implement one item, review it, fix, mark.
That found real defects but spent a full review cycle on every item. From Phase 5
onward the reviews are batched per phase instead, because the redesign document
and this checklist already carry the design detail a critic would otherwise have
to rediscover each time.

A phase now proceeds:

1. **Plan** the whole phase — every class and function, recorded on the items
   below before any code exists.
2. **Stub** the whole phase, one subsection at a time, with documentation
   comments stating what each type and member will do and why. Mark each
   subsection `Stubbed`. No critic runs during this step.
3. **Review the stubs** with one critic reading the entire phase. It is checking
   that the decomposition and contracts are right, which does not require
   implementation bodies.
4. **Apply** any missing interfaces or contract corrections the critic found and
   judged relevant. Mark every subsection `Stubs Reviewed`.
5. **Implement** the whole phase, one subsection at a time. Mark each subsection
   `Implemented`.
6. **Review the implementation** with one critic, checking that the phase does
   everything the phase was supposed to do.
7. **Fix** the findings, which should be few. Mark each subsection
   `Implemented And Reviewed` and set its `[x]`.
8. **Commit and push** the completed phase as one commit before starting the
   next phase.

A subsection's status line records which of those states it has reached. A step
becomes `[x]` only at step 7, and only when its listed focused verification and
the relevant regression gates pass alongside the critic's approval.

The active step is the first unchecked item whose prerequisites are complete.

## Phase 0 — Safe Migration Boundary

- [x] **P00-01 — Define the owner-prediction mode policy.**
  - Purpose: Make `Legacy` the explicit safe default and represent the disabled
    `FrameRewindV2` path without changing runtime behavior.
  - Target files: `OwnerPredictionMode.cs`, `OwnerPredictionModePolicyTests.cs`.
  - Verification: Focused tests prove the default, valid values, and rejection of
    undefined enum values.
  - Completed: Focused tests 4/4 and full multiplayer tests 180/180 passed.
    Independent critic verdict: `APPROVE` with no findings.

- [x] **P00-02 — Add a versioned prediction baseline identity.**
  - Purpose: Record source commit/build/protocol/simulation/movement identifiers
    without embedding credentials or mutable runtime objects.
  - Target files: `PredictionBaselineIdentity.cs`,
    `PredictionBaselineIdentityTests.cs`.
  - Verification: Value/validation tests reject missing or malformed identity
    components and preserve a valid identity exactly.
  - Completed: Focused tests 36/36 and full multiplayer tests 216/216 passed.
    The critic required Unicode separator, malformed-surrogate, and test-order
    hardening across two revisions, then returned `APPROVE`.

- [x] **P00-03 — Add prediction trace header serialization.**
  - Purpose: Produce a stable, human-readable trace header from the baseline
    identity while explicitly excluding secrets and personal network data.
  - Target files: `PredictionTraceHeader.cs`, `PredictionTraceHeaderTests.cs`.
  - Verification: Golden serialization and secret-name regression tests pass.
  - Completed: Focused tests 14/14 and full multiplayer tests 230/230 passed.
    The critic required broader segment-aware authentication/personal/network
    field-name protection, then returned `APPROVE`.

- [x] **P00-04 — Wire the mode policy into multiplayer composition.**
  - Purpose: Let `NetworkArena` select the legacy path through an injected/exported
    policy while V2 remains unavailable by default.
  - Target files: `NetworkArena.cs`, `network_arena.tscn`,
    `verify_multiplayer_parity.ps1`.
  - Verification: Existing ENet parity runs in Legacy mode and logs one bounded
    mode-selection diagnostic.
  - Completed: Godot C# build succeeded; core tests 134/134, multiplayer tests
    230/230, and full two-/three-player parity passed. The critic required a
    complete-line, exactly-once mode diagnostic assertion, then returned
    `APPROVE`.

## Phase 1 — Evidence, Impairment, and Feasibility

- [x] **P01-01 — Define owner input lifecycle telemetry.**
  - Purpose: Count generated, sent, received, applied, fallback, late, duplicate,
    and rejected input decisions without changing scheduling.
  - Target files: `OwnerInputTelemetry.cs`, `OwnerInputTelemetryTests.cs`.
  - Verification: Counter transition and invalid-transition tests pass.
  - Completed: Focused tests 24/24 and full multiplayer tests 254/254 passed.
    The critic required separate typed origin, mutually exclusive arrival, and
    mutually exclusive authority-application taxonomies plus future-proof enum
    and checked-overflow coverage, then returned `APPROVE`.

- [x] **P01-02 — Define correction telemetry and reason taxonomy.**
  - Purpose: Measure pre/post replay error, replay depth, first mismatch, and
    enumerated snap/rebase causes.
  - Target files: `OwnerCorrectionTelemetry.cs`,
    `OwnerCorrectionTelemetryTests.cs`.
  - Verification: Classification tests cover confirmation, ordinary, contact,
    history miss, epoch change, penetration, and extreme error.
  - Completed: Focused tests 47/47 and full multiplayer tests 301/301 passed.
    Four critic revisions added explicit measurement-frame semantics, separate
    lifecycle causes, coherent reason/evidence invariants, derived replay depth,
    current-frame zero-depth handling, and future-frame guards before the critic
    returned `APPROVE`.

- [x] **P01-03 — Add a bounded prediction telemetry ring.**
  - Purpose: Retain recent diagnostic samples with fixed memory and deterministic
    overwrite behavior.
  - Target files: `PredictionTelemetryRing.cs`,
    `PredictionTelemetryRingTests.cs`.
  - Verification: Capacity, wrap, ordering, clear, and no-growth tests pass.
  - Completed: Focused tests 14/14 and full multiplayer tests 315/315 passed.
    The critic required standards-correct non-generic enumeration, allocation
    evidence for concrete enumeration and span copying, and reference-identity
    wrap/clear coverage, then returned `APPROVE`.

- [x] **P01-04 — Add credential-safe trace formatting.**
  - Purpose: Export bounded owner telemetry for comparison without Steam tokens,
    route secrets, IP addresses, or display names.
  - Target files: `PredictionTraceFormatter.cs`,
    `PredictionTraceFormatterTests.cs`.
  - Verification: Golden trace and forbidden-field/name tests pass.
  - Completed: The final baseline/header/formatter focused gate passed 52/52 and
    the full multiplayer suite passed 317/317. Two critic revisions replaced
    arbitrary baseline strings with canonical diagnostic domains, centralized a
    single version-2 safe header, rejected malformed samples, made enum wire
    names explicit, and enforced payload-field, sample-count, byte-size, and
    culture-invariance guards before the critic returned `APPROVE`.

- [x] **P01-05 — Define deterministic unreliable impairment schedules.**
  - Purpose: Model delay, asymmetric delay, jitter, loss, bursts, duplicates,
    reordering, and stalls from a seeded schedule.
  - Target files: `NetworkImpairmentSchedule.cs`,
    `NetworkImpairmentScheduleTests.cs`.
  - Verification: Repeated seeds produce identical packet dispositions and all
    boundary policies are covered.
  - Completed: Focused tests 25/25 and full multiplayer tests 342/342 passed.
    Two critic revisions fixed maximum-ordinal burst arithmetic and added a
    cross-platform golden vector for every sampled stream, exact consecutive
    burst shape, direction separation, and duration-composition boundaries
    before the critic returned `APPROVE`.

- [x] **P01-06 — Decorate the authority transport for unreliable impairment.**
  - Purpose: Apply the deterministic schedule to unreliable application packets
    without pretending to emulate reliable retransmission.
  - Target files: `NetworkImpairmentTransportDecorator.cs`,
    `NetworkImpairmentTransportDecoratorTests.cs`.
  - Verification: Delay/loss/reorder/flush tests pass and reliable packets are
    explicitly passed through or rejected by policy.
  - Completed: Focused tests 23/23 and full multiplayer tests 365/365 passed.
    Two critic revisions added per-recipient route state, lifecycle purge and
    disposal, bounded atomic admission, pre-queue validation, and non-reentrant
    unreliable/reliable flushing before the critic returned `APPROVE`.

- [x] **P01-07 — Decorate the prediction-mesh transport for impairment.**
  - Purpose: Exercise the optional peer hint path independently of the authority
    path using the same schedule contract.
  - Target files: `PredictionMeshImpairmentDecorator.cs`,
    `PredictionMeshImpairmentDecoratorTests.cs`.
  - Verification: Mesh loss/recovery never disables authority-only operation.
  - Completed: Focused tests 18/18 and full multiplayer tests 383/383 passed.
    One critic revision preserved reliable control ordering across reentrant
    sends and detached terminal mesh wrappers from reusable inner transports
    before the critic returned `APPROVE`.

- [x] **P01-08 — Model reliable retransmission and head-of-line behavior.**
  - Purpose: Test reliable semantics below the application decorator with bounded
    retransmission/order behavior.
  - Target files: `DeterministicReliableChannelModel.cs`,
    `DeterministicReliableChannelModelTests.cs`.
  - Verification: Loss delays later reliable messages until retransmission while
    unreliable messages remain independent.
  - Completed: Focused tests 22/22 and full multiplayer tests 405/405 passed.
    Two critic revisions added a real reverse ACK path, in-flight duplicate
    handling, bounded observations and wire events, full-buffer head recovery,
    and atomic or explicitly terminal clock/capacity behavior before the critic
    returned `APPROVE`.

- [x] **P01-09 — Build the explicit-query feasibility probe.**
  - Purpose: Prove precreated profile RIDs can query historical transforms against
    static-only geometry without moving a live character.
  - Target files: `GodotKinematicQueryProbe.cs`,
    `kinematic_query_probe.tscn`, `run_kinematic_query_probe.ps1`.
  - Verification: Headless probe covers stairs, ramp seams, walls, ceilings,
    repeated queries, profile swaps, dynamic exclusions, and stable results.
  - Completed: The custom Godot headless probe executed 6,528 historical-transform
    queries across standing, crouched, and rolling profiles in 70.5 ms
    (10.80 microseconds/query). It verifies exact authored collider identities,
    static/dynamic isolation, stale-result clearing, stable repeated traces, and
    explicit cleanup. Multiplayer tests 405/405 and core tests 134/134 passed;
    one critic revision resolved every feasibility and runner-hardening finding
    before the critic returned `APPROVE`.

- [x] **P01-10 — Build the owner/world packet-size probe.**
  - Purpose: Measure Protobuf, envelope, authentication, and transport overhead at
    one, three, and eight players before state schemas freeze.
  - Target files: `PredictionPacketBudgetProbe.cs`,
    `PredictionPacketBudgetProbeTests.cs`.
  - Verification: Reports common/worst sizes and fails a configured 1,200-byte
    application ceiling rather than silently fragmenting.
  - Completed: Valid candidate Protobuf wire products now account for envelope,
    authentication, estimated transport framing, and IPv6/UDP bytes. The common
    eight-player Steam estimate uses one 988-byte world packet and 94,988 bytes/s
    per recipient; the fully saturated 16-source case uses eight explicit world
    parts and 457,612 bytes/s per recipient. The indivisible worst owner frame is
    1,189 accounted bytes. Focused tests 16/16, multiplayer tests 421/421, and
    core tests 134/134 passed. Three critic revisions added complete conceptual
    fields, independently decoded packed schemas, exact overflow/partition
    coverage, explicit topology/fanout, and honest transport-estimate provenance
    before the critic returned `APPROVE`.

- [x] **P01-11 — Build the replay CPU/memory probe.**
  - Purpose: Establish measured history memory, allocation, and replay cost for
    expected and worst depths.
  - Target files: `PredictionPerformanceProbe.cs`,
    `PredictionPerformanceProbeTests.cs`.
  - Verification: Probe reports 1/3/8-player allocation and p50/p95/p99 data and
    enforces bounded history/source/event storage.
  - Completed: The probe now owns exact unmanaged state/command/result/dependency
    layouts, bounded 256-frame allocation accounting, allocation-free managed
    replay checks, and independently keyed Godot query measurements. Four retained
    256-sample Godot runs are stored raw and aggregated in code: the expected
    8-combatant/3-character causal-island query batch measured a conservative
    1.263 ms p99, while the deliberately extreme 8-of-8 batch measured 28.697 ms
    p99. Native Godot allocation remains explicitly `NotMeasured`. Ten focused,
    431 full multiplayer, and 134 core tests pass; the Godot C# project builds.
    Three critic rounds resolved distribution provenance, storage ownership,
    allocation-scope, aggregation, and immutability findings before `APPROVE`.
    A later full-suite run exposed first-use `Stopwatch` initialization inside the
    allocation scope; timing is now primed before measurement and the same critic
    re-approved the correction.

## Phase 2 — Simulation, Presentation, and Lifecycle Separation

- [x] **P02-01 — Define prediction epoch identities.**
  - Purpose: Separate authority discontinuity, owner-control epoch, local rebase,
    and match-frame epoch instead of overloading one generation.
  - Target files: `PredictionEpochIds.cs`, `PredictionEpochIdsTests.cs`.
  - Verification: Construction, ordering, checked increment, and cross-type misuse
    tests pass.
  - Completed: Added strongly typed, positive, checked epoch counters for the
    core-owned match-frame numbering plane and multiplayer-owned authority
    discontinuity, owner-control, and client-local rebase planes. Invalid defaults
    cannot be advanced or ordered, no cross-type conversions exist, and comparison
    scope is explicit. Six focused, 437 full multiplayer, and 134 core tests pass.
    One critic revision moved `MatchFrameEpochId` into Core and completed the full
    relational-operator/scope contract before `APPROVE`.

- [x] **P02-02 — Implement the combatant prediction epoch gate.**
  - Purpose: Accept only evidence matching session/frame/life/discontinuity/control
    identities and keep local rebase client-only.
  - Target files: `CombatantPredictionEpochGate.cs`,
    `CombatantPredictionEpochGateTests.cs`.
  - Verification: Old-life, old-control, old-discontinuity, duplicate, reconnect,
    and local-rebase cases pass.
  - Completed: One gate now validates the full authority scope, applies only
    monotonic/coherent reliable lifecycle transitions, and advances a structurally
    client-only local rebase exactly once for each applied reset. Stale/future
    evidence, reconnect, respawn, teleport, global timeline reset, duplicate,
    rejection, and overflow paths fail closed. Eleven focused, 448 full
    multiplayer, and 134 core tests pass. Two critic revisions completed isolated
    stale/reset coverage and made default decision enums non-actionable before
    `APPROVE`.

- [x] **P02-03 — Populate and atomically reset client epoch state.**
  - Purpose: Fix the empty client life directory and clear every prediction stream
    together on life/control changes.
  - Target files: `NetworkArena.cs`, `CombatantPredictionEpochGate.cs`,
    `NetworkArenaLifecycleTests.cs`.
  - Verification: Client accepts current action/damage/life events, rejects old
    epochs, and clears pending owner state exactly once.
  - Completed: `NetworkArena` now routes spawn/snapshot/movement baselines and
    accepted/direct/action/damage/life evidence through one tested lifecycle
    coordinator. Stateless future-life events remain pending until a state-bearing
    baseline arrives. Applied transitions synchronously clear arena history,
    corrections, timelines, queued input edges, predicted attack/roll state,
    movement/action transients, velocity, and presentation exactly once. The
    temporary V1 life-to-discontinuity/control mapping is explicit and restricted
    to state-bearing authority data. Eighteen focused, 455 full multiplayer, and
    134 core tests pass; the Godot project builds. One critic revision replaced
    unsafe life-event promotion, completed the avatar reset, and introduced the
    production router seam before `APPROVE`.

- [x] **P02-04 — Add the predicted cue ledger.**
  - Purpose: Deduplicate jump, roll, swing, impact, and other presentation cues
    across first-run prediction, replay, and authority confirmation.
  - Target files: `PredictedCueLedger.cs`, `PredictedCueLedgerTests.cs`.
  - Verification: Stable event identities emit once, rejection invokes one repair,
    and life reset permits new cues.
  - Completed: Stable epoch/kind/origin/id/ordinal identities now survive replay
    and authority frame remapping; immutable cue-kind finality distinguishes
    replay-only presentation from authority-resolved transitions and actions.
    The bounded value ledger uses dual finality watermarks, retains unresolved
    authority cues, suppresses retired identities, clears atomically on epoch
    change, and allocates nothing on its warmed fixed path. Focused tests 21/21,
    full multiplayer tests 476/476, and core tests 134/134 pass; `diff --check` is
    clean. Four critic revisions resolved unsafe pruning, invalid decisions,
    frame-remap identity, local-only cue lifetime, and post-retirement policy
    reclassification before `APPROVE`.
  - Later verification: A full-suite run exposed incomplete first-use warm-up in
    the allocation test. The exact duplicate-lookup and batch-retirement workload
    now runs on a sacrificial preallocated ledger before the identical measured
    workload runs on a fresh preallocated ledger. Full multiplayer tests 518/518
    pass, and the original critic re-approved the strengthened evidence.

- [x] **P02-05 — Extract the character presentation controller.**
  - Purpose: Make one final simulation sample drive locomotion/action presentation
    without letting presentation mutate simulation.
  - Target files: `CharacterPresentationController.cs`,
    `CharacterPresentationControllerTests.cs`, `RiggedCharacterView.cs`.
  - Verification: One sample produces one update; replay context produces zero
    animation/audio/VFX operations.
  - Completed: A Godot-free controller now projects one immutable committed
    simulation sample into exactly one semantic view update, while historical
    replay returns before touching controller state or the adapter. Roll-start
    memory resets explicitly and cannot be changed by replay; default, invalid,
    and unknown committed inputs fail closed. `RiggedCharacterView` implements
    the semantic port without exposing imported animation names upstream.
    Focused tests 12/12, full multiplayer tests 488/488, and core tests 134/134
    pass; the Godot C# project builds with only two pre-existing nullable
    warnings and `diff --check` is clean. The critic returned `APPROVE` on the
    first review.

- [x] **P02-06 — Separate simulation, visual, and camera scene anchors.**
  - Purpose: Prevent authority correction of the simulation body from directly
    teleporting the model or local camera.
  - Target files: `network_avatar.tscn`, `NetworkAvatar.cs`,
    `NetworkAvatarSceneContractTests.cs`.
  - Verification: Scene contract locates sibling simulation/visual/camera anchors
    and preserves exported facade paths.
  - Completed: `NetworkAvatar` is now a `Node3D` composition root with sibling
    `CharacterBody3D`, visual, and camera anchors. Narrow position, velocity, RID,
    and floor-state facades preserve callers while gameplay remains owned solely
    by the simulation body. Routine authority correction cannot commit rendered
    positions; live movement, visual-only remote prediction, continuous respawn
    travel, and hard spawn/respawn snaps have distinct tested policies. Camera
    safety follows the rendered model, and hard snaps reset interpolation on all
    three anchors. Focused tests 17/17, full multiplayer tests 505/505, and core
    tests 134/134 pass; the Godot project builds with only the two pre-existing
    nullable warnings and `diff --check` is clean. One critic revision resolved
    rendered-position visibility and interpolation-history findings before
    `APPROVE`.

- [x] **P02-07 — Suppress every legacy replay presentation side effect.**
  - Purpose: Stop animation, view, camera, sound, and UI updates for each replayed
    historical command before replacing the motor.
  - Target files: `NetworkAvatar.cs`, `NetworkArena.cs`,
    `NetworkAvatarReplayTests.cs`.
  - Verification: Replaying 30 commands performs zero presentation calls and one
    final presentation sample.
  - Completed: Authority restore, every historical replay step, and the client's
    current simulation now run state-only inside one executable frame
    transaction. A single outer authorization publishes camera, anchors,
    continuous animation, and first-run attack cues after the final current
    state; alive, eliminated, zero-history, host, remote interpolation, and
    remote prediction paths have explicit one-publication policies. Executable
    tests cover 30 historical steps and fail-closed frame lifecycle, while source
    contracts bind those policies to the Godot runtime. Focused presentation
    tests 42/42, full multiplayer tests 518/518, and core tests 134/134 pass with
    a clean `diff --check`. One critic revision replaced the original lexical
    evidence and removed a duplicate alive-client publication before `APPROVE`.
    The final root Godot rebuild was deferred because the app's escalation
    reviewer reported its usage quota exhausted; the prior P02-06 root build was
    green and the critic found no apparent syntax defect in the touched methods.

- [x] **P02-08 — Move remote collision commits to fixed physics.**
  - Purpose: Eliminate render-rate mutation of blocking bodies while leaving
    visual-only interpolation in render processing.
  - Target files: `NetworkArena.cs`, `GodotRemoteMovementPredictor.cs`,
    `verify_multiplayer_parity.ps1`.
  - Verification: Instrumented parity proves collision commits occur only at
    physics boundaries and remote visuals still update.
  - Completed: Authority packets now retain their true transport receipt time
    and immutable bytes while waiting in a bounded inbox that drains only at the
    start of fixed physics. Every post-spawn transform, velocity, movement/profile,
    capsule, lifecycle, and collision-enabled write passes a fail-closed avatar
    phase guard; render interpolation writes only the visual anchor. The headless
    gate drives real remote movement, elimination, and a state-bearing life-2
    respawn, then proves the ordered epoch/collider/pose transition, actual visual
    movement, and zero render-body mutations. Godot build, core tests 134/134,
    multiplayer tests 521/521, two-process parity, three-player movement, and
    `diff --check` pass. Three critic rounds resolved lifecycle mutation coverage,
    transport-timestamp preservation, and explicit higher-life acceptance before
    `APPROVE`.

## Phase 3 — Typed Input, Bootstrap, and Draft Wire Contracts

- [x] **P03-01 — Establish the shared simulation-frame context.**
  - Purpose: Use existing `SimulationInstant`/`SimulationRate` for movement,
    action, and effects instead of a hidden movement tick clock.
  - Target files: `SimulationStepContext.cs`, `SimulationStepContextTests.cs`.
  - Verification: Checked frame/rate construction and exact duration mapping pass.
  - Completed: One immutable Core context now carries the existing shared
    `SimulationInstant`, negotiated `SimulationRate`, and an explicit current or
    historical-replay pass identity. One-tick and elapsed durations map through
    `SimulationDuration` with exact decimal rate conversion; default/invalid
    contexts, backward time, and checked frame overflow fail closed. Focused tests
    7/7 and full Core tests 141/141 pass. The critic returned `APPROVE` on the
    first review.

- [x] **P03-02 — Add typed delivery and intent identities.**
  - Purpose: Separate input sequence, packet sequence, transition ID, predicted
    action ID, and resolution sequence.
  - Target files: `OwnerPredictionIds.cs`, `OwnerPredictionIdsTests.cs`.
  - Verification: Type, ordering, overflow, and epoch-scoped identity tests pass.
  - Completed: Simulation-facing life, movement-transition, predicted-action,
    and authority-execution IDs are Core-owned; Multiplayer owns input/packet
    delivery and distinct transition/action result cursors. Closed scopes encode
    owner journal lifetime (session + combatant life + control), authority action
    lifetime (session + life), directional endpoint generations for main transport,
    and route + connection-attempt reset identity for prediction mesh packets.
    Cross-scope ordering fails closed, teleport discontinuities preserve journal
    tombstones, and control renewal preserves active authority executions. Focused
    tests 10/10 and full Multiplayer tests 531/531 pass with a clean `diff --check`.
    Five critic passes corrected layer ownership, scope lifetimes, cursor separation,
    directional route identity, retry-attempt identity, and stale contract text
    before `APPROVE`.

- [x] **P03-03 — Define the immutable owner simulation command.**
  - Purpose: Capture one target-frame input sample with full held movement/combat
    state, references, and configuration revisions.
  - Target files: `OwnerSimulationCommand.cs`,
    `OwnerSimulationCommandTests.cs`.
  - Verification: Validation, quantization boundary, immutability, and no hidden
    timing tests pass.
  - Completed: Core owns the canonical quantized axes/view, complete held
    movement/combat state, typed configuration revisions, centralized 16/8
    fixed-buffer limits, reference buffers, and immutable
    `CharacterSimulationInput`. Multiplayer composes that value with scoped
    owner delivery, discontinuity, and the sole explicit target frame. Frame
    zero is legal; huge finite axes preserve direction; every fixed/flexible
    activation hold is represented; and symmetric buffer boundary/value tests
    prove allocation-free copy semantics. Focused tests 12/12, Core tests
    141/141, and Multiplayer tests 543/543 pass. Two critic passes corrected
    layer ownership, time-domain policy, combat completeness, stable
    quantization, and buffer verification before `APPROVE`.

- [x] **P03-04 — Split authority and direct-hint projections.**
  - Purpose: Guarantee the authority receives full attack grammar while the peer
    hint schema cannot contain combat authority.
  - Target files: `OwnerCommandProjections.cs`,
    `OwnerCommandProjectionsTests.cs`.
  - Verification: Attack held/press/release survive the authority projection and
    are structurally absent from the direct projection.
  - Completed: Separate projector interfaces produce an authority-only value
    retaining the complete Core simulation input and an explicit peer-hint value
    containing only scoped movement, view, transition, and revision data. Held
    attack/item controls and journal action references survive authority
    projection exactly; the direct type cannot carry or recover them. An exact
    top-level schema assertion plus a recursive reachable BattleArena API-graph
    guard prevents later nested authority leakage. Focused tests 6/6 and full
    Multiplayer tests 549/549 pass. Two critic passes added recursive structural
    isolation and independent constructor-boundary coverage before `APPROVE`.

- [x] **P03-05 — Implement the movement transition journal.**
  - Purpose: Resend unique jump/roll/crouch/traversal intents until an idempotent
    terminal result is acknowledged.
  - Target files: `MovementTransitionJournal.cs`,
    `MovementTransitionJournalTests.cs`.
  - Verification: Loss, duplicate, reorder, remap, reject, expire, cursor cleanup,
    epoch reset, and bounded-capacity tests pass.
  - Completed August 11, 2026: added allocation-free client/authority journals
    with scoped identities, fair resend, exact-once authority tombstones,
    contextual terminal validation, bounded immutable lifetime/retention policy,
    authenticated empty-baseline restoration, fail-closed repair state, and
    sequence-exhaustion recovery. Focused tests pass 29/29 and the full
    Multiplayer suite passes 578/578. Four critic passes closed ACK-scope,
    decision-frame, restoration-gap, repair-lock, maximum-sequence, and retired
    fingerprint/live-intent correctness gaps before `APPROVE`.

- [x] **P03-06 — Implement the predicted action journal.**
  - Purpose: Correlate local action prediction with authority accept/remap/reject
    without duplicating intents per command.
  - Target files: `OwnerActionCommandJournal.cs`,
    `OwnerActionCommandJournalTests.cs`.
  - Verification: The same loss/duplicate/cursor/reset/capacity guarantees pass for
    actions.
  - Completed August 11, 2026: added separate client and authority action
    journals with all attack/item activation triggers, rendered-authority-frame
    evidence, explicit accept/remap/reject/expire/supersede results, distinct
    life-scoped authority execution correlation, fair bounded resend, scoped
    cursors, tombstone repair, authenticated empty baselines, and fail-closed
    identity/resolution exhaustion. Focused tests pass 20/20 and the full
    Multiplayer suite passes 598/598. The first independent critic review found
    no correctness or scope blocker and returned `APPROVE`.

- [x] **P03-07 — Implement the deadline-aware owner send window.**
  - Purpose: Prioritize contiguous live target frames, SACK gaps, new commands,
    and unresolved journals under a byte budget.
  - Target files: `OwnerInputSendWindow.cs`, `OwnerInputSendWindowTests.cs`.
  - Verification: Burst loss, interior gaps, consumed-frame pruning, deadline
    expiry, and encoded-budget property tests pass.
  - Completed August 11, 2026: added a bounded allocation-free owner resend
    window with exact sequence/frame continuity, monotonic SACK and consumed-frame
    pruning, stable retry fairness, expired-identity retirement, exact encoded
    byte budgeting, and cross-category command/transition/action reservations.
    Real journals now peek without mutation and commit only entries actually
    encoded, so partial packets cannot skip interior intents. Focused send-window
    tests pass 26/26, the combined window/journal suites pass 75/75, and the full
    Multiplayer suite passes 624/624. Three critic passes closed consumed-input
    identity reuse, cross-tier starvation, journal preflight/partial-commit, and
    constrained-byte fairness gaps before `APPROVE`.

- [x] **P03-08 — Define absolute revisioned lead updates.**
  - Purpose: Make lead changes idempotent with an absolute target, policy revision,
    and future effective frame.
  - Target files: `PredictionLeadUpdate.cs`, `PredictionLeadUpdateTests.cs`.
  - Verification: Duplicate/stale/conflicting revisions and unsafe effective
    frames are handled deterministically.
  - Completed August 11, 2026: added immutable typed lead/revision values,
    negotiated bounds, absolute scope-bound policy updates, and a future-frame
    safety gate covering observed authority time, scheduled commands, notice,
    and overflow. Duplicate/stale updates are idempotent, while contradictory or
    unschedulable current evidence fails closed into explicit timeline rebase.
    Authenticated bootstrap scope renewal is componentwise monotonic: the gate is
    permanently bound to one session/combatant, control always advances, and a
    respawn advances both life and control. Focused tests pass 20/20 and the full
    Multiplayer suite passes 644/644. Three critic passes closed stale-scope
    rollback, invalid revision ordering/advance, lifecycle composition, and
    respawn control-regression gaps before `APPROVE`.

- [x] **P03-09 — Implement bootstrap/control state transitions.**
  - Purpose: Model pre-match enable, first target frame, reconnect neutral pre-roll,
    respawn, clock-confidence loss, and owner-control renewal.
  - Target files: `OwnerPredictionBootstrap.cs`,
    `OwnerPredictionBootstrapTests.cs`.
  - Verification: Every state transition yields contiguous frames and no early
    high-ping match start.

- [x] **P03-10 — Implement zero/one/two-step command construction.**
  - Purpose: Preserve quick input during zero steps and avoid invented edges on
    the second step of a lead-growth callback.
  - Target files: `OwnerCommandStepBuilder.cs`,
    `OwnerCommandStepBuilderTests.cs`.
  - Verification: Quick tap, press/release, held input, camera update, zero-step,
    and two-step traces pass.

- [x] **P03-11 — Draft owner command/control Protobuf messages.**
  - Purpose: Encode target frames, quantized continuous input, batch-level unique
    journals, control epochs, SACK, consumption, and resolution cursors.
  - Target files: `client_commands.proto`, `common.proto`,
    `ProtobufProtocolCodecTests.cs`.
  - Verification: Round trips preserve all draft fields and encoded-size tests
    stay within the measured input ceiling.
  - Completed August 11, 2026: added protocol-next owner command, durable
    transition/action journal, scoped receive/consumption acknowledgement,
    bootstrap/baseline receipt, and absolute lead-update drafts without routing
    them through the live v1 envelope. The wire contract preserves complete
    session/frame/life/discontinuity/control identity, quantized input, all held
    combat controls, optional frame-zero-safe cursors, simulation/configuration
    bootstrap evidence, and the collision-content hash. Exact Protobuf sizing
    accounts for future-envelope metadata plus the audited ENet and Steam
    authentication/framing/IPv6-UDP profiles; the six-frame floor and an atomic
    16-transition/eight-action-reference command fit under 1,200 bytes, while
    oversized count combinations are explicitly classified for splitting.
    Focused tests pass 14/14 and the full Multiplayer suite passes 721/721. Four
    critic passes closed authority-override, bootstrap completeness,
    compatibility, optional-presence, journal-only, worst-varint, and atomic
    maximum-reference coverage before `APPROVE`.

- [x] **P03-12 — Validate draft owner command/control messages.**
  - Purpose: Bound frame windows, journals, references, axes/view, revisions,
    identity ownership, and malformed values.
  - Target files: `InboundMessageValidator.cs`,
    `InboundMessageValidatorTests.cs`, `ProtocolConstants.cs`.
  - Verification: Valid boundaries pass; fuzzed unknown/oversized/spoofed/stale
    messages fail with stable reasons.
  - Completed August 11, 2026: added fail-closed validation for every protocol-next
    owner command/control draft using authenticated full-scope expectations,
    retained/future frame bounds, exact selective journal membership, command and
    intent correlation, quantized input/held-state masks, configuration revisions,
    optional cursors, received-versus-consumed semantics, bootstrap scheduling,
    and exact collision-content identity. The conservative Steam body allowance
    accepts exactly 1,047 bytes and rejects 1,048; frame zero remains valid.
    First-seen intents must be attached once to their originating command, while
    exact-known journal-only retries survive loss and reorder. Focused validator
    tests pass 56/56 and the full Multiplayer suite passes 741/741. Two critic
    passes closed out-of-order journal gaps, atomic first transmission, duplicate
    dispositions, stale repairs, and exact byte-boundary proof before `APPROVE`.

## Phase 4 — Exact Authority Scheduling

- [x] **P04-01 — Define authority input decisions and fallback policy.**
  - Purpose: Represent received, repeated-continuous, neutral, rejected, late, and
    authority-override decisions for every combatant/frame.
  - Target files: `AuthorityInputFrameDecision.cs`,
    `AuthorityInputFrameDecisionTests.cs`.
  - Verification: Invariants forbid invented edges and multiple decisions for one
    frame.
  - Completed August 15, 2026: one authority combatant/frame cell now resolves to
    exactly one application decision. Received, repeated-continuous, neutral, and
    authority-override representations are mutually exclusive and validated as a
    unit; repeated continuous carries only axes/view/held state and structurally
    cannot replay a transition or action reference; neutral preserves only safe
    view and authority-resolved revisions. Arrival classification is a separate
    type, so a late, duplicate, or rejected packet is recorded without ever
    becoming a second application. Fallback derives gap length from the
    immediately preceding committed frame rather than a caller-supplied age, and
    refuses a non-adjacent or foreign predecessor. Focused tests 17/17,
    Multiplayer 760/760, and Core 141/141 pass. One critic pass returned two
    blockers, both fixed before `APPROVE`.
  - Discovered work folded into this step: the critic proved a stale command from
    a prior match-frame epoch could be applied to the same tick in a new epoch,
    because `OwnerSimulationCommand` never carried `MatchFrameEpochId` and
    `OwnerIntentScope` is deliberately a journal-lifetime scope (session + life +
    control) that must survive a timeline reset. The epoch was already on the wire
    in `OwnerPredictionScopeDraft.match_frame_epoch`, so the domain command was
    dropping a field the protocol has. `MatchFrameEpochId` is now a required,
    validated component of `OwnerSimulationCommand`, threaded through
    `OwnerCommandStepBuilder`, and compared by both the received-command and
    arrival matchers. Reconstructing it from the batch-level wire scope remains
    P04-08/protocol work.
  - Discovered work folded into this step: `AuthorityInputFrameDecisionSlot.TryCommit`
    was an instance method on a mutable struct, so committing through a
    `List<T>`/`Dictionary<K,V>` indexer reported `Committed` while silently
    discarding the decision — the exact single-assignment guarantee the type
    exists to provide, and the storage shape P04-02 most naturally reaches for.
    Commit is now `static TryCommit(ref AuthorityInputFrameDecisionSlot, ...)`, so
    that misuse is a compile error while the type stays allocation-free.

- [x] **P04-02 — Implement exact target-frame command storage.**
  - Purpose: Accept validated commands by target frame without newest-command
    compaction or FIFO time compression.
  - Target files: `AuthorityOwnerInputScheduler.cs`,
    `AuthorityOwnerInputSchedulerTests.cs`.
  - Verification: In-order, burst, gap, duplicate, reorder, and overflow tests
    account for every frame exactly once.
  - Completed August 15, 2026: admission and consumption are now separate
    operations, which is what structurally removes newest-command compaction. A
    command is stored in the cell its own target frame names, so a burst of four
    fills four cells and is consumed over four frames instead of collapsing into
    one integration step. Consumption advances the cursor by exactly one and has
    no batching, skipping, or compacting variant. A command for an already
    consumed frame is classified `LateCommand` before it can touch storage; a
    redundant resend is idempotent; a conflicting command for an occupied frame
    is refused so first admission always wins. Storage is a preallocated ring
    whose horizon is also the flood bound, so future-frame spam is refused rather
    than growing a queue. Focused tests 15/15 and Multiplayer 775/775 pass. The
    critic traced every admission/consumption path for double-consumption, skip,
    late-apply, and cross-lap aliasing counterexamples, independently verified the
    sequence/frame lockstep claim against `OwnerCommandStepBuilder` and
    `OwnerPredictionBootstrap`, confirmed the legacy `AuthorityMovementInputBuffer`
    is untouched, and returned `APPROVE` on the first review.
  - Note: `SequenceFrameSkew` rejects a command whose `sequence - target frame`
    offset disagrees with the scope anchor. This is safe because the owner command
    builder advances both in lockstep, and every lifecycle event that could change
    the offset (reconnect, respawn, timeline rebase) also advances the owner
    control epoch, producing a new scope and therefore a new scheduler anchor.

- [x] **P04-03 — Add deadline fallback and terminal dispositions.**
  - Purpose: Permanently consume missed frames with declared held/neutral policy
    and reject their late arrivals.
  - Target files: `AuthorityOwnerInputScheduler.cs`,
    `AuthorityInputFallbackTests.cs`.
  - Verification: No late frame runs after fallback; no repeated state invents an
    edge; consumption cursors advance correctly.
  - Completed August 15, 2026: `ResolveNextFrame` is now the authority entry
    point, and it is impossible to advance the cursor through it without also
    committing exactly one decision. A missed frame decays received -> repeated
    continuous -> neutral under the immutable bound, and fallback input is built
    from the authority's current view and configuration revisions rather than a
    stale command, so a missed frame cannot resurrect a superseded movement
    revision. Repeated continuous reuses axes, view, and held state only; the
    decision type independently re-validates that no transition or action
    reference survives, and `AppliedInputSequence` is structurally null for any
    frame no owner command supplied. Each resolved frame writes an immutable
    terminal disposition into a bounded ring, and `ConsumedThroughFrame` plus
    `CopyRetainedDispositions` supply the consumed cursor and bounded disposition
    list the authority state contract owes the owner. Focused tests 15/15,
    AuthoritySimulation 48/48, Multiplayer 790/790, and Core 141/141 pass. The
    critic verified all five consumption guarantees, traced every throw path for
    transactionality, and returned `APPROVE`; its one noted gap (retention
    arithmetic from a non-zero first frame) was closed with an added test.
  - Correctness fix made during this step: resolution is transactional. The first
    implementation consumed the frame before validating the override input and
    reason, so a rejected override retired a frame with no decision and
    permanently wedged the timeline. The decision is now fully constructed before
    the cursor moves, and a throwing call leaves the scheduler untouched.

- [x] **P04-04 — Integrate transition/action journals with scheduling.**
  - Purpose: Apply each legal durable intent once on its accepted/remapped frame
    and publish bounded terminal resolutions.
  - Target files: `AuthorityOwnerInputScheduler.cs`,
    `AuthorityIntentResolutionTests.cs`, `MovementTransitionResolver.cs`.
  - Verification: Loss/duplicate/reorder/remap/expire/property tests yield one
    terminal result per ID.
  - Completed August 15, 2026: a referenced intent is now decided purely from its
    own authored window and the frame being simulated: before its first predicted
    frame nothing happens, on that frame it is accepted, after it but inside the
    deadline it is remapped to an explicitly named frame, and past the deadline it
    expires. Because frames are consumed monotonically and each is offered to the
    intent sink exactly once, the first frame carrying a still-unresolved
    reference is provably the earliest frame authority could apply it on. Deadline
    sweeps run every frame, not only when something is referenced, so an intent
    whose commands were all lost still reaches a terminal result instead of being
    advertised forever. Fallback, repeated-continuous, and override frames carry
    no references by construction, so the "repeating a held command never invents
    a press" rule holds structurally rather than by convention. Focused tests
    18/18, AuthoritySimulation 66/66, Multiplayer 806/806, and Core 141/141 pass.
    Three critic passes were required before `APPROVE`.
  - Deviation from the listed scope: `PredictedActionResolver` lives in
    `MovementTransitionResolver.cs` beside the transition resolver rather than in a
    fourth file, so both share one frame rule and cannot drift apart in how they
    interpret a match frame. A read accessor (`TryGetIntent`) was also added to
    both authority journals; it has no behaviour, but the resolver cannot decide a
    frame without seeing an intent's authored window and any existing tombstone.
  - Correctness fixes made during this step, all from the same hazard class: the
    intent sink runs after the frame is committed, so anything that throws inside
    it consumes a frame whose decision never reaches simulation. Every injected
    seam is therefore validated instead of trusted. An admission policy returning
    a reserved or undefined rejection reason has that reason substituted rather
    than aborting the frame; an execution allocator that reports success with an
    invalid or foreign-scoped identity is classified as a repair condition; and
    allocator exhaustion no longer throws. Separately, the per-frame resolution
    buffers were sized to references only, which starved the expiry sweep of
    space exactly when the journal was fullest; they are now sized to references
    plus journal capacity so every result due on a frame is reported on that
    frame.
  - Discovered work folded into this step: `Superseded` was one of the four
    terminal categories the design names but was unreachable, because the only
    seam able to refuse an intent was forbidden from using that reason. Admission
    policies now return an explicit `Admit`/`Reject`/`Supersede` decision, so all
    four categories are reachable from the scheduling integration.

- [x] **P04-05 — Implement adaptive lead control.**
  - Purpose: Derive a bounded lead target from clock/path confidence, jitter, and
    actual authority buffer occupancy.
  - Target files: `PredictionLeadController.cs`,
    `PredictionLeadControllerTests.cs`.
  - Verification: 0/40/80/120/200/300 ms profiles converge without oscillation or
    varying fixed delta.
  - Completed August 15, 2026: the target lead is the documented sum of one-way
    travel frames, clamped jitter safety frames, and the authority buffer target,
    corrected by measured occupancy rather than inferred from RTT alone. All six
    required RTT profiles settle and then stop changing entirely, and the settled
    lead grows monotonically with RTT. The controller has no access to and no
    concept of the physical step duration as an output, so it structurally cannot
    implement the rejected vary-the-delta approach; it emits only whole frame
    counts. Output is absolute and revisioned, and every emitted update is
    accepted by the P03-08 client receive gate with a monotonic revision and an
    effective frame beyond both the notice bound and any already scheduled
    command. Focused tests 32/32, Multiplayer 840/840, and Core 141/141 pass.
    Two critic passes were required before `APPROVE`.
  - Correctness fixes made during this step. The first control law latched: an
    asymmetric magnitude deadband let the target rise through a narrow grow
    threshold and never fall back through a wider shrink threshold, so an
    ordinary transient buffer dip stranded the owner with permanent extra
    authority-side latency. The deadband is now symmetric, and reluctance to
    shrink is expressed as time and step size, which cannot latch. Occupancy
    correction became one-sided — it may only shorten the lead — because a
    shallow buffer proves nothing on its own while a deep one proves the travel
    estimate is overshooting. Starvation no longer bypasses rate limiting
    outright; it has its own shorter clock, since one fallback-filled frame per
    evaluation previously emitted one wire update per evaluation. Non-finite or
    negative path samples are now rejected before any arithmetic, where a NaN
    would otherwise become `int.MinValue` and jump past every limit. The shrink
    step now scales with distance from target, cutting worst-case recovery from
    the ceiling from about 72 seconds to under 30.
  - Discovered work folded into this step: the design's lead ceiling is a
    duration, not a frame count — 24 frames at 60 Hz and 48 at 120 Hz are both
    400 ms — but `PredictionLeadUpdatePolicy.Default` is a flat 48 frames, which
    silently doubles the ceiling at the negotiated 60 Hz default.
    `PredictionLeadControllerPolicy.MaximumLeadFramesForRate` and
    `LeadPolicyForRate` now derive it from the negotiated rate.
  - Carried forward for P04-06/P04-09, raised by the critic and deliberately not
    handled here: `RebaseRequired` currently repeats on every evaluation while
    sustained low confidence or starvation-at-maximum-lead persists. Whichever
    step consumes that signal must debounce it, or a rebase — a heavier message
    than a lead update — becomes its own amplification vector. Also noted: a
    buffer that sits chronically thin without ever actually starving gets no
    proactive lengthening, and waits for one real starvation event before
    recovery begins.

- [x] **P04-06 — Implement authority hitch and timeline-reset policy.**
  - Purpose: Preserve every fixed frame through bounded catch-up and emit one
    reliable epoch reset only when lag/history is unrecoverable.
  - Target files: `AuthoritySimulationClock.cs`,
    `AuthoritySimulationClockTests.cs`.
  - Verification: Normal hitch, catch-up cap, slow wall-time recovery, and hard
    reset traces never increment an unsimulated frame.
  - Completed August 15, 2026: pacing is now owned by one negotiated clock rather
    than a hard-coded fixed delta. Two rules are structural. Simulation time moves
    only inside `CompleteFrame`, which the caller invokes once per frame it really
    ran, so planning alone advances nothing and completing an unplanned frame
    fails closed. Elapsed wall time is never discarded: a callback runs at most
    the catch-up cap and stays behind, and the remainder stays owed, so a hitch is
    recovered frame by frame with none skipped or compressed. Unrecoverable lag
    freezes the match and raises exactly one reset, latched rather than repeated
    every callback, since it is a heavy reliable message. Frame boundaries are
    stamped per frame so clock synchronisation can reply with a real physics
    boundary instead of a tick sampled at an arbitrary callback time. Focused
    tests 24/24, Multiplayer 864/864, and Core 141/141 pass. Two critic passes
    were required before `APPROVE`.
  - Correctness fixes made during this step: a reset raised before the current
    epoch had simulated anything named a last simulated frame derived from the
    cursor, which no epoch had ever run — the gate is now an epoch-scoped frame
    count, cleared on resume, not the lifetime total. Boundary occupancy used a
    zero timestamp as its empty sentinel, so a monotonic clock legitimately
    reading zero made a real boundary permanently unqueryable; occupancy is now a
    parallel frame stamp and lookup is O(1) instead of a linear scan. Freezing
    consumed the planned steps' worth of owed time before deciding not to run
    them, understating the reported lag; the reset test now runs against the full
    unconsumed deficit and freezing consumes nothing.
  - Design decision made during this step: an implausibly long callback interval
    is no longer clamped and absorbed. Truncating it would silently discard time,
    which this design prohibits, so an interval beyond the per-callback bound is
    itself treated as unrecoverable and resets the timeline, reporting the
    interval as observed. The bound must be at least the maximum recoverable lag,
    so an ordinary hitch is never mistaken for a suspended process. Callers must
    feed elapsed-since-last-boundary, not elapsed-since-load.
  - Note for P04-09: this closes half of the debounce concern carried from
    P04-05. The clock latches its own reset, but the lead controller's
    `RebaseRequired` signal still repeats while its condition persists, so the
    composition step must debounce that one.

- [x] **P04-07 — Add the listen-host in-memory command publisher.**
  - Purpose: Route host input through the same scheduler without network loss and
    without directly controlling authority state.
  - Target files: `InMemoryAuthorityOwnerCommandPublisher.cs`,
    `InMemoryAuthorityOwnerCommandPublisherTests.cs`.
  - Verification: Host/remote publishers produce identical validated command
    semantics given identical evidence.
  - Completed August 15, 2026: the host is now a client of its own authority. It
    has no path that writes authority movement state, cannot skip the scheduler,
    and gets no relaxation of the late, horizon, duplicate, or conflicting-command
    rules. Every host command is put through the same authority projection the
    wire path uses and rebuilt from that projection, so any field the projection
    drops is dropped for the host too. Removing the network is the only difference;
    the host will naturally suffer fewer corrections because its commands genuinely
    do not traverse one, which is a physical fact rather than a rules advantage.
    Focused tests 7/7, Multiplayer 871/871, and Core 141/141 pass. Two critic
    passes were required before `APPROVE`.
  - Correctness fix made during this step: the first implementation silently
    coerced a caller-supplied match-frame epoch to the authenticated one while
    rejecting a mismatched authority discontinuity. The wire validator rejects a
    whole batch on any scope mismatch, so coercing one component and rejecting
    another handed the host a rule no remote client gets. Every scope component
    is now refused the same way, before the scheduler sees the command.
  - Test-quality fix made during this step: the parity test originally compared
    two in-memory publishers, which compared the code path to itself and proved
    nothing. The remote side now goes through a real wire path — mapped into the
    protocol draft, serialised and deserialised with Protobuf, validated by the
    real `InboundMessageValidator`, then rebuilt from the decoded bytes. The
    critic confirmed all eight `CharacterSimulationInput` components are carried
    and that every draft field is full width for the domain type it holds, so the
    round trip cannot flatter the host by preserving something the wire would
    drop.
  - Note for P04-09: composition must rebuild the scheduler and publisher together
    on any epoch change. A host command reaching a publisher whose scheduler has
    already moved to a new epoch is now hard-rejected rather than tolerated —
    which is correct, and is exactly what a remote client's stale-epoch batch
    already does.

- [x] **P04-08 — Publish exact owner scheduling acknowledgements.**
  - Purpose: Return represented frame, applied input, application kind, received
    SACK, consumed cursor/dispositions, journal resolutions, and absolute lead.
  - Target files: `authority_state.proto`,
    `AuthorityOwnerStateMapper.cs`, `AuthorityOwnerStateMapperTests.cs`.
  - Verification: Round trips and mapping tests distinguish received from
    consumed and remain idempotent.
  - Completed August 15, 2026: received and consumed are now separate facts on
    the wire and cannot be confused. Receipt is sequence-based and consumption is
    frame-based, in distinct submessages, so a command stored three frames ahead
    of the cursor is reported as received while the consumed cursor still names
    an earlier frame. The mapper is a pure structural translation: it makes no
    admission, resolution, or scheduling decision, and the two fields the wire
    omits — a transition's application frame and an action's start frame — are
    provably derivable, because both domain constructors already require them to
    equal the decision frame when applied and to be absent otherwise. Every
    terminal outcome of both journals round trips, including remapped and
    superseded. Focused mapper tests 35/35, scheduler tests 28/28, Multiplayer
    919/919, and Core 141/141 pass; the Godot C# project builds with only the two
    pre-existing nullable warnings and `diff --check` is clean. Three critic
    passes were required before `APPROVE`.
  - Deviation from the listed scope: a fourth file,
    `AuthorityOwnerInputScheduler.cs`, gained a received-input-sequence window.
    The mapper cannot publish a receive acknowledgement the scheduler does not
    track, and nothing else in Phase 4 tracked one. It is new behaviour rather
    than plumbing, so it carries its own tests in the scheduler's own suite.
  - Correctness fixes made during this step, all found by the critic. The
    disposition list was unbounded while the wire caps it at 64 and the scheduler
    retains 128, so from about one second of play onward the authority would have
    emitted acknowledgements its own peer validator rejects; all three collection
    bounds are now enforced on construction and on decode. `FromProtocol` threw a
    raw `NullReferenceException` on the ordinary proto3 encoding that omits an
    optional submessage. A mixed-scope aggregate decoded without complaint,
    because the scope rides the wire three times and only the encode side treated
    their agreement as an invariant; all three copies are now cross-checked.
    `ReceivedCommand` is the zero enum value, so a default disposition encoded as
    a received command at tick zero with no sequence — both directions now
    require exactly that kind to carry a sequence. Validated collections were
    aliased rather than copied, so a caller could append a foreign-scope entry
    after the check.
  - Correctness fixes made to the receive window itself, which took two further
    critic rounds and were the substantive risk in this step. The first version
    froze permanently at the first lost packet and recorded nothing at all on any
    scheduler built mid-stream — which is every respawn and reconnect, since
    `OwnerIntentScope` excludes the authority discontinuity and sequences
    therefore continue across one. The repair then over-corrected in both
    directions: seeding the base from the first packet to *arrive* claimed every
    sequence below it, so a single reordered first packet advertised still-live
    frames as resolved and the client pruned commands the authority had not yet
    run; and retiring dead sequences by extrapolating the frame/sequence offset
    forward claimed sequences the owner never sent, which
    `OwnerInputSendWindow.ApplyAuthorityProgress` rejects wholesale — discarding
    the consumed cursor riding alongside it, so the client stopped pruning
    exactly when recovery mattered. The window is now bounded below by the
    consumption cursor and above by the highest sequence actually admitted, and
    retirement is re-evaluated on admission rather than decided once when a frame
    leaves, because a one-shot decision stalls whenever a frame is consumed
    before the later command arrives.
  - Design decision made during this step: the receive cursor means *resolved* —
    admitted, or permanently superseded because its target frame was consumed —
    rather than *delivered*. Both mean "stop resending" to the send window, and
    the design's own rule is that a late copy of a consumed frame cannot help and
    is not resent forever. This does not collapse received into simulated: whether
    a frame actually ran on owner input is carried per frame by the terminal
    dispositions, which is the only place the design puts it. Delivered semantics
    carried a free invariant — the cursor never exceeds what the owner sent — that
    two existing consumers already depend on; resolved semantics has to establish
    that invariant explicitly, which the two bounds above are.
  - Discovered work, recorded rather than folded in: no message-level validator
    exists for `AuthorityOwnerStateDraft`, and no remaining checkbox names one.
    The decode path fails closed on everything checkable from the message alone,
    but context-relative checks — is the tick inside published authority time, is
    the sequence originated, is the lead inside the negotiated policy — need the
    receive context and are the validator's job. See P04-10.

- [x] **P04-09 — Integrate the scheduler behind the V2 feature flag.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Run exact scheduling in multiplayer composition without making V2
    the default or changing the legacy path.
  - Target files: `AuthorityOwnerSchedulingHost.cs`,
    `AuthorityOwnerSchedulingHostTests.cs`, `NetworkArena.cs`,
    `verify_multiplayer_parity.ps1`.
  - Verification: Legacy parity stays green; V2 scheduler smoke accounts for
    every frame and both host/client use the scheduler.
  - Planned decomposition. The composition lives in `BattleArena.Multiplayer`,
    not in `NetworkArena`, so it is testable without Godot and so the 3,000-line
    arena node does not absorb another subsystem. `NetworkArena` holds one host
    and delegates.
    - `PredictionRebaseDebouncer` — latches `RebaseRequired` so a persistent
      condition emits one rebase rather than one per evaluation. Carried
      constraint 1. Members: `TryClaim(SimulationInstant)`, `Release()`,
      `IsLatched`.
    - `AuthorityOwnerCombatantScheduling` — everything owned per controlled
      combatant: scheduler, both authority journals, frame intent resolver,
      lead controller, lead revision counter, rebase debouncer, and the
      in-memory publisher when this combatant is the listen host. Rebuilt as a
      unit on epoch change, which is carried constraint 2.
    - `AuthorityOwnerSchedulingHost` — the composition root. One
      `AuthoritySimulationClock` for match pacing plus one
      `AuthorityOwnerCombatantScheduling` per combatant. Members:
      `RegisterCombatant`, `RemoveCombatant`, `RebuildForEpoch`,
      `TryAdmitRemoteCommand`, `PublishHostCommand`, `Advance`,
      `ResolveFrame`, `CompleteFrame`, `EvaluateLead`, `BuildOwnerState`,
      `TryGetScheduling`.
    - `NetworkArena` seam — `ConfigureOwnerPredictionMode` stops throwing on
      `FrameRewindV2` and builds the host; `SimulateAuthority` routes through
      it when V2 is selected and is otherwise untouched. The node implements
      `IAuthorityFrameSimulator` rather than driving the frame loop itself.
  - Stub review changes, applied before implementation:
    - Added `IAuthorityFrameSimulator` and moved the frame loop into
      `RunDueFrames`. The host now drives every combatant through every due
      frame and calls back; the caller cannot skip a combatant, resolve one
      twice, complete an unsimulated frame, or admit a command mid-frame,
      because it never gets the opportunity. Four ordering hazards became
      structurally impossible instead of conventions to remember.
    - Added `ResumeAfterMatchEpochReset` and `DemandTimelineReset`. The clock
      can reset to a new `MatchFrameEpochId`, and that epoch is part of every
      combatant's identity, so a reset invalidates all of them at once. Without
      a match-wide rebuild the host was permanently unusable after the first
      reset. This is the match-wide half of the rebuild-together rule.
    - Added `TryObserveRemoteIntents`. A command carries only intent
      *references*; the authority journals must observe the intent itself or
      every reference resolves as not-observed and no transition or action is
      ever applied. The wire batch carries them in separate fields precisely
      for this, and the stub had no ingress for them.
    - Added `AcknowledgeResolutions`. Without it terminal results are retained
      and resent until the retention timeout latches a baseline repair.
    - Added `AuthorityOwnerSchedulingContinuity`. `OwnerIntentScope` excludes
      both the match-frame epoch and the authority discontinuity, so a teleport
      or timeline rebase yields a new epoch over an *unchanged* journal
      lifetime. Rebuilding from zero there restarts resolution sequences while
      the client's applied cursor is ahead, so every new resolution is
      discarded as stale, and restarts the lead revision, which the P03-08
      client gate treats as contradictory and locks on.
    - Deleted the duplicate lead-revision allocator.
      `PredictionLeadController` already allocates and stamps the revision and
      returns the finished update in `PredictionLeadEvaluation.Update`; the
      stub had added a competing second source of both.
    - `PredictionRebaseDebouncer` gained `ObserveRebaseNotRequired`. Low clock
      confidence is transient and can recur with no epoch change, so releasing
      only on rebuild would swallow every rebase demand after the first.
    - `EvaluateLead` now takes only measured path and clock confidence; buffer
      occupancy, starvation-since-last-evaluation, and last scheduled frame are
      host-owned. `BuildOwnerState` dropped its lead-update parameter for the
      same reason and returns `AuthorityOwnerStatePublication`, whose
      `HasMoreToPublish` prevents silent truncation at the protocol bound.
    - Owner state resolutions come from the journals' unacknowledged sets, not
      the last frame's results, so frames resolved during a multi-frame
      callback are not dropped.
    - The listen host's input sequence is derived from the target frame by the
      scheduling group. A node-side counter that advanced on a frame the host
      did not publish — an eliminated frame taking the override path — would
      break the scheduler's fixed sequence/frame offset and wedge host input
      for the rest of the epoch.
    - `AuthorityInputAdmissionFault` gained `UnknownCombatant` and
      `FrameRunInProgress`, so those stay separable on the telemetry surface
      instead of being flattened into `ForeignScope`.
  - Open items to settle during implementation, raised by the stub review:
    - Name the exact call sites for `RegisterCombatant`, `RemoveCombatant`, and
      `RebuildForEpoch`. `BuildOwnerSchedulingHost` currently runs before
      avatars spawn and before any peer joins, and respawn bumps
      `_lifeGenerations`, which is an epoch change.
    - Decide which frame counter is authoritative. `_simulationTick` increments
      unconditionally once per `_PhysicsProcess`, while V2 may run zero, one, or
      several frames; pose history, snapshots, and lag compensation all key off
      it. V2 should drive it from the completed frame.
    - Choose `capacityFrames` deliberately: it is the acceptance horizon, so
      below the negotiated maximum lead it silently refuses legal commands.
  - Implementation review findings, all fixed. The critic found two blockers,
    both of which the unit tests could not have caught:
    1. `SendOwnerLeadUpdatesV2` and `SendOwnerSchedulingStateV2` were left as
       `NotImplementedException` stubs while `SimulateAuthorityV2` called them
       on every callback that ran a frame, so selecting V2 threw out of
       `_PhysicsProcess` before the first frame completed. Both are implemented.
    2. No production caller routed remote commands into the scheduler, so under
       V2 every remote combatant resolved from fallback and could not move.
       `AdmitRemoteOwnerCommandV2` now bridges the legacy movement frame into an
       owner command at authority ingress. This is explicitly a bridge:
       `OwnerCommandBatchDraft` stays off `PacketEnvelope` v1 until the
       coordinated protocol bump, so the legacy frame is the only remote owner
       input on the wire today.
    Also fixed: respawn advances the life generation, which is inside
    `OwnerIntentScope` and therefore an epoch change, but `AdvanceRespawns` runs
    inside `EndFrame` where rebuilding is refused — epoch rebuilds are now queued
    there and drained before the next frame run. The admission counter folded
    duplicates into refusals, which made a healthy link look broken, because a
    client legitimately resends its recent history in every bundle; counting is
    now by disposition.
  - Added `verify_owner_prediction_v2_smoke.ps1`, wired into the parity gate.
    This is the item that mattered: the V2 path is flag-gated and off by
    default, so every existing gate ran straight past it, which is precisely how
    a stubbed method inside the V2 frame loop survived a fully green parity run.
    The gate selects V2 with `--owner-prediction-v2`, then asserts the scheduler
    accounted for frames, registered both combatants, admitted real remote
    commands with zero faulted refusals, and produced owner state. It caught two
    further defects on its first runs: the V2 branch returned before the legacy
    body's smoke exit so every V2 run hung forever, and the disposition
    miscount. Current result: 75 frames, 2 combatants, 9 remote commands
    admitted, 152 owner states.
  - Verified: Multiplayer 951/951, Core 141/141, Godot build with only the two
    pre-existing nullable warnings, and the full parity gate green including the
    new V2 smoke.
  - Note: the legacy `AuthorityMovementInputBuffer` stays in place and keeps
    driving the Legacy path. This item adds a parallel path; it does not delete
    the old one, which happens only when V2 becomes the default in a later phase.

- [x] **P04-10 — Validate the authority owner state message.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Bound and authenticate `AuthorityOwnerStateDraft` against receive
    context, the way `InboundMessageValidator` already does for the owner
    command/control drafts in P03-12.
  - Target files: `InboundMessageValidator.cs`,
    `InboundMessageValidatorTests.cs`, `ProtocolConstants.cs`.
  - Verification: Valid boundaries pass; fuzzed unknown/oversized/spoofed/stale
    messages fail with stable reasons, including a receive acknowledgement that
    claims an unoriginated input sequence and a tick outside published authority
    time.
  - Planned decomposition, following the established validator shape exactly:
    - `ProtocolConstants` — no new constants needed after all. Planning found
      that `MaxOwnerRecentInputDispositions` and `MaxOwnerJournalEntriesPerBatch`
      already exist and `AuthorityOwnerStateMapper` already sources its bounds
      from them, so the validator reuses the same two and the wire bound stays
      single-sourced. The file drops out of the target list.
    - `AuthorityOwnerStateDraftValidationContext` — expected scope, highest
      authority frame published, earliest retained frame, highest input sequence
      the owner originated, current transition/action resolution cursors, and
      the negotiated lead policy bounds. Mirrors
      `OwnerControlDraftValidationContext`.
    - `InboundMessageValidator.ValidateAuthorityOwnerStateDraft` — presence,
      scope agreement, collection bounds, enum definedness, kind/sequence
      coherence, frame windows, cursor monotonicity, and the context-relative
      checks the P04-08 defects proved are needed.
  - Stub review changes, applied before implementation:
    - The message carries **four** scope copies, not three: the body, the
      receive acknowledgement, the consumption acknowledgement, and the lead
      update. Implemented as three, a spoofed `lead_update.scope` passes.
    - Added known/permitted transition and action identity windows to the
      context, mirroring `OwnerCommandDraftValidationContext`. A resolution
      naming an ID the client never originated reaches the journal's
      unknown-identity branch and forces a full baseline repair — the cheapest
      amplification vector in the message, and context-relative, so only this
      validator can catch it.
    - Added upper bounds on both resolution cursors plus a per-entry coherence
      check against the advertised `latest_*_resolution_sequence`. An inflated
      cursor is permanent poison: the client then discards every genuine
      resolution as stale.
    - Added `EarliestPermittedLeadEffectiveTick`. Checking the effective frame
      only against published authority time is weaker than the P03-08 client
      gate, which also requires it beyond the notice bound and any scheduled
      command, so the validator would have admitted updates the gate refuses.
    - Added a represented-frame/consumed-cursor coherence rule, which the
      original check list left undecided.
    - Corrected the opening remark: this validator owns every check including
      the message-alone ones. `AuthorityOwnerStateMapper.FromProtocol` enforces
      many of the same rules but *throws*, and on an ingress path a hostile
      packet must yield a classified violation, so validation runs first and
      the mapper becomes a redundant backstop.
  - Blocking note: P04-09 must not route this message on a real transport before
    this item lands. The two receive-window defects found during P04-08 were both
    context-relative violations, which is exactly the class only this validator
    can catch. Ordered after P04-09 because it is discovered work, but either
    order is fine provided the routing constraint holds.
  - Carried-forward constraints, gathered from P04-05 through P04-08:
    1. Debounce `PredictionLeadController`'s `RebaseRequired`. It repeats on
       every evaluation while its condition persists, and a rebase is heavier
       than a lead update. `AuthoritySimulationClock` already latches its own
       reset; this is the other half.
    2. Rebuild the scheduler and the in-memory publisher together on any epoch
       change. A host command reaching a publisher whose scheduler has already
       moved on is hard-rejected, which is correct and matches what a remote
       client's stale-epoch batch already does.
    3. A buffer that sits chronically thin without ever starving gets no
       proactive lead lengthening; it waits for one real starvation event.
    4. Do not route `AuthorityOwnerStateDraft` on a real transport until P04-10
       lands. Its decode path fails closed on everything checkable from the
       message alone, but nothing yet validates it against receive context.

## Phase 5 — Explicit-State Kinematic Motor

### Phase plan (recorded August 15, 2026, before any code)

**What is actually being replaced.** Today `MovementRuntimeState` holds velocity,
facing, and mode/jump/roll state, while *position and all collision* live on the
Godot `CharacterBody3D` and are resolved by `MoveAndSlide`. That split is why
owner prediction cannot replay: half the state is inside an engine node that
only moves when the node moves. Phase 5 moves position, contacts, collision
profile, and external movement sources into replayable value state, and replaces
`MoveAndSlide` with an explicit capsule motor driven from that state.

**What is deliberately preserved.** The accepted *feel* — acceleration curves,
run/sprint, air control, jump hold/coyote/buffer/apex/fast-fall, roll distance
and timing — already lives in `GroundLocomotionSimulator`,
`AirborneLocomotionSimulator`, `JumpFallSimulator`, and `CrouchRollSimulator`.
Phase 5 reuses those rules and changes only how the resulting motion is
integrated against the world. P05-12 through P05-15 are compositions, not
rewrites, and their gate is that existing golden curves still match.

**Layering.**

1. *Value state* (P05-01..04, `BattleArena.Core`, no engine dependency). All
   value structs rather than the current `sealed record`, because replay copies
   them every frame and per-frame allocation is what the Phase 1 probes bound.
   - `CharacterKinematicState` — position, velocity, facing, grounded, ground
     normal, support identity.
   - `CollisionContactState` — one stable contact fact; ordering key for
     deterministic slide resolution.
   - `CollisionProfileState` — standing/crouching/rolling by stable identity and
     validated capsule dimensions.
   - `MovementSourceState` + `MovementSourceBuffer` — fixed-capacity replayable
     lunge/roll/dash/knockback/pull curves.
   - `CharacterSimulationState` — the complete rewind unit composing all of the
     above plus the existing mode/jump/roll fields and deterministic counters.
     Authority-only hit and damage state is structurally excluded.
2. *Collision contracts* (P05-05..06).
   - `ICharacterCollisionWorld` — sweep, ground probe, clearance, support
     motion, all taking an explicit transform. No node movement, no
     presentation.
   - `CharacterCollisionContracts` — request/result values and a deterministic
     fake world for tests.
   - `GodotKinematicCollisionWorld` — the adapter, built on the precreated
     profile RIDs and static-only masks the P01-09 probe already proved feasible
     at ~10.8 microseconds per query.
3. *Capsule motor* (P05-07..11, `CapsuleMovementSimulator`). Sweep and
   penetration recovery, stable slide resolution, slope classification and
   ground snap, explicit stair/step solving, ceiling and profile clearance.
   Ordering is stable by fraction, then collider, then shape, then normal, so
   the result never depends on engine iteration order.
4. *Locomotion composition* (P05-12..15, `CharacterMovementSimulator`). Drives
   the existing rule simulators through the explicit motor, then applies
   external movement sources in canonical frame order.
5. *Harness* (P05-16..17). Dual-motor switch in the offline movement arena, and
   a golden suite covering the full course plus 10,000-frame restore/replay
   reproducibility.

**Ordering constraint.** Groups 2-5 all depend on group 1's types, so P05-01..04
is stubbed first and the rest follow. Within the phase the stub pass is one
sweep, then a single stub critic, then implementation.


### Stub review outcome (one critic over the whole phase)

Six blockers and fourteen majors, all contract-level and all cheap to fix before
implementation. The most consequential:

- **The composition could not drive the accepted rules at all.** Velocity and
  facing live only in `CharacterKinematicState`, but `WithRuntimeState` was
  documented to leave the kinematic state untouched, which would have meant the
  character never moved. Fixed, and the round trip is now explicit about which
  fields cross in each direction.
- **No path for input edges.** The rule simulators read jump/crouch press and
  release, but `CharacterSimulationInput` carries held state only. The critic
  proposed storing the previous frame's held bits; that remedy was rejected,
  because it reintroduces exactly the transient-bit fragility P03-05 exists to
  eliminate. The design routes edges through durable transitions and the redesign
  anticipates a compatibility mapper for tick-local bits, so `OwnerInputEdgeMapper`
  now derives edges from applied transition references. That is also strictly
  better for replay: an edge is present on the frame its transition applies, so a
  restored frame reproduces it without needing the prior frame at all.
- **No path for movement influence.** Accepted attack feel gates sprint,
  momentum, acceleration, and steering off the active attack step, and nothing in
  the rewind unit could say which step was active. Added `CharacterActionState`
  — correlation and phase only, never an outcome.
- **Movement sources would have double-counted.** The contribution had no stated
  lifetime, and the obvious implementation persists it into velocity, so the next
  frame re-evaluates the same curve on top of it and a lunge accelerates every
  frame. Now pinned: sources apply to displacement only and are never persisted.
- **Penetration recovery had no query it could use.** A sweep needs a direction
  and reports an undefined fraction at zero motion, which is exactly the recovery
  case. Added `ResolveOverlap`, returning separation direction and depth.
- **Zero per-frame allocation was unreachable.** `MovementRuntimeState` was a
  record class, so every `with` in the rule simulators heap-allocated, multiplied
  by history depth and combatant count. Converted to a `readonly record struct`;
  the `with` expressions compile unchanged, all 1,092 tests still pass, so the
  accepted rules are preserved verbatim.
- **The canonical frame order was wrong.** Profile resolution sat before the
  crouch/roll rules, but those rules *produce* the profile intent, so a frame
  entering a roll would have swept the standing capsule and committed the rolling
  one. Reordered to probe clearance, run the rules, then commit the profile
  before the move.

Also applied: capsule dimensions and motor policy are now revisioned content
rather than singletons, so a replayed frame uses the tuning in force on it;
sweep results carry achieved and remaining motion as vectors rather than a scalar
fraction, because the engine folds depenetration into travel; `MovementSourceBuffer`
implements `IEquatable` so the record struct holding it does not fall back to
reflection-based equality; source capacity raised from 8 to 16 to match the
design's stated wire bound; `Simulate` returns a result carrying motor
diagnostics, which the golden suite and Phase 6 reconciliation both need; the
accepted crouch speed scaling and sprint/jump stripping currently stranded in the
Godot driver were given a home; and the capsule origin is documented as a foot
position, correcting a comment that would have sunk characters into the floor.

Deferred deliberately, recorded rather than fixed: `SupportIdentity` to Godot
`Rid` mapping and the shape-granularity limit of body exclusion (P05-06 decides
it against the real API); and P05-11's player-clearance acceptance, which cannot
be met by a static-only world and belongs with Phase 9 frame-aligned collision.


### Implementation review outcome (one critic over the whole phase)

One blocker, six majors. The blocker was the one that changed how the game feels.

- **The motor never folded collision-resolved velocity back into state.**
  `SweepAndSlide` projected the frame's *motion* onto each contact plane but left
  velocity untouched, so a character pressed into a wall kept full speed forever.
  The accepted driver got this for free by reading body velocity back after
  `MoveAndSlide`, and that readback *is* the deceleration. Consequences went well
  past feel: `JumpFallSimulator` adds a bonus proportional to horizontal speed,
  so jumping into a wall granted the full bonus for speed the character did not
  have, and `CrouchRollSimulator` gates roll entry and boost distance on the same
  value, so a character pinned to a wall could roll at full boost from a
  standstill. Velocity is now projected onto the same planes as the motion.
  Two tests lock it, and neither existed before: the whole suite asserted on
  position and never once on velocity.
- **Landing did not clear downward velocity**, so a landing roll carried the
  entire fall speed for its duration and leaving a ledge mid-roll dropped
  instantly. Same root cause; fixed alongside, with its own test.
- **The dual-motor arena rendered from the motor that was not running.**
  Presentation, animation, camera offset, and the diagnostics label all read the
  legacy driver directly, so under the explicit motor every one of them was
  frozen at spawn — which made the arena useless for the single thing P05-16
  exists for. They now read the active motor through one accessor. `ResetToSpawn`
  also failed to reseed the explicit state, so falling below the reset height was
  an unrecoverable loop.
- **The walkable-slope threshold was mutable adapter state** read during
  `Simulate`, outside the rewind unit and outside the revision. It now travels on
  `CapsuleSweepRequest`, so a replayed frame classifies contacts against the
  revision it was simulated under.
- **The golden suite asserted only end states**, so a divergence that appeared
  mid-trace and re-converged would pass. Restore-and-replay now compares every
  frame. The two-run test is documented as the weaker of the two — over a pure
  function it is close to true by construction, and what it actually proves is
  that nothing holds hidden per-instance state.
- Motor fixes: stepping is now attempted when the slide iteration cap is hit
  (a busy corner at the foot of a staircase is exactly where stepping matters);
  the step down-probe is bounded by how far the up-probe actually rose rather
  than the full step height; blocking-contact selection skips contacts the motion
  is travelling away from instead of abandoning the frame; the step solver's
  remaining motion is measured from the post-recovery position so a depenetration
  push is not mistaken for spent motion; and overlap at or below an authored
  margin depth is ignored, because a resting capsule legitimately reports
  margin-scale overlap and recovering it every frame would fight ground snap.


- [x] **P05-01 — Define compact kinematic and contact state.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Own position, velocity, facing, grounded, normal, support behavior,
    and stable contact facts in replayable value state.
  - Target files: `CharacterKinematicState.cs`,
    `CollisionContactState.cs`, `CharacterKinematicStateTests.cs`.
  - Verification: Finite-value, tolerant-contact, seam-equivalence, and copy
    semantics pass without per-frame allocation.

- [x] **P05-02 — Define collision profile state and bounds.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Represent standing/crouching/rolling profiles by stable identity and
    validated dimensions.
  - Target files: `CollisionProfileState.cs`,
    `CollisionProfileStateTests.cs`.
  - Verification: Profile validation and legal shrink/expansion intent tests pass.

- [x] **P05-03 — Define bounded movement-source state.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Store replayable lunge, roll, dash, knockback, pull, and future item
    motion in a fixed-capacity value buffer.
  - Target files: `MovementSourceState.cs`, `MovementSourceBuffer.cs`,
    `MovementSourceBufferTests.cs`.
  - Verification: Capacity, stable ordering, lifecycle, copy, aggregation, and
    no-allocation tests pass.

- [x] **P05-04 — Define the aggregate character simulation state.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Combine movement, action, contact, profile, sources, revisions, and
    deterministic counters into the complete rewind unit.
  - Target files: `CharacterSimulationState.cs`,
    `CharacterSimulationStateTests.cs`.
  - Verification: Complete-copy/equality tests prove no prediction-relevant field
    is omitted and authority-only hit/damage state cannot be stored.

- [x] **P05-05 — Define query-neutral collision contracts.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Isolate sweep, ground probe, clearance, and support motion from Godot
    nodes and presentation.
  - Target files: `ICharacterCollisionWorld.cs`,
    `CharacterCollisionContracts.cs`, `CharacterCollisionContractsTests.cs`.
  - Verification: Contract validation and deterministic fake-world tests pass.

- [x] **P05-06 — Implement the static Godot query adapter.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Execute explicit-transform capsule queries with precreated profile
    RIDs, static-only masks, exclusions, and reusable buffers.
  - Target files: `GodotKinematicCollisionWorld.cs`,
    `GodotKinematicCollisionWorldTests.cs`, `kinematic_query_probe.tscn`.
  - Verification: Headless probe proves no live-node movement, no dynamic hits,
    reusable results, and same-frame query/commit behavior.

- [x] **P05-07 — Implement bounded sweep and penetration recovery.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Resolve desired capsule travel and recover legal shallow overlap from
    explicit state.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleSweepSimulatorTests.cs`.
  - Verification: Free travel, wall impact, high speed, starting overlap, bounded
    iterations, and unrecoverable penetration pass.

- [x] **P05-08 — Implement stable slide resolution.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Resolve multiple contacts in stable fraction/collider/shape/normal
    order without node iteration dependence.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleSlideSimulatorTests.cs`.
  - Verification: Wall slide, convex/concave corner, reorder, duplicate contact,
    and iteration-cap traces pass.

- [x] **P05-09 — Implement slope classification and ground snap.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Reproduce walkable/unwalkable slopes, explicit ground probing, and
    no snap while rising.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleSlopeAndSnapTests.cs`.
  - Verification: Slope thresholds, descent, edge departure, rising jump, seam,
    and normal tolerance tests pass.

- [x] **P05-10 — Implement explicit stair/step solving.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Generalize up-forward-down stepping for direct and strafing entry
    without changing the map to hide lips.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleStepSolverTests.cs`.
  - Verification: Thin/thick ramps, every stair direction, shallow lips, blocked
    headroom, and no-progress rejection pass.

- [x] **P05-11 — Implement ceiling and profile-clearance rules.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Stop upward motion on ceilings and allow profile expansion only when
    static/player clearance permits it.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleClearanceTests.cs`.
  - Verification: Ceiling hit, crouch/roll shrink, blocked stand, delayed stand,
    and forced-expansion policy pass.

- [x] **P05-12 — Compose ground and airborne locomotion.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Reuse accepted acceleration, run/sprint, momentum, air-control, and
    falling rules through the explicit motor.
  - Target files: `CharacterMovementSimulator.cs`,
    `CharacterLocomotionIntegrationTests.cs`, `CapsuleMovementSimulator.cs`.
  - Verification: Existing movement golden curves match accepted tolerances.

- [x] **P05-13 — Integrate jump state and durable transitions.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Preserve variable hold, coyote, buffering, apex, fast fall, momentum,
    lateral control, and air sprint under restore/replay.
  - Target files: `CharacterMovementSimulator.cs`,
    `CharacterJumpReplayTests.cs`, `JumpFallSimulator.cs`.
  - Verification: Restore/replay from every jump frame yields the same final
    canonical state.

- [x] **P05-14 — Integrate crouch and roll state.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Preserve hold-to-crouch, moving roll, tap/hold duration, momentum,
    steering, cooldown, landing roll, and non-cancelability.
  - Target files: `CharacterMovementSimulator.cs`,
    `CharacterRollReplayTests.cs`, `CrouchRollSimulator.cs`.
  - Verification: Restore/replay and accepted roll-distance/timing traces pass.

- [x] **P05-15 — Integrate replayable external movement sources.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Apply source curves in the canonical frame order instead of mutating
    node velocity once.
  - Target files: `MovementSourceSimulator.cs`,
    `MovementSourceSimulatorTests.cs`, `CharacterMovementSimulator.cs`.
  - Verification: Start/progress/stack/end, F-start lunge, and F+1 hit-knockback
    traces pass.

- [x] **P05-16 — Add the dual-motor offline test adapter.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Let the existing movement arena switch between Legacy and
    ExplicitQueryMotor without changing accepted content values.
  - Target files: `MovementTestPlayer.cs`, `movement_test_arena.tscn`,
    `MOVEMENT_TEST_ARENA.md`.
  - Verification: Both modes launch headlessly and the selected mode is visible
    in diagnostics.

- [x] **P05-17 — Lock the explicit-motor golden suite.**
  - Status: **Implemented And Reviewed**.
  - Purpose: Cover the complete arena course and 10,000-frame restore/replay
    reproducibility before networking cutover.
  - Target files: `ExplicitMotorGoldenTraceTests.cs`,
    `MovementGoldenTraceData.cs`, `run_movement_golden_tests.ps1`.
  - Verification: All accepted run/sprint/jump/roll/stair/ramp/wall/ceiling traces
    and every replay pivot pass with zero unexplained divergence.


- [ ] **P05-18 — Supply attack movement influence to the explicit motor.**
  - Purpose: `CharacterMovementSimulator.InfluenceFor` returns null
    unconditionally. The plumbing is complete — `CharacterActionState` carries
    the step index through restore — but the authored policy mapping a step to
    its influence is combat content owned by P08-01. Today `NetworkAvatar`
    passes a live influence into the legacy driver, so the explicit path loses
    accepted attack-movement feel the moment V2 becomes the movement source.
  - Target files: `CharacterMovementSimulator.cs`, `CharacterActionState.cs`.
  - Verification: Sprint gating, momentum preservation, and the acceleration and
    steering multipliers reproduce the legacy driver's behaviour for each
    authored attack step, under restore and replay.

## Phase 5B — Godot Motor Integration and Engine Verification

**Executed as the first subsection of Phase 6.** 5B and Phase 6 share one
plan/stub/review/implement/review cycle and one phase commit. They are kept as
separate headings because 5B verifies the motor against the engine while Phase 6
builds reconciliation on top of it, but running them as one unit is deliberate:
P5B-01 blocks P06-11, and P5B-02's motor-parity trace is the only thing that
would catch the explicit motor drifting from accepted feel before reconciliation
starts masking it as a correction.

The explicit motor is proven against a deterministic fake world and the offline
arena. Nothing has yet proven it behaves the same way against the real engine,
and the adapter that connects the two has neither a test nor a probe. This phase
closes that gap.

It is numbered 5B rather than inserted as a new number because Phases 6 through
12 are referenced by number throughout this checklist and the redesign document;
renumbering them would invalidate every one of those references for no benefit.

**Scope boundary.** This phase verifies the *query adapter and the motor against
the real engine*. Wiring the motor into multiplayer is already owned elsewhere:
P06-10 adds the commit-once Godot owner adapter and P06-11 integrates V2 owner
prediction into `NetworkArena`. Engine-side performance budgets belong to P11-04.
This phase is the prerequisite all three of those depend on.


### Phase plan (recorded August 16, 2026, before any code)

Phase 5B and Phase 6 are planned, stubbed, reviewed, implemented, reviewed, and
committed as one unit. 5B is the first subsection.

**What this unit delivers.** Phase 5 made a character's frame replayable. This
unit makes *prediction* work: the owner keeps a bounded history of its own
frames, compares each one against the authority's answer when it arrives,
decides whether that difference matters, and replays only what it must. Phase 4
already delivers the authority's answer; Phase 5 delivers a simulation that
reproduces a frame exactly. Neither is useful until this connects them.

**Why the order is what it is.**

1. *Engine verification first (P5B-01..03).* Everything below trusts the motor
   to behave identically in-engine. Verifying that after building reconciliation
   on top of it would mean any divergence has two candidate causes, and the
   expensive one — reconciliation — would be searched first.
2. *Storage and comparison before policy (P06-01..04).* A correction policy is
   only as good as the comparison feeding it, and comparison needs somewhere to
   compare against. Contacts land in history here, which is why P05-18 is a
   prerequisite of P06-01 rather than optional cleanup.
3. *Reconciliation before wire (P06-05..07 before P06-08..09).* The protocol
   should encode what reconciliation actually needs, not what seemed likely
   beforehand. Finalizing the wire first would freeze a guess.
4. *Engine adapter last (P06-10..12).* Committing to nodes is the one step that
   cannot be unit-tested, so everything testable happens first.

**Layering.**

- *Engine verification* (P5B-01..03, `scripts/movement/`): adapter probe, a
  legacy-versus-explicit motor parity trace, and a real per-frame query cost
  measurement.
- *History and comparison* (P06-01..04, `BattleArena.Multiplayer`):
  `OwnerPredictionHistory` as a preallocated frame-indexed ring;
  `CanonicalMovementStateHash` producing versioned diagnostics from quantized,
  explicitly ordered fields; `OwnerReconciliationComparer` separating discrete
  mismatches from numeric tolerance; `OwnerReconciliationPolicy` choosing
  confirmed, ordinary replay, contact replay, or hard rebase.
- *Reconciliation* (P06-05..07): tick-effective configuration lookup, then
  `LocalMovementPredictionController` performing restore-and-replay and the
  queued-future and hard-rebase paths.
- *Wire* (P06-08..09): finalize the owner baseline and the compact
  collision-world state, both derived from what reconciliation proved it needs.
- *Engine* (P06-10..12): commit-once owner adapter, static-only V2 integration in
  the arena, and separate-process canonical trace parity.

**The rule this unit exists to enforce.** A correction is a claim that the
owner's simulation was wrong. It must be triggered by a real, classified
difference — never by a hash alone, never by a tolerance the comparer cannot
name a field for, and never by smoothing collision truth. An unnecessary
correction is a visible snap the player did nothing to deserve.


### Stub review outcome (one critic over the merged unit)

Four blockers and fifteen majors. The largest was that this unit re-invented a
taxonomy Phase 1 already shipped.

- **`OwnerCorrectionAction` and `OwnerRebaseReason` duplicated and contradicted
  P01-02.** `OwnerCorrectionAction` was a value-for-value copy of
  `OwnerCorrectionDisposition`, and `OwnerRebaseReason.EpochChanged` collapsed
  the four lifecycle causes that P01-02 deliberately keeps separate — a
  separation a critic specifically required before approving that item. It also
  invented `AuthorityDemanded`, which `OwnerCorrectionTelemetry.Classify` would
  throw on, while omitting `ConfigurationHistoryPolicyExhausted`, which P06-05
  needs. Both new enums are deleted; the decision now carries the Phase 1 types.
- **The difference reported a `string` field name** where `OwnerMismatchField`
  already enumerates twenty-two fields. The consequence was sharp: the policy
  could not structurally tell `ContactReplay` from `OrdinaryReplay`, which is its
  entire job. It now carries the enum plus `OwnerCorrectionError`, which also
  supplies the five separate magnitude axes telemetry requires — a single
  position scalar could not express that a vertical error near a jump apex is
  more visible than the same error in plan.
- **P05-18 was a prerequisite scheduled nowhere.** History stores contacts and
  the comparer compares them, but Phase 5 left contacts out of the rewind unit.
  It is now P6-00 and runs first.
- **Three Godot probe classes shared one file**, so two of them could not be
  attached to a scene, and none carried the `GetTree().Quit(exitCode)` contract
  every existing probe runner keys on. Split per repo convention.

Also applied: applied transitions and simulation events move into
`OwnerPredictedFrame` rather than being re-derived at replay time — authority can
remap a transition to a later frame, so re-deriving is exactly how first run and
replay come to disagree, and the events are what `PredictedCueLedger` needs to
keep a replayed jump from firing ten sounds. `TryReplacePostState` was added
because the corrected frame otherwise keeps the owner's wrong answer and
mismatches again on a duplicate authority state. The policy now receives a
difference and a motion outcome instead of the predicted frame, so it is
physically unable to read a canonical hash — the rule the unit exists to enforce
is structural rather than documented. The comparer takes the authority's hash so
`DiagnosticHashOnly` is producible, and the hash takes an epoch because the
design's field list includes identities `CharacterSimulationState` deliberately
excludes. `OwnerPredictionWorkPolicy` bounds replay depth and queued futures
separately from retention, because retention decides how far back a correction
may reach and that is not the same as how much work one engine frame may do. The
epoch gate and cue ledger are injected rather than duplicated, and the adapter
publishes through `CharacterPresentationController` — which already refuses a
replay pass structurally — rather than reimplementing presentation.

Confirmed by the review and kept: deferring P06-08/09/12 was right, and the
review proved it — three wire-shaping discoveries would otherwise have been
frozen wrongly. Running 5B first is right, with `VerifyAgreesWithDeterministicWorld`
the load-bearing case, since it converts every motor unit test against the fake
into evidence about the real engine.

- [x] **P6-00 — Store contact facts in the rewind unit.**
  - Status: **Implemented And Reviewed.** The checkbox was stale — the work landed
    in `dc24b53` and the implementation critic flagged the mismatch.
  - Coverage landed in `CharacterMovementSimulatorTests.cs`
    (`ContactsAreRecordedAndSurviveRestoreAndReplayIdentically`,
    `TheSupportingSurfaceIsAmongTheRecordedContacts`) rather than the
    `CharacterSimulationStateTests.cs` named below, because the property worth
    asserting is that contacts survive a real restore-and-replay through the
    simulator, not that a struct round-trips.
  - Purpose: P05-01 promised "stable contact facts in replayable value state",
    and `CollisionContactState` exists only as scratch inside the motor.
    Contacts are derivable, so this is not a determinism break — but P06-01
    stores contacts in history, P06-03 compares them, and P06-04 keys
    `ContactReplay` on them, so all three would otherwise reopen the rewind
    unit, which is a protocol change.
  - Target files: `CharacterSimulationState.cs`, `CharacterMovementSimulator.cs`,
    `CharacterSimulationStateTests.cs`.
  - Verification: Contacts survive restore/replay identically and the bounded
    storage allocates nothing per frame.
  - Note: moved here from Phase 5, where the stub review found it scheduled
    nowhere despite the plan naming it a prerequisite of P06-01. It runs before
    P06-01 rather than after 5B, because it changes the rewind unit and 5B's
    probes measure the motor against it.

- [x] **P5B-01 — Verify the Godot collision adapter in-engine.**
  - Status: **Implemented And Reviewed.** Three engine defects found and all
    three fixed. Gate wired into `verify_multiplayer_parity.ps1`.
  - Findings from the first in-engine runs, which is what this item exists for.
    Every one was invisible to the 200-test unit suite because that suite runs
    against the deterministic reference world:
    1. **A walkable-ground contact at travel fraction zero cancelled the entire
       frame's motion.** Every grounded frame contacts the floor at fraction
       zero, because the driver proposes a small downward velocity to stay
       stuck to it. Walkable ground is not "blocking", so the slide loop broke
       and discarded all remaining motion — horizontal included — and the
       character could barely walk. Fixed: the loop now projects against the
       earliest contact the motion is driving into, blocking or not, so the
       floor removes only the downward component. Regression test added; the
       unit suite missed it because every existing test passed zero vertical
       motion.
    2. **The surface skin exactly equalled the engine's query margin**, both one
       millimetre, so a resting capsule sat *inside* the margin and every sweep
       re-reported the floor — including sweeps of purely horizontal motion the
       floor does not obstruct. Measured cost: a walking character covered 3.378
       m in-engine where the reference covered 3.600 m, six percent slower.
       Fixed by raising the skin to five millimetres, with the
       skin-exceeds-margin relationship documented as the invariant it is.
    3. **A character was permanently stuck on a 0.25 m step in-engine.** Fixed.
       This was the most serious of the three and the hardest to see, so the
       wrong first diagnosis is kept here deliberately.

       *The diagnosis first recorded here was wrong.* It claimed the up-sweep
       started already overlapping because `ResolveOverlap` probes downward and
       so misses horizontal overlap. Instrumenting the adapter's raw answers
       disproved that in one run: the up-sweep achieved its full 0.4 m and the
       raised forward sweep its full 0.06 m. Nothing was overlapping. The
       lesson is that the oscillating trace was equally consistent with two
       different causes, and I picked one by plausibility rather than measuring
       — the instrumented run cost less than the reasoning that preceded it.

       The real cause is capsule geometry. A capsule's bottom is a hemisphere,
       so walking into a step it contacts the step's top **edge** while its axis
       is still radius-scale short of the face: exactly
       `sqrt(r² − (r − h)²)` = 0.371 m for a 0.4 m radius against a 0.25 m step,
       which is precisely where the trace stalled (X ≈ 1.629 against a face at
       X = 2.0). An edge normal is steeper than any walkable limit — measured
       (−0.801, 0.599, 0), or 53°, against a 45° limit — so `SolveStep`'s
       down-probe classified the landing as `UnwalkableSlope` and refused the
       step, every frame, forever. `ResolveGrounding` rejected the same corner,
       so the character was also considered airborne while resting on it: it
       rode the corner up about two centimetres, was declared unsupported,
       fell back, and repeated.

       Fixed by probing the step-solver's forward sweep at least a capsule
       radius even when the frame's own motion is far shorter, so the down-probe
       is taken where the axis has cleared the face and reports the step's flat
       top. The **commit** still advances only the distance the frame earned —
       committing the probe distance would teleport the character up to a radius
       forward whenever a step came into range — and the committed landing is
       re-checked for clearance, since the probe position cleared and the commit
       sits behind it.

       Four unit tests lock this in (`CapsuleStepCornerTests`), against a world
       that reproduces the corner geometry. Two of them fail without the fix;
       the other two are guards that the wider probe does not turn a wall into a
       step or commit into geometry, and they pass either way by design. That
       split is deliberate — a regression test that passes before the fix is
       decoration, and I verified which was which by reverting the fix and
       re-running.
  - **Why 201 unit tests could not see any of this.** All three defects live at
    the engine boundary, and the reference world is a box. Specifically for the
    step: a box's flat bottom reports the step's flat top face the instant it
    overhangs, so it climbs in a single frame and every step assertion in the
    suite has been asserting *box* behaviour. This is a standing limitation of
    the reference world rather than something now fixed — it will keep
    disagreeing with the engine wherever capsule roundness matters, such as
    convex corners and ledge edges. P5B-02's parity trace against the legacy
    motor in the real arena is what covers the rest.
  - **Latent gap recorded, deliberately not fixed here.** The adapter ignores
    `ExcludedCollider` in both `Sweep` and `ResolveOverlap`, while the reference
    world honours it and the interface documents it. Nothing is affected today
    because the motor passes `SupportIdentity.None` at all four call sites. It
    is not fixed now because the obvious implementation — populating
    `PhysicsTestMotionParameters3D.ExcludeBodies` — allocates a Godot array per
    query on the hot path, and whether that is affordable is exactly what
    P5B-03 measures. Fixing it blind would trade a dormant bug for a per-frame
    allocation in the replay loop.
  - Also changed: the deterministic collision world moved from the test project
    into `BattleArena.Core`. The probe must compare against the *same* reference
    the motor's unit tests use — a copy could drift, and the agreement check
    would then prove only that the engine matches a fake nobody tests against.
  - The agreement check asserts behaviour rather than matching positions frame
    by frame. The reference world approximates the capsule as an axis-aligned
    box, so at a step edge the two differ by construction; demanding positional
    parity would mean tuning the assertion until it passed rather than learning
    anything. It compares open-ground travel rate, that a wall stops both, and
    that a step is climbed by both.
  - Purpose: `GodotKinematicCollisionWorld` has no test and no probe. P01-09
    proved the query *approach* but exercised a different class, and the adapter
    added a path the probe deliberately never ran: `ResolveOverlap` uses
    `RecoveryAsCollision = true`, which the probe kept disabled throughout, and
    it runs before every frame's motion.
  - Target files: `GodotKinematicCollisionWorldProbe.cs`,
    `kinematic_collision_world_probe.tscn`,
    `run_kinematic_collision_world_probe.ps1`.
  - Verification: A headless probe proves no live-node movement, no dynamic
    hits, correct foot-versus-centre capsule offset, stable collider identity,
    reusable results, explicit `Margin` and `CollideSeparationRay`, and that a
    resting capsule does not oscillate between penetration recovery and ground
    snap.
  - The probe's strongest case is the one added last: walking the whole course
    under the real `CharacterMovementSimulator`, in-engine and in the reference.
    Every other case drives `CapsuleMovementSimulator` directly with a
    hand-supplied vertical motion, which cannot distinguish "the capsule cannot
    climb" from "the harness pulled it back down each frame". That ambiguity is
    what made the step defect look like a harness artifact; the driver run
    settled it, showing the engine stuck at X=1.628 where the reference reached
    X=5.6. Both now reach X=5.6.
  - **The implementation critic found the step fix itself was still wrong, and it
    was right.** The first version probed a capsule radius ahead to find the
    step's walkable top, then committed the frame's much shorter motion at *that*
    height. For the exact geometry the fix was written for, the character was
    placed at X = 1.669 with Y = 0.25 while the real ground there is Y = 0 — 0.25 m
    in the air, 0.33 m behind the step face, reporting the step as its support
    while resting on nothing. `ResolveGrounding` then re-probed, found the same
    53-degree edge, and returned airborne. So the climb worked by "float, be
    declared airborne, fall, repeat" rather than by stepping, and `IsGrounded`,
    `GroundNormal` and `Support` flickered on every frame of a step approach.
    Those are exactly the discrete fields P06-03 compares outside numeric
    tolerance and P06-04 keys a contact-replay correction on, so the first fix
    manufactured a per-frame discrete mismatch on every staircase.
  - Fixed by validating each candidate landing where it will actually be
    committed (`TryLandOn`): ground present, walkable, strictly above the frame's
    start height, and clear. The earned position is preferred, and only when it
    has no ground does the commit advance to the probe position where ground was
    actually found — bounded by one capsule radius. A single probe taken at one
    position and used to justify committing at another is the mistake, and it is
    now structurally impossible.
  - The height-gain requirement also fixes a misreported outcome the critic
    spotted: a sweep into a real wall still achieves the engine's query margin,
    which exceeds the motion epsilon, so the forward-progress gate alone did not
    stop a wall being reported as `Stepped`. `CapsuleMotionOutcome` is a canonical
    comparison field, so a misreported outcome is a divergence.
  - Two of the four regression tests had to be rewritten because they encoded the
    float: `TheStepCommitsOnlyTheMotionTheFrameEarned...` literally asserted the
    bad committed position. They now assert the property instead — the world is
    asked what lies beneath wherever the character was placed. That is the risk of
    writing a test from the implementation rather than from the requirement, and
    it is worth recording that the tests passed while the behaviour was wrong.
  - **Known regression introduced by the widened probe, recorded not fixed:** a
    climbable ledge shallower than one capsule radius plus skin (about 0.425 m
    deep) with a drop behind it can no longer be stepped onto, because the forward
    probe overshoots its top and the down-probe finds nothing. Nothing in the
    arena hits this today. Closing it needs the probe to sample at more than one
    distance, which is more per-frame queries, so it belongs with the cost budget
    P5B-03 established rather than being added blind.
  - **Two further findings recorded rather than fixed here**, both real and both
    needing more care than the end of this phase allows:
    1. *The adapter gives every reported contact the same travel fraction.*
       `CollisionContactState.CompareForStableResolution` sorts on travel fraction
       first, so in-engine the primary key is constant and the real ordering
       becomes the tie-break — a raw physics-server RID. `FirstOpposing` therefore
       returns the lowest-RID opposing contact rather than the earliest one, and in
       a floor/wall concave corner which surface wins depends on scene
       instantiation order. That is precisely the kind of difference P06-12's
       cross-process trace parity would be asked to explain. Fixing it means
       changing the comparer's primary key to something the adapter can actually
       supply, which is a determinism change touching every motor test.
    2. *`ReportQuerySettings` prints a throwaway parameters object's defaults*
       rather than the adapter's own `_parameters`, so it cannot detect the adapter
       changing its margin — the drift it exists to make visible. `Margin` is also
       never set explicitly anywhere in the adapter, and `CollideSeparationRay` is
       not reported at all, both of which this item's verification text promises.
  - Blocking note resolved: this has landed, so P06-11 is unblocked.

- [x] **P5B-02 — Prove the explicit motor matches the legacy motor in the arena.**
  - Status: **Implemented And Reviewed.** Gate green and wired into
    `verify_multiplayer_parity.ps1`.
  - **Result: the motors agree on feel, closely.** Measured over the real arena
    course: top run speed 6.000 vs 6.000 m/s, top sprint 12.500 vs 12.500,
    frames to 95% run speed 21 vs 21, braking distance 0.513 vs 0.513 m, roll
    distance 10.090 vs 10.090 m, jump apex 3.970 vs 3.975 m, ledge height gained
    0.307 vs 0.302 m, crouched tunnel travel 7.244 vs 7.243 m, standing tunnel
    travel 1.580 vs 1.581 m, obstacle forward travel 16.289 vs 16.193 m,
    obstacle lateral deflection 0.573 vs 0.572 m. Jump airtime is the loosest at
    64 vs 61 frames.
  - **The implementation critic found this probe's first version was largely
    measuring open ground, and it was right.** Three of the seven gated metrics
    never touched the geometry they were named for, and each would have passed
    with the corresponding motor behaviour completely broken:
    1. *"Obstacle slide travel" never contacted an obstacle.* Spawned at
       X = -10.55 against 0.55 m cylinders at X = -12 and X = -8 with a 0.42 m
       capsule, which leaves a 0.48 m clear corridor down the whole slalom. It
       measured three seconds of unobstructed running. Worse, it would have been
       *greener* if both motors had tunnelled straight through the cylinders,
       because the two would then have agreed exactly. Now spawned on the
       obstacle line and slightly off its axis — dead-centre produces a head-on
       stop with zero lateral deflection, which the first corrected version
       measured and failed on — and lateral deflection is now asserted, because
       it is the only evidence the obstacle was touched at all.
    2. *The crouch metric ran seven metres from the nearest ceiling.* It compared
       crouched top speed across flat ground and called it clearance; profile
       switching could have been entirely broken. Now run at the arena's low
       tunnel and asserted as a **pair**: crouched travel 7.24 m against standing
       travel 1.58 m. The standing run is what gives the crouched run meaning —
       without it, a motor ignoring the ceiling in both postures shows identical
       travel and passes.
    3. *The "stair climb" gate — written explicitly as the regression guard for
       P5B-01's step defect — ran over a ramp.* Every visible stair in
       `MovementTestCourse` is built with `collisionEnabled: false`; the only
       collider on that path is a smooth 15-degree traversal ramp, so `SolveStep`
       was never invoked and the guard would have stayed green with the step
       solver reverted. Now run against the 0.35 m box ledge at Z = 32, with an
       absolute floor on **both** motors so a shared regression cannot hide
       behind the comparison.
  - **Finding: the motors are exactly one frame out of phase.** Same-frame
    horizontal position disagreed by 0.126 m, which collapses to 0.026 m when
    the traces are compared at a one-frame offset — so the character follows the
    same path, one frame apart, rather than a different path. Ruled out as a
    probe artifact by rewiring the probe to drive the explicit motor exactly as
    `MovementTestPlayer` does (the command's own tick as the step context); the
    offset was unchanged, so it is a property of the motors' integration order.
    Accepted rather than repaired: forcing the explicit motor to phase-match a
    motor that is being retired would be the wrong direction of repair.
  - **This phase difference is a Phase 6 constraint, not just a note.**
    Reconciliation compares an owner's predicted frame against authority's
    answer for the *same* frame. A one-frame phase error anywhere in that path
    reads as a divergence on every frame and would correct the player
    continuously. P06-03 and P06-06 must be checked against this explicitly.
  - The gate therefore separates two questions that a same-frame comparison
    conflates: the *path* is gated at the best alignment, and the *phase* is
    gated at one frame. A two-frame drift, or a path that diverges however it is
    aligned, both still fail. Vertical settling at spawn is reported but not
    gated — the legacy body falls the spawn gap and `MoveAndSlide` stops it while
    the explicit motor snaps to its skin distance, so the first frames differ by
    construction.
  - Defect found in the probe itself and fixed: the first version collapsed four
    fields into one distance magnitude, which cannot tell vertical spawn settling
    apart from a horizontal speed difference. That is the averaging-away this
    item's own verification text warns against, and it made a 1.5 m/s vertical
    settling artifact look like a motor failure. Now reported per field.
  - Further critic findings on this item, all fixed:
    - *The shared-window velocity gate did not exist.*
      `SharedWindowVelocityTolerance` was declared and never read, so of the four
      fields the probe carefully separated, exactly one was enforced. A motor
      producing correct positions with wrong velocities passed silently — which
      is precisely what a broken velocity projection produces, and that
      projection exists because a character pressed into a wall that keeps full
      speed corrupts the jump speed bonus and the roll entry gate. Velocity is
      now gated at the same alignment as position.
    - *Most of the scripted trace was unreachable.* `ScriptedInput` scripted a
      jump at frame 90, a crouch at 130 and a roll at 200, but its only caller
      runs 30 frames. Every branch past frame 30 was dead, which read as far
      broader coverage than existed. The trace is now only what it is — a forward
      walk with sprint — and the behaviour list is covered by targeted stations,
      each spawned at the geometry it needs. One long continuous trace cannot
      deliver that list: by the time it reached its roll the two motors would be
      metres apart for reasons unrelated to rolling.
    - *Jump airtime compared two different definitions of "grounded"* — a
      locomotion mode on the legacy side against the kinematic flag on the
      explicit side, and the mode lags the flag by the rule simulator's
      transition. The tolerance had been set just above that definitional
      mismatch. Both sides now read `LocomotionMode`.
  - **Named accepted difference: jump airtime differs by 3 frames** (64 legacy,
    61 explicit) with the apex agreeing to 5 mm. Real rather than definitional
    after the fix above, and consistent with the one-frame integration phase
    difference plus a grounding-threshold difference at each end of the arc.
    Gated at 4 frames.
  - **Open production issue this item surfaced and could not safely close:
    `RecoverPenetration` scales the depenetration push by `1 + SurfaceSkin`
    instead of adding the skin along the separation direction.** `SurfaceSkin` is
    a distance, so for a 10 mm overlap the correction adds 50 micrometres of
    clearance where 5 mm was intended — an order of magnitude *inside* the
    engine's 1 mm query margin, which is the same class of defect as P5B-01's
    engine defect #2 and contradicts `CapsuleMotorPolicy.SurfaceSkin`'s own
    documented invariant. The obvious correction was implemented and measured,
    and it broke something worse: with an additive skin the character stops dead
    instead of coasting when input is released — **braking distance fell from
    0.513 m to 0.002 m while velocity decayed normally**, meaning position
    integration depends on this value in a way that is not yet understood. It is
    therefore left as-is with the reasoning recorded in the code, rather than
    trading a documented shortfall for an undiagnosed feel regression. The
    braking metric added here is the test that now fails the moment someone gets
    this wrong.
  - Purpose: P05-16 supplies the switch but nothing compares the two motors.
    Both read the same authored `movement.json`, so a scripted input trace run
    through each should produce closely matching motion — and where it does not,
    the difference should be named and accepted rather than discovered later in
    a playtest.
  - Target files: `MovementMotorParityProbe.cs`,
    `run_movement_motor_parity.ps1`, `MOVEMENT_TEST_ARENA.md`.
  - Verification: One scripted trace covering flat running, sprint, a wall
    slide, a stair climb, a jump arc, a crouch passage, and a roll runs under
    both motors headlessly; divergence stays inside an authored tolerance, and
    any excursion is reported with its frame and field rather than averaged
    away.
  - **Design decision recorded before implementing: what "matches" is asserted
    as.** The reviewed stub implied per-frame position parity across the whole
    trace. That assertion cannot hold and would be dishonest to author, so it is
    replaced deliberately rather than quietly tuned:
    1. *Both motors are closed loops over different collision algorithms.* Any
       difference on frame N changes the input state of frame N+1, so divergence
       compounds. Over a 900-frame trace the two will separate by metres no
       matter how correct both are. A per-frame tolerance wide enough to pass
       such a trace is wide enough to hide a real regression, so the number
       would be chosen to make the gate green — which is the failure mode this
       whole probe exists to avoid.
    2. *Re-seeding at segment boundaries was considered and rejected.* Seeding
       the explicit motor from legacy state each segment would keep the
       comparison honest, but only one direction of conversion exists
       (`CharacterSimulationState.ToRuntimeState`). The reverse is lossy —
       runtime state carries no contacts, no support identity, and no movement
       sources — so it would need new production surface built solely for a
       probe, and every reconstructed field would be a guess the comparison then
       depends on.
    3. *What is asserted instead.* Feel-level quantities, which are what a
       player actually perceives and what a tuning regression actually moves:
       top run and sprint speed, time to reach top speed, braking distance,
       jump apex and airtime, stair-climb completion and time, crouch clearance
       height, and roll distance and duration. Each is measured under both
       motors and must agree inside a per-metric authored tolerance, reported as
       a measured pair rather than a pass bit.
    4. *Plus a short shared-spawn window.* The first 30 frames from an identical
       spawn are compared per frame on position and velocity, where divergence
       has not yet compounded. This is what catches a gross immediate
       difference, and it is the part of the original per-frame intent that is
       actually measurable.
  - Scope limit inherited from the stub and still true: the trace contains no
    attack, because attack movement influence is unauthored until P05-18. The
    result means "the motors agree with no attack step active" and must not be
    read as "the motors agree".

- [x] **P5B-03 — Measure the explicit motor's real per-frame query cost.**
  - Status: **Implemented And Reviewed.** Gate green and wired into
    `verify_multiplayer_parity.ps1`.
  - **Result: the budget is crossed between replay depth 12 and 16. The supported
    cap is set to 8 as a deliberately conservative choice.** Budget is 4166.7 us
    per frame, a quarter of a 60 Hz frame. Over the hostile geometry:

    | depth | queries/frame | mean | p95 | share of budget |
    | --- | --- | --- | --- | --- |
    | 1 | 8 | 235 us | 314 us | 8% |
    | 4 | 32 | 936 us | 1103 us | 26% |
    | 8 | 64 | 1938 us | 2137 us | 51% |
    | 12 | 96 | 2999 us | 3476 us | 83% |
    | 16 | 128 | 3952 us | 4260 us | 102% |
    | 20 | 160 | 4845 us | 5184 us | 124% |
    | 32 | 256 | 7568 us | 8190 us | 197% |

  - **The critic caught the original conclusion being unsupported, and it was
    right.** The first version measured depths 1, 8 and 32 only, then declared the
    supported depth to be 8 "because that is what the measurement supports and 32
    is not". Eight was simply the largest sampled depth that passed; "32 is
    unaffordable" argues against 32 and is not evidence for 8. The curve is close
    to linear at roughly 240 us per depth, so the real limit was around 16 and
    nobody could have seen that from three samples. Intermediate depths are now
    measured, and the cap is held at 8 for a stated reason — 51% of budget against
    83% at depth 12, on a development machine rather than the slowest that will
    run the game — rather than being presented as the limit.
  - The probe now fails if this constant drifts in **either** direction: above what
    is affordable, or below half of it. Too low is also a defect — Phase 6 would
    quietly give up correction quality for no reason — and either way the constant
    and the evidence must not silently disagree.
  - Added guard against a vacuous measurement: a scenario issuing fewer than three
    queries per simulated frame fails. The hostile spawn sits close to
    `MaximumRecoverablePenetration`, and if it ever crossed it the motor would bail
    out of `RecoverPenetration` after one query per frame and the gate would go
    green having timed almost no work.
  - **Constraint on Phase 6: `OwnerPredictionWorkPolicy`'s replay-depth cap must
    not exceed 8.** P06-07 and P11-04 must honour it rather than choosing a cap by
    intuition.
  - Per-query cost is higher than P01-11's headline figure: about 21 us on open
    ground and 29 us in a corner, against the 10.8 us a single isolated query
    measured. Worth knowing before anyone budgets from the older number.
  - Depths above the supported cap are measured and reported but deliberately not
    gated. A gate that fails by design is a gate everyone learns to ignore, and
    the evidence for *why* the cap is 8 is more useful than the cap alone — a
    future optimisation that makes 32 affordable will show up here first.
  - The gate enforces the 95th percentile rather than the single worst frame. The
    worst frame in a headless probe is dominated by collection pauses, not query
    cost: at depth 1 over open ground it measured 3178 us against a 134 us mean,
    which is a GC pause and not twenty-four times the work. Gating on that would
    be flaky, and a flaky gate gets disabled. Mean and true worst are both still
    reported so a genuine spike stays visible.
  - Queries are counted by a decorator around the production adapter rather than
    a counter inside it, so the measured motor is byte-for-byte the shipped one.
  - Purpose: P01-11 measured the *probe's* query cost, not the motor's. The
    motor issues several queries per frame — recovery, sweep, per-slide-iteration
    re-sweep, ground probe, and up to three more for a step — so the real budget
    is a multiple of the probe's figure and has never been measured.
  - Target files: `MovementMotorCostProbe.cs`, `run_movement_motor_cost.ps1`.
  - Verification: Reports queries per frame and microseconds per frame for
    typical and worst-case geometry, at replay depths of 1, 8, and 32 frames,
    and fails if a frame exceeds the authored budget. Feeds P11-04.

## Phase 6 — Owner History and Exact Reconciliation

### Plan refresh: what Phase 5B changed for the rest of Phase 6

P06-01..12 were planned and their stubs reviewed *before* 5B ran. 5B then
measured the motor in-engine and produced four constraints that the reviewed
stubs do not account for. Recording them here, before implementing, because each
one is a thing reconciliation would otherwise get wrong in a way that looks like
a network problem:

1. **The motors are one frame out of phase, and reconciliation compares
   same-frame.** Measured: same-frame horizontal position differs by 0.126 m,
   collapsing to 0.026 m at a one-frame offset. Reconciliation compares an
   owner's predicted frame against authority's answer *for that frame*, so any
   phase error in that path reads as a divergence on **every** frame and would
   correct the player continuously — the worst possible failure, because it looks
   exactly like a tolerance being too tight. P06-03 and P06-06 must each carry an
   explicit test that a correctly-predicted frame compared against its own
   authority answer produces *no* difference, with the frame identity asserted on
   both sides rather than assumed.

2. **The replay-depth cap is 8, measured, not chosen.**
   `OwnerPredictionWorkPolicy.MaximumReplayFrames` must not exceed it. Over
   hostile geometry depth 12 costs 83% of a quarter-frame budget and depth 16
   costs 102%. The policy should refuse a larger value rather than trusting its
   caller, and the constant should name P5B-03 as its source so the next person
   to raise it knows what to re-measure.

3. **Contact ordering is not reliable in-engine, so contact comparison cannot be
   an equality check.** The adapter reports one shared travel fraction for every
   contact of a sweep, so `CompareForStableResolution`'s primary key is constant
   and the real ordering falls through to a physics-server RID. Two processes can
   therefore order the same corner's contacts differently. P06-03 must compare
   contacts as a *set keyed by collider and surface kind*, never as an ordered
   sequence, and P06-04 must not raise `ContactReplay` on an ordering difference
   alone. This is a workaround for a recorded defect, not the desired end state —
   see P5B-01's finding 1 for the real fix and why it was deferred.

4. **Discrete grounding fields are the ones that flicker.** The step-solver
   blocker 5B found expressed itself as `IsGrounded`, `GroundNormal` and
   `Support` changing every frame of a step approach while position stayed
   plausible. That is the shape of a motor bug reaching reconciliation, so
   P06-03's discrete-field comparison is load-bearing for diagnosis, and P06-04's
   reason codes must distinguish it from a numeric drift. A correction storm on
   stairs should name grounding, not position.

Two further deferrals inherited from 5B that Phase 6 must not silently depend
on: `RecoverPenetration`'s skin scaling is documented-wrong but left alone
because correcting it broke coasting, and a climbable ledge shallower than one
capsule radius can no longer be stepped onto. Neither blocks reconciliation, but
both are motor behaviour that a trace comparison will faithfully reproduce, so
they must not be mistaken for reconciliation defects.

### Phase 6 revision: Phase 6A foundations

Phase 6 as originally planned cannot deliver what it promises. Two independent
blockers — cross-process collider identity and the replay-budget conflict — turn
out to have the same answer, and it is an architectural one, so it belongs in a
small foundations unit ahead of the reconciliation loop. Phase 6 is therefore
split: **6A** below, then **6B** for the existing P06-01..12.

#### The research that decided it (P06-A3)

The question was whether replay can cover the prediction lead, or whether the
design must degrade to rebasing at real latency. Rocket League was the reference,
and it reframes the problem rather than answering it as posed.

Established: Rocket League runs physics at **120 Hz on both client and server**
(server sending at ~60 Hz), uses **Bullet inside UE3 specifically to get
deterministic networked physics**, and predicts **not only the local car but all
cars and the fully physical ball — resimulating the entire physics scene**. For
remote players it applies **input decay**, using full input on the first predicted
frame then roughly 66%, 33%, and none, because that reads better than overshooting
and rubber-banding back. Physics is single-threaded.

Not established, and deliberately not assumed: whether they cap the rollback
window, and whether corrections snap or blend. The GDC PDF would not parse and the
practitioner thread is behind a 403.

**The decision rests on P5B-03's measurement, not on the Rocket League
comparison.** The stub review was right to push on this and the framing is
corrected here: Rocket League runs 120 Hz *forward* simulation with input decay for
remotes, which is prediction breadth, not 48-frame rollback per received packet. It
also steps a whole world — one broad phase, one solver — against our per-operation
query cost, so the two cost structures are not comparable, and the plan itself
records that the decisive fact is unknown (whether they cap the rollback window; if
they do, the citation argues the other way). What Rocket League genuinely
establishes is weaker but still useful: choosing an in-process deterministic physics
library over the host engine's own, specifically to get deterministic networked
physics, is a shipped and successful choice rather than an exotic one.

The measured argument stands on its own. **Every motor operation is a
`PhysicsServer3D.BodyTestMotion` round trip at 25-29 microseconds**, so a 48-frame
replay costs about 11.5 ms — consistent with extrapolating P5B-03's roughly
240-microseconds-per-depth line — against an 87 microsecond budget. Godot exposes no
batched motion test, so batching is unavailable.

The budget math settles it. Covering the authored 48-frame lead needs
4166.7 / 48 = **87 us per replayed frame**. At the 8 queries per frame P5B-03
measured, that is 10.9 us per query — below even P01-11's optimistic isolated
figure of 10.8 us. **Engine queries cannot reach the target at any useful depth.**
In-process capsule sweeps at 1-2 us put 48 frames comfortably under 1 ms.

**The conclusion drawn from this at first — build our own collision world — was
wrong, and P06-A1 is withdrawn.** Cost is real, but the cheap levers (Jolt,
memoization, accepting the measured depth) were never tried. What follows is the
part of the research that survived, and it is the more important part.

#### The engine boundary, decided by what Godot actually permits

The question "why are we decoupled from Godot at all" has one concrete answer, and
it is not testability:

**`PhysicsServer3D::space_step()` and `flush_queries()` are not public API.**
Physics stepping is welded into the engine's main iteration loop. There are open
proposals (godot-proposals #1373, #7998, discussion #5707) and a PR, and one
proposal notes `space_step` was used internally for exactly client-side prediction
and reconciliation and never exposed — but it is not in 4.4.

Rollback requires replaying many simulation frames inside one engine frame.
Therefore:

- **A `RigidBody3D` driven by applied forces cannot be rolled back.** There is no
  way to advance Godot's world N times in one frame, so an engine-integrated body
  is unusable for owner prediction, however natural the physics would feel.
- **Queries have no such restriction.** `BodyTestMotion` with an explicit `From`
  transform may be issued as often as needed, which is exactly why P01-09 chose it.

So the correct description of this architecture is: **Godot's physics is the query
service; only the integration step is ours, because Godot will not re-step its
world.** That is one forced seam, not a home-grown engine. This reasoning belongs
in the plan permanently — the previous justification ("engine-free for
testability") is weak, and it is what made the whole design look like
gold-plating and nearly led to replacing the physics server.

Everything else should use Godot: ENet transfer modes and channels for transport
(already), built-in physics interpolation for render smoothing (already enabled),
shape queries at explicit historical transforms for hit validation (Phase 8), and
`GetColliderId()` for stable identity (P06-A1b).

#### Phase 6A subsections

- [x] **P06-A1 — WITHDRAWN. Do not build an in-process collision world.**
  - Status: **Withdrawn before implementation.** Stubs deleted.
  - I proposed replacing `PhysicsServer3D` with a capsule-accurate collision world
    of our own, to solve cross-process identity and replay cost together. That was
    an over-reach, and it is recorded rather than quietly dropped because the
    reasoning that produced it is the reasoning to watch for.
  - **Why it was wrong.** Both problems have far cheaper answers. Identity: Godot
    exposes `PhysicsTestMotionResult3D.GetColliderId()`, an ObjectID that resolves
    through `InstanceFromId` to the node and therefore to its authored path — a
    dictionary, not a subsystem (now P06-A1b). Cost: measure Jolt, memoize replay
    queries, and accept the measured depth with Phase 7 smoothing (now P06-A3).
  - **The smuggled premise.** I treated bit-determinism as a requirement. It is
    not — reconciliation exists precisely because two machines cannot be made to
    agree exactly, and this plan's own canonical hash is documented as diagnostic
    only and never a correction trigger. What replay actually needs is that the
    same process replaying the same frames gets the same answer, which
    `BodyTestMotion` against static geometry with an explicit `From` already
    delivers, as the golden trace suite and P5B-01's stability case both show.
  - **What it would have cost.** Reimplementing collision detection — and the stub
    review found the specified overlap algorithm was already wrong for slopes
    before a line was written, the yaw-only shape could not represent the arena's
    two X-pitched ramps, and the per-query cost target was an assumption dressed
    as a derivation. Against that: Godot's physics is hardened, and 5B had just
    finished validating our use of it.
  - Rocket League is a precedent for the opposite instinct: it does not use real
    car geometry for collision at all, only oriented bounding boxes per preset,
    with ball contact approximated as a force at a single point. Shipping
    simplification, not geometric exactness.

- [ ] **P06-A1b — Stable cross-process collider identity, inside the existing adapter.**
  - Status: **Planned.**
  - Purpose: what A1 was actually needed for. `SupportIdentity` is built from
    `colliderRid.Id`, a per-process physics-server allocation handle, so `Support`
    — a discrete comparison field checked before numeric ones — would mismatch on
    every grounded frame between two processes and correct the owner continuously.
  - Approach: resolve `GetColliderId()` to its node once per collider, derive an
    identity from the **owning body's** authored scene path plus the shape ordinal,
    and cache it by ObjectID. Never the shape node's own name: `MovementTestCourse`
    adds shapes as unnamed children, so Godot assigns
    `@CollisionShape3D@<counter>`, which is the same instantiation ordering the fix
    exists to escape. A walker's natural `shapeNode.GetPath()` would pass every
    single-process test and fail across processes — which is exactly how this
    defect class survived four phases.
  - Target files: `GodotKinematicCollisionWorld.cs`, `CharacterKinematicState.cs`.
  - Verification: two processes loading the same scene derive equal identities for
    the same collider; identity is stable across repeated queries within a process;
    the cache adds no per-query allocation.

- [ ] **P06-A2 — Canonical contact ordering in the rewind unit.**
  - Status: **Planned**.
  - Purpose: `FrameContactBuffer.Equals` is positional and `GetHashCode` folds in
    order, so `CharacterSimulationState`'s record equality and any canonical hash
    inherit order-sensitivity — while the order itself was RID-derived. Two
    endpoints agreeing about a corner would report different hashes, which the
    comparer classifies as "a bug to investigate".
  - Target files: `FrameContactRecord.cs`, `CollisionContactState.cs`.
  - Verification: buffers holding the same contacts in any insertion order are
    equal and hash equally; ordering is a pure function of content; must precede
    P06-02.

- [x] **P06-A3a — Measure Jolt against Godot Physics with the existing cost probe.**
  - Status: **Done. Jolt adopted** (`project.godot: 3d/physics_engine="Jolt Physics"`).
  - **Result: motion queries are about 2.5x cheaper, and it cost nothing but a
    project setting.** p95 over the hostile corner geometry:

    | depth | Godot Physics | Jolt | share of budget (Jolt) |
    | --- | --- | --- | --- |
    | 1 | 305 us | 107 us | 3% |
    | 8 | 2158 us | 900 us | 22% |
    | 12 | 3197 us | 1391 us | 33% |
    | 16 | 4264 us (over) | 1741 us | 42% |
    | 24 | 6817 us | 2624 us | 63% |
    | 32 | 8650 us | 3366 us | 81% |

  - Depth 16 went from *unaffordable* (102% of budget) to comfortable (42%). This
    is the entire lead-versus-depth problem, solved by a config change — after I
    had proposed replacing the physics server to solve it.
  - **All three motor gates re-baselined green under Jolt**, which the plan required
    because an engine swap invalidates every measurement 5B took. The parity numbers
    are essentially unchanged — top speed, braking, roll and crouch travel identical
    to three decimals, ledge climb within 5 mm — so the motor behaves the same and
    the swap cost no feel.
  - Also fixed here: `MovementMotorCostProbe` had its own private copy of the
    replay-depth constant, which made the drift gate fictional. It now reads
    `OwnerPredictionWorkPolicy.MeasuredMaximumReplayFrames` directly — the Godot
    project references both assemblies, so no move to Core was needed.

  - Original plan text, kept for the record: Godot 4.4 ships Jolt as an alternative under
    `physics/3d/physics_engine`. P5B-03 measured 25-29 us per `BodyTestMotion`
    against Godot Physics and never tried the alternative. If Jolt's motion queries
    are materially cheaper the replay-depth problem may simply evaporate, and the
    experiment is one project setting plus a re-run of a gate that already exists.
  - Target files: `project.godot`, `run_movement_motor_cost.ps1`.
  - Verification: the cost curve is reported for both engines; whichever is chosen
    is recorded with its numbers; if Jolt is chosen, P5B-01's adapter probe and
    P5B-02's parity probe both re-run green against it, because a physics engine
    swap re-baselines every motor measurement 5B took.

- [x] **P06-A3b — Set the replay-depth cap from measurement.**
  - Status: **Done. Cap raised from 8 to 16.**
  - Sixteen rather than the 24 that fits inside 70% of budget: 42% leaves real
    headroom for a slower machine, and 16 frames is 267 ms of replay coverage at
    60 Hz, which spans the 200 ms round trip P06-11 verifies. The authored 48-frame
    lead is a ceiling, not a typical value — a 200 ms round trip is roughly six
    frames of one-way lead plus jitter margin.
  - **The bidirectional check earned its keep immediately.** It failed the build
    saying the cap of 8 was "more than twice as conservative as the measurement
    requires", which is how the value got raised at all rather than quietly leaving
    replay depth unused.
  - It then failed *again* at 16, claiming depth 32 was affordable — which exposed a
    flaw in the check rather than in the cap. The top of the curve is noisy: depth
    32 measured p95 4668 us on one run (112% of budget) and 3477 on the next (83%).
    Promotion now requires a depth to fit inside 70% of budget, while the hard gate
    for depths at or below the cap stays at the full budget. Two thresholds because
    "is the cap affordable" and "should the cap be raised" are different questions,
    and only the second needs margin.
  - Memoizing replay queries is no longer needed for the Brazil target and is left
    unbuilt. It remains the cheap lever if a future cap needs to approach 48.

- [ ] **P06-A3b-superseded — original plan text, kept for the record.**
  - Purpose: resolve the lead-versus-depth conflict without an architectural
    change. Authored lead reaches 48 frames; measured affordable depth was 8-12.
  - Approach, cheapest first: take whatever A3a gives; memoize sweep results across
    a replay, since `ExplicitMotorGoldenTraceTests` documents that replay re-issues
    *identical* queries for frames whose inputs did not change, so a quantized
    origin/motion/profile key collapses most of a deep replay; then accept the
    resulting depth and let Phase 7's correction debt smooth what cannot be
    replayed.
  - Target files: `LocalMovementPredictionController.cs`
    (`OwnerPredictionWorkPolicy`), `MovementMotorCostProbe.cs`.
  - Verification: the cost probe measures depths up to the authored lead, the
    supported cap is derived from where the budget is actually crossed, and the
    probe fails if the constant and the measurement disagree in either direction.
    Rebase-plus-smoothing beyond the cap is a stated outcome rather than a
    surprise, and `ReplayDepthExceeded` (P06-A4) is the honest name for it.
  - Deferred to Phase 9: **input decay for remote players.** Queries are masked to
    static geometry today, so remote players are absent from replay entirely and
    there is nothing yet to decay.

- [ ] **P06-A4 — Depth-exceeded and configuration-exhausted correction outcomes.**
  - Status: **Planned**.
  - Purpose: even with A3, both remain reachable, and neither is representable
    today. There is no `OwnerCorrectionReason` for "small difference, too old to
    replay"; `OwnerCorrectionTelemetry.Create` throws either way because
    `HistoryMiss` requires no comparison and `ExtremeError` requires a large one;
    and `Decide` stamps `OrdinaryReplay` before the span is known.
  - Target files: `OwnerCorrectionTelemetry.cs`, `OwnerReconciliation.cs`.
  - Verification: each outcome has one enumerated reason, a telemetry factory
    that accepts a comparison having happened, and a decision made with the
    replay span in hand so the recorded action matches what occurred.

#### Stub review of 6A: three blockers, and A1's shape needs redesigning

Corrected already: the identity rule now keys on the **owning body's** path plus a
shape ordinal (the shape nodes have no authored names — `MovementTestCourse` adds
them as unnamed children, so Godot assigns `@CollisionShape3D@<counter>`, which is
the same process-local ordering this subsection exists to escape; a walker's natural
`shapeNode.GetPath()` would have passed every single-process test and failed across
processes); the ordinal is kept out of the identity hash so `ExcludedCollider` keeps
excluding a body rather than one shape; `StaticCollisionWorld` no longer holds the
profile table, because rebuilding the world on a revision change is precisely how a
replayed frame gets swept with the wrong capsule — dimensions travel with the
request, the rule `WalkableSlopeRadians` already follows; and the Rocket League
framing is corrected to say the decision rests on P5B-03's measurement.

**Still open, and A1 must not be implemented until these are settled:**

1. **BLOCKER — the yaw-only parametric shape cannot represent this arena.**
   `MovementTestCourse` authors `StairTraversalRamp` and `StairRampDown` as boxes
   pitched about **X** (15.4 and -14 degrees), and the first is the *only* collision
   surface for the entire stairs station. Neither is a wedge: a pitched slab's top
   face is a full offset rectangle, while `Wedge` as stubbed rises from zero. Every
   available workaround is bad — refuse them and the stairs lose their floor, flatten
   the pitch and A1's own fidelity gate fails, approximate as a wedge and the normal
   is wrong, which is the error class that caused P5B-01's step defect. The
   restriction also buys nothing: an arbitrarily oriented box's support point costs
   the same. Direction: store each collider as its vertex or plane set plus a full
   basis, drop `Box`/`Wedge` as distinct kinds, and keep the support function a loop
   of dot products. The real ramps are `ConvexPolygonShape3D` with eight authored
   vertices, so a parametric builder would have to reverse-derive a wedge from a
   vertex list and would silently change meaning if those vertices were ever edited.
   The builder also needs an explicit fault for an unsupported authored shape — a
   sphere, trimesh or GridMap added later would otherwise become a hole in the floor
   that both endpoints agree about.
2. **BLOCKER — `ResolveOverlap`'s specified algorithm is box-only.** "Least
   penetrated axis" was lifted from `DeterministicCollisionWorld`, where it is
   correct because both bodies are AABBs. A capsule against a pitched slab has no
   axes, and an axis-aligned push on the slope station shoves the character
   vertically or laterally instead of along the slope normal — on a query that runs
   before every sweep of every frame. Correct formulation: segment-to-convex
   distance, `depth = radius - distance`, direction from the witness points.
3. **BLOCKER — the "1-2 microsecond" per-query figure is an assumption dressed as a
   derivation.** A support function is closed-form; the conservative-advancement
   sweep built on it is an iterative root-find calling GJK per candidate per
   iteration. A realistic first implementation is 0.5-3 microseconds per pair times
   several candidates times the iteration count, and A1's gate asserts under 3 for
   the whole query with no named fallback. The architecture survives anything under
   10.9, but A3's "raise the cap toward 48" depends on this number, so the gate needs
   a stated fallback rather than a target nobody has hit yet.

**Cheaper alternatives that were not argued against, and should be:** memoizing
sweep results across a replay (`ExplicitMotorGoldenTraceTests` documents that replay
re-issues *identical* queries for frames whose inputs did not change, so a quantized
origin/motion/profile key would collapse most of a deep replay's query count against
*either* world); amortizing a deep replay across the frames before the next authority
answer; and gating replay on measured divergence rather than on every answer. The
in-process world is still the choice I would make, but the plan presented it as
forced when it is a decision.

**Other findings recorded:**
- A4's new validation arm calls `RequireRebaseTargetsComparison`, which asserts the
  correction lands on the comparison frame — up to 48 frames old, or 10 m backwards
  at sprint speed. The existing `HistoryMiss` arm is *looser* and permits
  rebase-then-fast-forward, so the new arm is stricter than the reason it replaces
  and structurally forbids catch-up. Additionally `ValidateObservation` reports
  `replayDepth = 0` for every `HardRebase`, so the reason whose stated purpose is
  signalling a mis-tuned budget records no magnitude and A3's tuning loop gets a rate
  without a depth.
- **A1 has no reproducibility gate**, yet conservative advancement is exactly where
  run-to-run non-determinism enters, and this is the world replay will use.
  `ExplicitMotorGoldenTraceTests` — the suite whose whole purpose is catching a rule
  that differs on a second pass — stays on `DeterministicCollisionWorld`, so after 6A
  the determinism gate would still prove box behaviour. That is the identical
  criticism P5B-01 levelled at the 201-test suite.
- Unowned in 6A and will force rework: who constructs and holds the world (nothing in
  `BattleArena.Multiplayer` constructs one today); how the two worlds coexist during
  migration; sequencing the build after `MovementTestCourse._Ready()`; and
  `ContentHash` disagreement having no member, handshake, or protocol field to travel
  on despite the stub claiming it is compared at join.
- P5B's tunings are re-baselined by this change and A1 does not name them:
  `SurfaceSkin` and `IgnoredPenetrationDepth` are justified *solely* by the engine's
  1 mm margin, which disappears — but both values stay load-bearing in the P5B-01
  step fix, so the invariant becomes documented, dead, and structural, and the next
  person to clean it up breaks stepping. `MovementMotorParityProbe` and
  `run_movement_motor_cost.ps1` also need re-baselining and are not target files.
- The broad-phase overflow contract is unreachable against ~23 collision-enabled
  shapes and unreportable anyway, since `CapsuleSweepResult` has no fault channel.
  A uniform grid plus a 64x76 ground box also means one shape is reported from many
  cells, and de-duplicating without allocation normally needs a visited stamp, which
  contradicts "immutable, no per-frame state".
- A2's verification text is unmeetable as stubbed: it promises buffers equal and
  hashing equally in any insertion order, but the stubs deliberately leave `Equals`
  positional and add `DescribesSameContacts`/`CopyCanonical` alongside. Restate it as
  "the canonical copy is order-independent and P06-02 must use it".
- Correction to the earlier Phase 6 stub review: it claimed `MovementMotorCostProbe`
  cannot see `BattleArena.Multiplayer`. It can — `Battle Arena.csproj` references
  both projects — so the fix is to delete the probe's private `IntendedReplayDepth`
  and read `OwnerPredictionWorkPolicy.MeasuredMaximumReplayFrames`, not to move the
  constant into Core.

- [x] **P06-A5 — Delete the three test-only models.**
  - Status: **Done. ~3,100 lines removed, and PX-01 is resolved as a side effect.**
  - Deleted `PredictionPerformanceProbe`, `PredictionPacketBudgetProbe`,
    `DeterministicReliableChannelModel` and their three test files. The multiplayer
    suite went from 952 tests to 904 and now passes on three consecutive runs with
    no order-dependent failures.
  - One type was rescued rather than deleted: `PredictionTransportOverhead` moved to
    its own file, because the protobuf codec tests use it to check that a *really
    encoded* owner command still fits an MTU-safe datagram once ENet or Steam
    framing and IP/UDP headers are added. That is measurement against real bytes,
    which is exactly the kind of evidence this revision kept.

- [ ] **P06-A5-superseded — original plan text, kept for the record.**
  - Purpose: `PredictionPerformanceProbe` (1,130 lines), `PredictionPacketBudgetProbe`
    (1,124) and `DeterministicReliableChannelModel` (877) are referenced by nothing
    but their own tests. Together that is ~3,100 lines of the shipping assembly
    modelling things we now measure: 5B measured replay cost for real and got a
    better answer than `PredictionPerformanceProbe` projects, and
    `DeterministicReliableChannelModel` reimplements reliable-ordered delivery and
    head-of-line blocking that ENet already provides and that we already use.
  - They are also the root of PX-01: adding one value to `OwnerCorrectionReason`
    shifted execution order and moved a memory-layout ceiling from 4048 to 4848
    bytes, so the correction taxonomy that Phases 6 through 9 all extend cannot
    currently be extended without reding the build.
  - The measured *numbers* are worth keeping; they are already recorded in P01-10
    and P01-11. The code that produced them is not.
  - Verification: both suites green with no order-dependent allocation failures,
    on repeated cold runs.

#### The lens this revision came from

Phase 1 was titled "Evidence, Impairment, and Feasibility" and produced evidence by
building *models*. Phase 5B produced better evidence in a fraction of the code by
running the real thing headlessly and asserting on it. Every place this plan
modelled something instead of measuring it, the model turned out to be both larger
and less accurate — and the withdrawn P06-A1 was about to repeat that at the scale
of a physics engine.

**Standing rule for the rest of the plan: prefer a headless probe against the real
engine over a model of the engine.**

Consequences already identified elsewhere:
- **Phase 7, P07-01** should shrink. Godot's built-in physics interpolation is
  already enabled (`project.godot: common/physics_interpolation=true`) and
  `NetworkAvatar` already manages it per node, so "one-tick interpolation" is
  largely provided. P07-02/03 correction debt stays — that is genuinely
  netcode-specific and Godot has no equivalent.
- **Phase 8** should validate hits with Godot shape queries at explicit historical
  transforms (`PhysicsShapeQueryParameters3D.Transform`), not custom geometry.
- **Phase 9** is where goal 3 lands: forward owner commands over the existing
  prediction mesh and predict remote players from their real inputs — the pattern
  Unity's Netcode for Entities documents — with Rocket League's input decay as the
  fallback when a peer's input has not arrived, and authority always overriding.
- **Protobuf stays.** It is an intentional choice for versioned wire contracts and
  is not up for revision; the guidance is only to avoid *growing* it beyond what
  each phase needs.

#### Deferred to Phase 6B, folded into existing subsections

Recorded here so they are not lost: cue identity completion (widen
`OwnerSimulationEvent` to the ledger's five-field key and route committed events
to the adapter) folds into P06-10; duplicate and reordered authority answers fold
into P06-01; queued-future application folds into P06-07; frame-identity coherence
on the write side folds into P06-01. Attack movement influence (P05-18) and the
motor defect debt from 5B move into 6B as their own subsections.

### Stub review of the refresh: it closed one constraint of four

A critic reviewed the refresh above against the stub set. Its verdict on the four
constraints: **(d) closed, (a) half closed, (b) and (c) not closed.** Recording
the findings, because the refresh's own prose was more confident than the shapes
it described — the exact failure the review exists to catch.

**BLOCKER — collider identity is process-local, so constraint (c)'s fix does not
work.** I framed the contact problem as *ordering*. The deeper problem is the
key. `SupportIdentity` is built from `colliderRid.Id`
(`GodotKinematicCollisionWorld.cs`), a physics-server allocation handle. P5B-01's
`VerifyStableColliderIdentity` proved it stable *within one process* — which is
all it tested — and it is not comparable between two. Authority and owner are two
processes; that is P06-11's and P06-12's whole premise. Consequences:
  - `ContactsDescribeTheSameSurfaces` keyed on collider returns false on every
    frame with any contact, over geometry both sides agree about.
  - Worse, `OwnerMismatchField.Support` compares `Kinematic.Support`, also a RID,
    and discrete facts are compared before numeric ones — so **every grounded
    frame produces a discrete mismatch**, and the result is continuous
    `ContactReplay` corrections. That is the same correction-storm failure
    constraint (1) exists to prevent, arriving through a different door.
  - Switching from sequence to set does not touch this. Comparing as a set was
    still right, but insufficient.
  - **This changes subsection ordering**, answering the one open question the
    refresh had: P06-09's deterministic, scene-derived collider identity must
    land *before* P06-03 can compare contacts or support cross-process.
    Otherwise P06-03 must explicitly exclude both fields and document contact
    divergence as unreachable — which guts P06-04's `ContactReplay` and
    constraint (d)'s diagnosis story. Decide this before implementing P06-03.

**BLOCKER — exceeding the replay-depth cap is the common case at real latency and
has no representable outcome.** Retention is 256 frames and authored prediction
lead reaches 48, so the replay span exceeds 8 on every correction above roughly
130 ms RTT — and P06-11's own verification runs 120 ms and 200 ms. At 200 ms it is
the *only* case. Yet: no `OwnerCorrectionReason` means "small difference, too old
to replay"; `OwnerCorrectionTelemetry.Create` throws either way, because
`HistoryMiss` requires no comparison to have happened and `ExtremeError` requires
a non-zero one that a 2 cm difference does not justify calling extreme; and
`OwnerReconciliationPolicy.Decide` stamps `OrdinaryReplay` *before* `ReplayFrom`
discovers the span, so the decision contradicts what happened. Needs a new reason
value, a telemetry factory that accepts it, and `Decide` receiving the replay span
(computable as `history.NewestFrame - frame` before deciding). The same gap
applies to `ConfigurationHistoryPolicyExhausted` mid-replay.

**BLOCKER — the cue dedup story has no path end to end.**
`OwnerSimulationEvent` stores `(eventId, kind)`; `PredictedCueIdentity` requires
`(epoch, kind, originKind, originId, eventOrdinal)` and throws on an unspecified
origin or a zero id. Epoch is recoverable from the epoch-bound history;
`OriginKind` and `EventOrdinal` are simply absent, so replay must invent them —
which is the re-derivation the same file's opening remark argues against, and a
derivation that is not bit-identical makes the ledger fire the sound again.
Separately, the only member that could offer identities to the ledger is
`GodotOwnerPredictionAdapter.CommitFrame`, whose parameters carry no events and
which has no access to history. So events are stored by a type documented as not
using them and consumed by a type that cannot see them. Fix is a shape change:
widen `OwnerSimulationEvent` to the ledger's full key, and either pass the
committed frame's event buffer to `CommitFrame` or let the controller own
`ObservePredicted` and pass emit decisions out.

**BLOCKER — `ReplayFrom`'s remark recommends the mechanism that destroys the data
replay needs.** It says frames are resimulated "with the input and applied
transitions it originally consumed" and, in the next sentence, that "history is
truncated and rewritten rather than edited ad hoc". The first needs frames F+1..N
present during replay, because that is the only place `Command` and
`AppliedTransitions` live. `TruncateAfter` deletes exactly those before replay
reads them. `TryReplacePostState` is sufficient alone. Keep replace-in-place,
restrict `TruncateAfter` to the rebase path, and delete the truncate sentence.

**SHOULD-FIX — record equality and the canonical hash stayed order-sensitive, and
`FrameContactRecord`'s own comment asserts the premise 5B disproved.** It still
claims the stored sequence is comparable positionally. `FrameContactBuffer.Equals`
is a positional walk and `GetHashCode` folds in order, and
`CharacterSimulationState` is a record struct containing one — so `==` and the
canonical hash inherit it. P06-02 would then hash contacts in stored order, two
processes that agree about a corner would report different hashes, and the
comparer classifies that as `DiagnosticHashOnly`, documented as "a bug to
investigate". P06-12 would spend its first week investigating a non-bug on every
corner frame. Either canonicalize order on insert or require P06-02 to sort before
hashing.

**SHOULD-FIX — `OwnerPredictionWorkPolicy` cannot refuse anything and its safety
net is fictional.** It is a record struct with only `init` properties, so
`new OwnerPredictionWorkPolicy { MaximumReplayFrames = 64 }` compiles; `IsValid`
is a backstop nothing forces callers through, and `default` yields a silently
replay-free policy. Needs a validating factory. And the remark's claim that
`run_movement_motor_cost.ps1` "fails if this constant and the measurement
disagree" is false: the probe declares its own `IntendedReplayDepth` and has no
reference to `BattleArena.Multiplayer`, so raising the Multiplayer constant leaves
the gate green. The gate protects the probe's copy of the number, not the one
Phase 6 uses. Either move the constant to Core where the probe can read it, or
have the probe assert against a shared value.

**SHOULD-FIX — the queued-future path has bounds but no consumer.**
`MaximumQueuedFutureStates` and `QueuedFutureCount` exist; nothing queues,
nothing drains, and no type holds a queued entry (`state`, `epoch`, `hash` must
all be retained). Worse, if `PredictFrame` drains implicitly it returns
`CharacterFrameResult` while `CommitFrame` needs an `OwnerReconciliationResult`,
so a queued state that triggers a replay during catch-up produces a correction the
adapter cannot be told about and `MaySmooth` cannot be consulted for. Needs an
explicit `ApplyQueuedFutureStates()` or a widened return.

**SHOULD-FIX — "a state for a frame already reconciled is idempotent" is not
deliverable.** `PruneThrough` drops confirmed frames, so a retransmitted or
reordered state for a confirmed frame becomes `OlderThanRetention` → `HistoryMiss`
→ `HardRebase`: a duplicate packet produces a visible snap.
`OwnerHistoryLookupDecision` cannot distinguish "gone because confirmed" from
"gone because the window moved", and those want opposite responses.

**SHOULD-FIX — constraint (a) is closed on the read side only.** `Compare`'s
frame-identity fault is real and assertable. But `PredictFrame` receives two
independent frame numbers (`command.TargetFrame` and `context.Frame`) with no
coherence contract — and 5B's own phase investigation turned on exactly that
pairing — and `OwnerPredictedFrame` carries four frame numbers with no stated
relationship. Cheap to fix, and it is the guard whose absence produces the failure
the refresh itself calls the worst available.

**MINOR** — two wrong comments worth correcting because they will mislead:
`Telemetry` is described as "accumulated for this combatant" but
`OwnerCorrectionTelemetry` is a single immutable observation
(`PredictionTelemetryRing` is the accumulator); and "replay depth is derived by
telemetry ... so it has one source" is false, since telemetry computes the span
from frame numbers while `FramesReplayed` records what replay actually did, and
those disagree exactly in the depth-cap case above. Also
`OwnerReconciliationPolicy` cannot read a hash *value* — verified — but
`DiagnosticHashOnly` is a `Kind` it does receive, so the structural claim is
slightly weaker than stated.

Verified and needing no change: P06-05's position relative to the comparer is
correct (the comparer reads revisions off the state directly and needs no
timeline); an epoch change mid-replay is correctly unrepresentable because history
is epoch-bound; and deferring P06-08/09 remains right for everything except the
collider identity above.

- [ ] **P06-01 — Implement the bounded owner prediction history.**
  - Status: **Stubs Reviewed**.
  - Purpose: Store complete pre/post state, command, revisions, contacts, events,
    and diagnostics in a preallocated frame-indexed ring.
  - Target files: `OwnerPredictionHistory.cs`,
    `OwnerPredictionHistoryTests.cs`.
  - Verification: Insert/find/prune/wrap/epoch/reset/history-miss and no-allocation
    tests pass.

- [ ] **P06-02 — Implement canonical diagnostic state hashing.**
  - Status: **Stubs Reviewed**.
  - Purpose: Produce versioned XxHash64 diagnostics from explicitly ordered,
    quantized fields rather than raw floats or Protobuf bytes.
  - Target files: `CanonicalMovementStateHash.cs`,
    `CanonicalMovementStateHashTests.cs`.
  - Verification: Golden bytes/hash, field inclusion/exclusion, source ordering,
    seam tolerance, and schema-version tests pass.

- [ ] **P06-03 — Implement tolerant owner state comparison.**
  - Status: **Stubs Reviewed**.
  - Purpose: Separate exact gameplay-discrete mismatches from numeric tolerance
    and diagnostic-only manifold differences.
  - Target files: `OwnerReconciliationComparer.cs`,
    `OwnerReconciliationComparerTests.cs`.
  - Verification: Position/velocity/facing/contact thresholds, seam equivalence,
    support/profile/action mismatch, and first-field diagnostics pass.

- [ ] **P06-04 — Implement correction classification policy.**
  - Status: **Stubs Reviewed**.
  - Purpose: Choose confirmed, ordinary replay, contact replay, or hard local
    rebase without smoothing collision truth.
  - Target files: `OwnerReconciliationPolicy.cs`,
    `OwnerReconciliationPolicyTests.cs`.
  - Verification: Boundary and enumerated-reason tests pass; no raw hash alone
    triggers correction.

- [ ] **P06-05 — Integrate tick-effective movement configuration lookup.**
  - Status: **Stubs Reviewed**.
  - Purpose: Resolve the canonical revision for every first-run/replay frame and
    queue baselines when definitions are briefly missing.
  - Target files: `MovementConfigurationTimeline.cs`,
    `MovementConfigurationTimelineTests.cs`, `CharacterMovementSimulator.cs`.
  - Verification: Same-frame revision changes replay exactly; valid intent is not
    rejected merely for a client's stale claimed revision.

- [ ] **P06-06 — Implement static-world local reconciliation.**
  - Status: **Stubs Reviewed**.
  - Purpose: Restore the exact authority frame and replay later owner commands
    through simulation only.
  - Target files: `LocalMovementPredictionController.cs`,
    `LocalMovementPredictionControllerTests.cs`, `OwnerPredictionHistory.cs`.
  - Verification: Confirmation prunes without replay; injected errors replay the
    correct frames and converge.

- [ ] **P06-07 — Implement queued-future and hard-rebase handling.**
  - Status: **Stubs Reviewed**.
  - Purpose: Queue authority frames ahead of local simulation and perform one
    clean local rebase for missing history/epoch/penetration failures.
  - Target files: `LocalMovementPredictionController.cs`,
    `LocalPredictionRebaseTests.cs`, `CombatantPredictionEpochGate.cs`.
  - Verification: Out-of-order/future/old states cannot regress; each hard rebase
    has one enumerated reason and clears bounded state atomically.

- [ ] **P06-08 — Finalize the owner-baseline Protobuf state.**
  - Status: **Stubs Reviewed**.
  - Purpose: Encode the proven Phase 5 state, ACKs, journals, lead policy, and hash
    schema without authority-only hit/damage fields.
  - Target files: `authority_state.proto`, `ProtobufProtocolCodecTests.cs`,
    `InboundMessageValidatorTests.cs`.
  - Verification: Full round trip, reserved-number, fuzz, bounds, and packet-size
    gates pass.

- [ ] **P06-09 — Finalize the compact collision-world Protobuf state.**
  - Status: **Stubs Reviewed**.
  - Purpose: Encode self-contained quantized frame state, bounded contacts/sources,
    and deterministic partition identity.
  - Target files: `authority_state.proto`,
    `MovementWorldProtocolTests.cs`, `InboundMessageValidator.cs`.
  - Verification: 1/3/8-player round trips, incomplete-part discard, next-frame
    supersession, and byte ceilings pass.

- [ ] **P06-10 — Add commit-once Godot owner adapter.**
  - Status: **Stubs Reviewed**.
  - Purpose: Reconcile/replay in value state, then commit one final collision pose
    and publish one presentation sample per real frame.
  - Target files: `GodotOwnerPredictionAdapter.cs`, `NetworkAvatar.cs`,
    `GodotOwnerPredictionAdapterTests.cs`.
  - Verification: N-frame replay performs one body commit, one presentation
    sample, and zero historical node/cue operations.

- [ ] **P06-11 — Integrate static-only V2 owner prediction.**
  - Status: **Stubs Reviewed**.
  - Purpose: Exercise exact history/reconciliation in the arena while explicitly
    disabling/softening predicted player collision until Phase 9.
  - Target files: `NetworkArena.cs`, `NetworkAvatar.cs`,
    `verify_multiplayer_parity.ps1`.
  - Verification: Host/client unopposed locomotion passes at 0/80/120/200 ms with
    local input latency unchanged; V2 cannot become alpha default.

- [ ] **P06-12 — Add separate-process canonical trace parity.**
  - Status: **Stubs Reviewed**.
  - Purpose: Compare authority and owner per-frame state/first divergence across
    independent Godot processes.
  - Target files: `verify_owner_prediction_trace.ps1`, `NetworkArena.cs`,
    `OwnerPredictionTraceAssertions.cs`.
  - Verification: 10,000 static-world frames match in the same build and injected
    faults identify the exact first field/frame.

## Phase 7 — Render-Rate Presentation and Correction Debt

- [ ] **P07-01 — Implement owner render-pose strategies.**
  - Purpose: Provide bounded latest-state extrapolation and one-tick interpolation
    reference modes independent of correction smoothing.
  - Target files: `OwnerPresentationPoseGenerator.cs`,
    `OwnerPresentationPoseGeneratorTests.cs`.
  - Verification: Constant speed, reversal, wall, landing, profile/action change,
    and 30–240 FPS sampling tests pass.

- [ ] **P07-02 — Implement model correction debt.**
  - Purpose: Preserve the pre-correction visual pose and decay horizontal,
    vertical, and yaw debt without overshoot.
  - Target files: `OwnerCorrectionPresenter.cs`,
    `OwnerCorrectionPresenterTests.cs`.
  - Verification: Frame-rate-independent decay, clamp, accumulation, contact, and
    hard-reset tests pass.

- [ ] **P07-03 — Implement camera-anchor correction debt.**
  - Purpose: Smooth only camera position while keeping render-frame yaw/pitch
    fully local and never rewound.
  - Target files: `LocalCameraPresentationController.cs`,
    `LocalCameraPresentationControllerTests.cs`.
  - Verification: Mouse/controller look remains current-frame during corrections,
    reset, and 30–240 FPS tests.

- [ ] **P07-04 — Compose the owner presentation path.**
  - Purpose: Apply normal render pose, then correction debt, exactly once with no
    double built-in/custom interpolation.
  - Target files: `CharacterPresentationController.cs`, `NetworkAvatar.cs`,
    `network_avatar.tscn`.
  - Verification: Scene/runtime diagnostics prove custom-driven nodes disable
    built-in interpolation and model/camera remain continuous.

- [ ] **P07-05 — Add correction visual diagnostics.**
  - Purpose: Toggle raw body, rendered pose, authority pose, correction vector,
    replay depth, and first mismatch without affecting simulation.
  - Target files: `OwnerPredictionDiagnosticsView.cs`, `NetworkPlayerHud.cs`,
    `NetworkArena.cs`.
  - Verification: Toggle smoke works for host/client and diagnostics never change
    canonical state hashes.

- [ ] **P07-06 — Add render-rate prediction parity automation.**
  - Purpose: Verify owner presentation at 30/60/75/120/144/165/240 FPS over the
    same 60 Hz simulation.
  - Target files: `verify_owner_render_rates.ps1`,
    `OwnerRenderRateAssertions.cs`.
  - Verification: No whole-tick default visual latency, oscillation, double
    interpolation, or camera yaw correction.

- [ ] **P07-07 — Tune correction policies from recorded distributions.**
  - Purpose: Replace provisional half-lives/clamps with measured values accepted
    on the movement course.
  - Target files: `OwnerCorrectionPresentationPolicy.cs`,
    `OwnerCorrectionPresentationPolicyTests.cs`,
    `LOCAL_OWNER_PREDICTION_REDESIGN.md`.
  - Verification: Distribution fixtures pass and the user accepts recorded raw
    versus smoothed comparison video/play.

## Phase 8 — Replayable Attacks and Fair Historical Validation

- [ ] **P08-01 — Define replayable character action state.**
  - Purpose: Store predicted/authority IDs, phase, accepted start, committed
    facing, combo state, and movement influence without hit/damage authority.
  - Target files: `CharacterActionState.cs`,
    `CharacterActionStateTests.cs`.
  - Verification: Copy/replay tests pass and type boundaries cannot store accepted
    targets, damage, health, or effects.

- [ ] **P08-02 — Drive starter-sword lunge through movement sources.**
  - Purpose: Replace one-time node velocity mutation with frame-addressed authored
    lunge state.
  - Target files: `StarterSwordAttackPolicy.cs`,
    `MovementSourceSimulator.cs`, `StarterSwordMovementSourceTests.cs`.
  - Verification: Tap/hold/finisher/remap/reject replay produces one correct lunge
    beginning on the accepted action frame.

- [ ] **P08-03 — Implement authority action frame scheduling.**
  - Purpose: Accept on target frame, explicitly remap within policy, or reject;
    receipt time never silently becomes action time.
  - Target files: `AuthorityActionScheduler.cs`,
    `AuthorityActionSchedulerTests.cs`.
  - Verification: On-time, late-remap, too-late, duplicate, cooldown, life, and
    capability tests pass.

- [ ] **P08-04 — Implement bounded defender-history selection.**
  - Purpose: Validate the rendered-world frame against authority clock/path data
    and the existing half-RTT-plus-jitter, 200 ms cap.
  - Target files: `HistoricalHitFrameSelector.cs`,
    `HistoricalHitFrameSelectorTests.cs`.
  - Verification: Host/remote, 0–300 ms, spoofed frame, stale life, teleport,
    history edge, and cap tests pass.

- [ ] **P08-05 — Align movement, hit queries, and damage frame order.**
  - Purpose: Commit F movement before F hit queries, apply damage on F, and start
    hit-produced knockback on F+1.
  - Target files: `CombatApplicationFacade.cs`,
    `CombatApplicationFacadeTests.cs`, `AuthorityActionScheduler.cs`.
  - Verification: Mutual elimination, one-hit-per-target, current obstruction,
    F-lunge, and F+1-knockback tests pass.

- [ ] **P08-06 — Make attack presentation identity-idempotent.**
  - Purpose: Start the local clip once, attach authority identity without restart,
    seek/rate-repair drift, and handle rejection once.
  - Target files: `CharacterPresentationController.cs`,
    `AttackPresentationTests.cs`, `RiggedCharacterView.cs`.
  - Verification: Prediction, confirmation, remap, snapshot, replay, and rejection
    never double-start or multi-advance animation.

- [ ] **P08-07 — Integrate full attack grammar into multiplayer runtime.**
  - Purpose: Carry held/press/release to authority and keep direct hints combat-free
    while replaying action movement locally.
  - Target files: `NetworkArena.cs`, `NetworkAvatar.cs`,
    `verify_multiplayer_parity.ps1`.
  - Verification: Host/client perform starter sword tap, held step two, release/
    repress finisher, miss, cancel, and correction with matching action traces.

- [ ] **P08-08 — Add role-swapped combat fairness automation.**
  - Purpose: Detect systematic host/client hit advantage with lag compensation
    enabled, disabled, and capped at several values.
  - Target files: `verify_combat_fairness.ps1`,
    `CombatFairnessAssertions.cs`, `NetworkArena.cs`.
  - Verification: Stationary/moving role swaps at 0/40/80/120/200+ ms report
    requested/granted rewind and block unexplained outcome bias.

## Phase 9 — Frame-Aligned Player Collision

- [ ] **P09-01 — Define deterministic player-pair collision policy.**
  - Purpose: Lock equal weights, grounded/airborne behavior, non-supporting player
    surfaces, sweep rules, iteration bounds, and fallback.
  - Target files: `PlayerCollisionPolicy.cs`,
    `PlayerCollisionPolicyTests.cs`.
  - Verification: Moving/stationary, grounded/airborne, high-speed, vertical, and
    nonconvergence policies are explicit and deterministic.

- [ ] **P09-02 — Implement swept capsule-pair contact.**
  - Purpose: Detect high-speed relative player contact before final overlap.
  - Target files: `PlayerCapsulePairSolver.cs`,
    `PlayerCapsulePairSweepTests.cs`.
  - Verification: Head-on, crossing, grazing, tunneling, equal-time ordering, and
    no-contact traces pass.

- [ ] **P09-03 — Implement symmetric iterative pair/static resolution.**
  - Purpose: Share pair separation/normal velocity and re-sweep static clearance
    so a player is never pushed through a wall.
  - Target files: `PlayerCapsulePairSolver.cs`,
    `PlayerCapsulePairResolutionTests.cs`.
  - Verification: Squeeze, wall, corner, three-body, iteration order, and fallback
    tests pass independent of host/combatant iteration order.

- [ ] **P09-04 — Implement profile and spawn overlap policy.**
  - Purpose: Permit shrinking, validate expansion against static/player worlds,
    reject spawn overlap, and identify forced recovery.
  - Target files: `PlayerCapsulePairSolver.cs`,
    `PlayerProfileAndSpawnCollisionTests.cs`.
  - Verification: Crouch/roll/stand, forced expansion, clear/blocked spawn, and
    discontinuity cases pass.

- [ ] **P09-05 — Implement the authority movement-world simulator.**
  - Purpose: Resolve all provisional static motions, symmetric pairs, and final
    commits as one stable frame.
  - Target files: `AuthorityMovementWorldSimulator.cs`,
    `AuthorityMovementWorldSimulatorTests.cs`, `PlayerCapsulePairSolver.cs`.
  - Verification: 2/3/8-player traces are iteration/host-role independent and
    every combatant receives one final state.

- [ ] **P09-06 — Record collision dependency journals.**
  - Purpose: Store actual contacts, near-contact swept dependencies, supports,
    and movement-source dependencies per frame.
  - Target files: `CollisionDependencyJournal.cs`,
    `CollisionDependencyJournalTests.cs`.
  - Verification: Stable bounded recording, dedupe, causal connection, and no
    unrelated dependency tests pass.

- [ ] **P09-07 — Implement trusted remote collision timelines.**
  - Purpose: Advance collision proxies only from authority world state and
    accepted commands with bounded held-intent horizon/confidence.
  - Target files: `TrustedRemoteCollisionTimeline.cs`,
    `TrustedRemoteCollisionTimelineTests.cs`.
  - Verification: 100–150 ms hold, unknown edge suppression, freeze/neutral
    degradation, correction, and direct-hint exclusion pass.

- [ ] **P09-08 — Implement causal contact-island resolution.**
  - Purpose: Patch unrelated remote histories without owner replay and replay only
    owner-connected or potentially intersecting causal islands.
  - Target files: `CollisionDependencyResolver.cs`,
    `CollisionDependencyResolverTests.cs`.
  - Verification: Far-player mismatch causes no owner replay; contact chains,
    corrected swept intersections, and fully connected eight-player cases pass.

- [ ] **P09-09 — Integrate world-island owner reconciliation.**
  - Purpose: Restore authority state for the affected island and replay through
    the common world simulator while preserving visual-only direct hints.
  - Target files: `LocalMovementPredictionController.cs`,
    `MovementWorldReconciliationTests.cs`, `OwnerPredictionHistory.cs`.
  - Verification: Contact corrections converge, unrelated corrections do not
    touch owner state, and malicious direct hints cannot alter collision.

- [ ] **P09-10 — Integrate solid player collision into Godot multiplayer.**
  - Purpose: Replace legacy sequential Godot player response with the authority
    world solver and fixed-boundary client proxies.
  - Target files: `NetworkArena.cs`, `GodotMovementWorldCoordinator.cs`,
    `verify_three_player_movement.ps1`.
  - Verification: Role-swapped 2/3/8-player head-on/side/jump/squeeze tests show no
    systematic host advantage or render-time collision mutation.

## Phase 10 — Mesh, Configuration, and Fallback Integration

- [ ] **P10-01 — Stamp prediction-mesh evidence with common frame epochs.**
  - Purpose: Carry simulation frame, authority discontinuity, owner-control, and
    revision identities without adding combat authority.
  - Target files: `prediction_mesh.proto`,
    `PredictionProtocolCodecTests.cs`, `PredictionInboundMessageValidator.cs`.
  - Verification: Round trip, stale epoch, malformed frame, and combat-field
    exclusion tests pass.

- [ ] **P10-02 — Map direct/accepted/authority evidence onto one timeline.**
  - Purpose: Preserve precedence and per-peer visual timing while collision uses
    authority-trusted evidence only.
  - Target files: `RemoteMovementTimeline.cs`,
    `RemoteMovementTimelineTests.cs`, `GodotRemoteMovementPredictor.cs`.
  - Verification: Loss/duplicate/reorder/mismatch/quarantine/fallback permutations
    pass without direct collision influence.

- [ ] **P10-03 — Wire movement configuration into every runtime path.**
  - Purpose: Apply exact revisions to owner first-run/replay, authority, remote
    visual, and trusted collision prediction.
  - Target files: `NetworkArena.cs`, `MovementConfigurationTimeline.cs`,
    `MovementConfigurationRuntimeTests.cs`.
  - Verification: Frame-effective revision changes match across all paths and a
    delayed update repairs rather than discards valid input.

- [ ] **P10-04 — Preserve authority-only startup/fallback.**
  - Purpose: Let match entry and owner correctness continue when ENet/Steam mesh
    startup or an authenticated direct route fails.
  - Target files: `NetworkLauncher.cs`, `NetworkArena.cs`,
    `verify_multiplayer_parity.ps1`.
  - Verification: Forced prediction startup/send failure still enters, moves,
    attacks, reconciles, and later recovers visual routing.

- [ ] **P10-05 — Finalize per-peer route diagnostics.**
  - Purpose: Distinguish local owner, authority collision, accepted input, direct
    visual, quarantine, and fallback quality without leaking secrets.
  - Target files: `NetworkPlayerHud.cs`, `PredictionMeshDriver.cs`,
    `PredictionDiagnosticsTests.cs`.
  - Verification: 40/120/200 ms and route-failure scenarios show correct per-peer
    status and secret scan passes.

## Phase 11 — Acceptance, Soak, and Performance

- [ ] **P11-01 — Add the full deterministic impairment matrix.**
  - Purpose: Run owner locomotion/action scenarios across required RTT, jitter,
    random/burst loss, reorder, duplicate, asymmetry, and player-count profiles.
  - Target files: `verify_owner_prediction_matrix.ps1`,
    `OwnerPredictionMatrixAssertions.cs`, `NetworkArena.cs`.
  - Verification: Every matrix cell records corrections, transitions, replays,
    hard reasons, packet bytes, and outcome.

- [ ] **P11-02 — Add clock/stall/catch-up acceptance.**
  - Purpose: Exercise positive/negative clock drift, confidence loss, render/
    physics/Steam callback stalls, authority hitches, and catch-up exhaustion.
  - Target files: `verify_prediction_clock_stalls.ps1`,
    `PredictionClockStallAssertions.cs`.
  - Verification: No missing/duplicate frame, unbounded lead oscillation, or
    implicit timeline reset occurs.

- [ ] **P11-03 — Add long randomized prediction soak.**
  - Purpose: Detect history corruption, leaked journals, NaNs, invalid profiles,
    and unexplained hard snaps over long legal input.
  - Target files: `verify_owner_prediction_soak.ps1`,
    `OwnerPredictionSoakAssertions.cs`.
  - Verification: Thirty-minute seeded profiles complete with bounded memory and
    every durable intent terminal.

- [ ] **P11-04 — Enforce replay CPU/allocation budgets.**
  - Purpose: Gate p50/p95/p99/worst replay cost and post-warmup allocations on a
    representative CPU for 1/3/8 players.
  - Target files: `verify_prediction_performance.ps1`,
    `PredictionPerformanceAssertions.cs`.
  - Verification: Measured Phase 1 budgets pass; failures identify normal versus
    causal-island versus all-player reference cost.

- [ ] **P11-05 — Add Windows/Linux trace compatibility.**
  - Purpose: Prove reproducible/correctable behavior and content-hash compatibility
    across supported platforms without claiming bit determinism.
  - Target files: `verify_cross_platform_prediction.ps1`,
    `CrossPlatformPredictionAssertions.cs`.
  - Verification: Same-build traces stay within declared tolerances and collision
    content mismatch rejects join cleanly.

- [ ] **P11-06 — Promote prediction gates into the parity suite.**
  - Purpose: Make exact frame accounting, zero replay side effects, lifecycle,
    attack grammar, fallback, and correction targets mandatory after every change.
  - Target files: `verify_multiplayer_parity.ps1`,
    `MULTIPLAYER_PARITY_CHECKLIST.md`, `NetworkArena.cs`.
  - Verification: Full parity passes and each deliberately injected regression
    fails the expected assertion.

## Phase 12 — Documentation and Steam Alpha Rollout

- [ ] **P12-01 — Update movement and multiplayer architecture decisions.**
  - Purpose: Replace legacy `MoveAndSlide` rollback, newest-input compaction, and
    stale remote collision assumptions with the accepted implementation.
  - Target files: `MOVEMENT_ARCHITECTURE.md`,
    `MULTIPLAYER_ARCHITECTURE.md`,
    `REMOTE_MOVEMENT_PREDICTION_ARCHITECTURE.md`.
  - Verification: Documentation consistency search finds no conflicting confirmed
    policy or stale completion status.

- [ ] **P12-02 — Update playtest, bug, and parity documentation.**
  - Purpose: Link implemented fixes and measured evidence to Brazil bugs and the
    combat/multiplayer acceptance process.
  - Target files: `MULTIPLAYER_COMBAT_PLAYTEST_PLAN.md`,
    `STEAM_BRAZIL_PLAYTEST_BUG_REPORT.md`,
    `MULTIPLAYER_PARITY_CHECKLIST.md`.
  - Verification: Every bug has implementation steps, evidence, status, and an
    explicit remaining external acceptance item.

- [ ] **P12-03 — Run release/export/readiness gates.**
  - Purpose: Produce one protocol-next Windows Steam build using the known custom
    Godot/Steam/C# runtime without credentials entering source or staging.
  - Target files: `verify_steam_windows_readiness.ps1`,
    `export_windows.ps1`, `upload_windows.ps1`.
  - Verification: Tests, recursive secret scan, export, staged-content validation,
    launch executable check, and dry-run upload readiness pass.

- [ ] **P12-04 — Complete near-region two-account Steam acceptance.**
  - Purpose: Validate invite/join, host/client role swap, movement, attacks,
    contact, death/respawn, reconnect/fallback, and trace collection before Brazil.
  - Target files: `STEAM_ALPHA_PLAYTEST_CHECKLIST.md`,
    `STEAM_BRAZIL_PLAYTEST_BUG_REPORT.md`.
  - Verification: Both accounts complete the scripted checklist on the recorded
    build with no critical parity issue.

- [ ] **P12-05 — Complete Brazil role-swapped alpha acceptance.**
  - Purpose: Prove the actual target experience with synchronized video/traces and
    user-approved feel before making V2 default.
  - Target files: `STEAM_ALPHA_PLAYTEST_CHECKLIST.md`,
    `STEAM_BRAZIL_PLAYTEST_BUG_REPORT.md`,
    `LOCAL_OWNER_PREDICTION_REDESIGN.md`.
  - Verification: Both authority assignments pass the agreed movement, camera,
    attack, fairness, contact, and lifecycle gates; build/source/protocol IDs and
    residual issues are recorded.

## Known Pre-Existing Issues

Not introduced by this plan, but they affect its gates and should not be
mistaken for regressions by a later session.

- [x] **PX-01 — RESOLVED by P06-A5, not by stabilizing the probes.**
  - Four of the five members lived in `PredictionPerformanceProbe` and its tests,
    which P06-A5 deleted along with the model they measured. The multiplayer suite
    now passes on repeated consecutive runs, and the correction taxonomy can be
    extended again — which was the thing this had started to block.
  - Worth noting how it was fixed: not by making the flaky measurement stable, but
    by deleting the thing being measured once it stopped being useful. The
    allocation ceilings were guarding a model that P5B-03 superseded with a real
    in-engine measurement. Original entry follows.
  - Purpose: Three Phase 1 allocation tests measure GC allocation without enough
    warm-up isolation, so they fail on a cold run immediately after a build and
    pass on warm re-runs and in isolation. They will intermittently red a CI run
    and have already cost several false alarms during Phase 4.
  - Affected tests: `PredictionPerformanceProbeTests.MemoryLayoutMatchesStateCommandResultAndDependencyOwnership`,
    `PredictionPerformanceProbeTests.AllocationScopesCannotCollapseUnknownNativeMemoryToZero`,
    `PredictedCueLedgerTests.FixedStepCueOperationsAllocateNothingAfterWarmup`,
    `PredictionTelemetryRingTests.ConcreteEnumerationAndSpanCopyDoNotAllocate`.
  - The fourth was found during P04-09: adding roughly thirty tests changed
    execution order and it began failing on full runs while passing in isolation
    and on rerun. That is direct evidence the family is order-dependent rather
    than cold-start-dependent, and that the set will keep growing as the suite
    does.
  - **A fifth was found during P06-A4, and it changes this item's severity from
    cosmetic to blocking.** Adding one value to `OwnerCorrectionReason` — which
    adds exactly one `Theory` case, 951 tests to 952 — moved
    `PredictionPerformanceProbeTests.MemoryLayoutMatchesStateCommandResultAndDependencyOwnership`
    from passing to failing: `PreallocatedManagedOverheadBytes` measured 4848
    against an asserted ceiling of 4096. Verified as ordering, not a real
    regression: the class passes 10/10 in isolation, and stashing the change
    restores 951/951. So the correction-reason taxonomy — a Phase 1 foundation
    that Phases 6 through 9 all extend — **cannot currently be extended without
    reding the build**, and the failure names a memory-layout ceiling rather than
    anything to do with the change, which is the worst possible signal to hand
    whoever hits it next.
  - Recommended fix direction, from what the five members have in common: they
    assert absolute allocation ceilings measured by observing the GC in a shared
    process. That is not a stable measurement under xUnit's ordering. Either give
    them a dedicated collection with a forced warm-up and no parallelism, or
    assert deltas against a baseline captured in the same test rather than
    absolute byte ceilings authored months earlier.
  - Verification: The full multiplayer suite passes on a cold run immediately
    after a clean build, repeatedly, without per-test ordering assumptions — and
    specifically, adding one enumeration value to the correction taxonomy does not
    change any allocation measurement.
