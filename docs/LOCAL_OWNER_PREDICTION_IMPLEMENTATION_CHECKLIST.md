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

- [ ] **P05-01 — Define compact kinematic and contact state.**
  - Purpose: Own position, velocity, facing, grounded, normal, support behavior,
    and stable contact facts in replayable value state.
  - Target files: `CharacterKinematicState.cs`,
    `CollisionContactState.cs`, `CharacterKinematicStateTests.cs`.
  - Verification: Finite-value, tolerant-contact, seam-equivalence, and copy
    semantics pass without per-frame allocation.

- [ ] **P05-02 — Define collision profile state and bounds.**
  - Purpose: Represent standing/crouching/rolling profiles by stable identity and
    validated dimensions.
  - Target files: `CollisionProfileState.cs`,
    `CollisionProfileStateTests.cs`.
  - Verification: Profile validation and legal shrink/expansion intent tests pass.

- [ ] **P05-03 — Define bounded movement-source state.**
  - Purpose: Store replayable lunge, roll, dash, knockback, pull, and future item
    motion in a fixed-capacity value buffer.
  - Target files: `MovementSourceState.cs`, `MovementSourceBuffer.cs`,
    `MovementSourceBufferTests.cs`.
  - Verification: Capacity, stable ordering, lifecycle, copy, aggregation, and
    no-allocation tests pass.

- [ ] **P05-04 — Define the aggregate character simulation state.**
  - Purpose: Combine movement, action, contact, profile, sources, revisions, and
    deterministic counters into the complete rewind unit.
  - Target files: `CharacterSimulationState.cs`,
    `CharacterSimulationStateTests.cs`.
  - Verification: Complete-copy/equality tests prove no prediction-relevant field
    is omitted and authority-only hit/damage state cannot be stored.

- [ ] **P05-05 — Define query-neutral collision contracts.**
  - Purpose: Isolate sweep, ground probe, clearance, and support motion from Godot
    nodes and presentation.
  - Target files: `ICharacterCollisionWorld.cs`,
    `CharacterCollisionContracts.cs`, `CharacterCollisionContractsTests.cs`.
  - Verification: Contract validation and deterministic fake-world tests pass.

- [ ] **P05-06 — Implement the static Godot query adapter.**
  - Purpose: Execute explicit-transform capsule queries with precreated profile
    RIDs, static-only masks, exclusions, and reusable buffers.
  - Target files: `GodotKinematicCollisionWorld.cs`,
    `GodotKinematicCollisionWorldTests.cs`, `kinematic_query_probe.tscn`.
  - Verification: Headless probe proves no live-node movement, no dynamic hits,
    reusable results, and same-frame query/commit behavior.

- [ ] **P05-07 — Implement bounded sweep and penetration recovery.**
  - Purpose: Resolve desired capsule travel and recover legal shallow overlap from
    explicit state.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleSweepSimulatorTests.cs`.
  - Verification: Free travel, wall impact, high speed, starting overlap, bounded
    iterations, and unrecoverable penetration pass.

- [ ] **P05-08 — Implement stable slide resolution.**
  - Purpose: Resolve multiple contacts in stable fraction/collider/shape/normal
    order without node iteration dependence.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleSlideSimulatorTests.cs`.
  - Verification: Wall slide, convex/concave corner, reorder, duplicate contact,
    and iteration-cap traces pass.

- [ ] **P05-09 — Implement slope classification and ground snap.**
  - Purpose: Reproduce walkable/unwalkable slopes, explicit ground probing, and
    no snap while rising.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleSlopeAndSnapTests.cs`.
  - Verification: Slope thresholds, descent, edge departure, rising jump, seam,
    and normal tolerance tests pass.

- [ ] **P05-10 — Implement explicit stair/step solving.**
  - Purpose: Generalize up-forward-down stepping for direct and strafing entry
    without changing the map to hide lips.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleStepSolverTests.cs`.
  - Verification: Thin/thick ramps, every stair direction, shallow lips, blocked
    headroom, and no-progress rejection pass.

- [ ] **P05-11 — Implement ceiling and profile-clearance rules.**
  - Purpose: Stop upward motion on ceilings and allow profile expansion only when
    static/player clearance permits it.
  - Target files: `CapsuleMovementSimulator.cs`,
    `CapsuleClearanceTests.cs`.
  - Verification: Ceiling hit, crouch/roll shrink, blocked stand, delayed stand,
    and forced-expansion policy pass.

- [ ] **P05-12 — Compose ground and airborne locomotion.**
  - Purpose: Reuse accepted acceleration, run/sprint, momentum, air-control, and
    falling rules through the explicit motor.
  - Target files: `CharacterMovementSimulator.cs`,
    `CharacterLocomotionIntegrationTests.cs`, `CapsuleMovementSimulator.cs`.
  - Verification: Existing movement golden curves match accepted tolerances.

- [ ] **P05-13 — Integrate jump state and durable transitions.**
  - Purpose: Preserve variable hold, coyote, buffering, apex, fast fall, momentum,
    lateral control, and air sprint under restore/replay.
  - Target files: `CharacterMovementSimulator.cs`,
    `CharacterJumpReplayTests.cs`, `JumpFallSimulator.cs`.
  - Verification: Restore/replay from every jump frame yields the same final
    canonical state.

- [ ] **P05-14 — Integrate crouch and roll state.**
  - Purpose: Preserve hold-to-crouch, moving roll, tap/hold duration, momentum,
    steering, cooldown, landing roll, and non-cancelability.
  - Target files: `CharacterMovementSimulator.cs`,
    `CharacterRollReplayTests.cs`, `CrouchRollSimulator.cs`.
  - Verification: Restore/replay and accepted roll-distance/timing traces pass.

- [ ] **P05-15 — Integrate replayable external movement sources.**
  - Purpose: Apply source curves in the canonical frame order instead of mutating
    node velocity once.
  - Target files: `MovementSourceSimulator.cs`,
    `MovementSourceSimulatorTests.cs`, `CharacterMovementSimulator.cs`.
  - Verification: Start/progress/stack/end, F-start lunge, and F+1 hit-knockback
    traces pass.

- [ ] **P05-16 — Add the dual-motor offline test adapter.**
  - Purpose: Let the existing movement arena switch between Legacy and
    ExplicitQueryMotor without changing accepted content values.
  - Target files: `MovementTestPlayer.cs`, `movement_test_arena.tscn`,
    `MOVEMENT_TEST_ARENA.md`.
  - Verification: Both modes launch headlessly and the selected mode is visible
    in diagnostics.

- [ ] **P05-17 — Lock the explicit-motor golden suite.**
  - Purpose: Cover the complete arena course and 10,000-frame restore/replay
    reproducibility before networking cutover.
  - Target files: `ExplicitMotorGoldenTraceTests.cs`,
    `MovementGoldenTraceData.cs`, `run_movement_golden_tests.ps1`.
  - Verification: All accepted run/sprint/jump/roll/stair/ramp/wall/ceiling traces
    and every replay pivot pass with zero unexplained divergence.

## Phase 6 — Owner History and Exact Reconciliation

- [ ] **P06-01 — Implement the bounded owner prediction history.**
  - Purpose: Store complete pre/post state, command, revisions, contacts, events,
    and diagnostics in a preallocated frame-indexed ring.
  - Target files: `OwnerPredictionHistory.cs`,
    `OwnerPredictionHistoryTests.cs`.
  - Verification: Insert/find/prune/wrap/epoch/reset/history-miss and no-allocation
    tests pass.

- [ ] **P06-02 — Implement canonical diagnostic state hashing.**
  - Purpose: Produce versioned XxHash64 diagnostics from explicitly ordered,
    quantized fields rather than raw floats or Protobuf bytes.
  - Target files: `CanonicalMovementStateHash.cs`,
    `CanonicalMovementStateHashTests.cs`.
  - Verification: Golden bytes/hash, field inclusion/exclusion, source ordering,
    seam tolerance, and schema-version tests pass.

- [ ] **P06-03 — Implement tolerant owner state comparison.**
  - Purpose: Separate exact gameplay-discrete mismatches from numeric tolerance
    and diagnostic-only manifold differences.
  - Target files: `OwnerReconciliationComparer.cs`,
    `OwnerReconciliationComparerTests.cs`.
  - Verification: Position/velocity/facing/contact thresholds, seam equivalence,
    support/profile/action mismatch, and first-field diagnostics pass.

- [ ] **P06-04 — Implement correction classification policy.**
  - Purpose: Choose confirmed, ordinary replay, contact replay, or hard local
    rebase without smoothing collision truth.
  - Target files: `OwnerReconciliationPolicy.cs`,
    `OwnerReconciliationPolicyTests.cs`.
  - Verification: Boundary and enumerated-reason tests pass; no raw hash alone
    triggers correction.

- [ ] **P06-05 — Integrate tick-effective movement configuration lookup.**
  - Purpose: Resolve the canonical revision for every first-run/replay frame and
    queue baselines when definitions are briefly missing.
  - Target files: `MovementConfigurationTimeline.cs`,
    `MovementConfigurationTimelineTests.cs`, `CharacterMovementSimulator.cs`.
  - Verification: Same-frame revision changes replay exactly; valid intent is not
    rejected merely for a client's stale claimed revision.

- [ ] **P06-06 — Implement static-world local reconciliation.**
  - Purpose: Restore the exact authority frame and replay later owner commands
    through simulation only.
  - Target files: `LocalMovementPredictionController.cs`,
    `LocalMovementPredictionControllerTests.cs`, `OwnerPredictionHistory.cs`.
  - Verification: Confirmation prunes without replay; injected errors replay the
    correct frames and converge.

- [ ] **P06-07 — Implement queued-future and hard-rebase handling.**
  - Purpose: Queue authority frames ahead of local simulation and perform one
    clean local rebase for missing history/epoch/penetration failures.
  - Target files: `LocalMovementPredictionController.cs`,
    `LocalPredictionRebaseTests.cs`, `CombatantPredictionEpochGate.cs`.
  - Verification: Out-of-order/future/old states cannot regress; each hard rebase
    has one enumerated reason and clears bounded state atomically.

- [ ] **P06-08 — Finalize the owner-baseline Protobuf state.**
  - Purpose: Encode the proven Phase 5 state, ACKs, journals, lead policy, and hash
    schema without authority-only hit/damage fields.
  - Target files: `authority_state.proto`, `ProtobufProtocolCodecTests.cs`,
    `InboundMessageValidatorTests.cs`.
  - Verification: Full round trip, reserved-number, fuzz, bounds, and packet-size
    gates pass.

- [ ] **P06-09 — Finalize the compact collision-world Protobuf state.**
  - Purpose: Encode self-contained quantized frame state, bounded contacts/sources,
    and deterministic partition identity.
  - Target files: `authority_state.proto`,
    `MovementWorldProtocolTests.cs`, `InboundMessageValidator.cs`.
  - Verification: 1/3/8-player round trips, incomplete-part discard, next-frame
    supersession, and byte ceilings pass.

- [ ] **P06-10 — Add commit-once Godot owner adapter.**
  - Purpose: Reconcile/replay in value state, then commit one final collision pose
    and publish one presentation sample per real frame.
  - Target files: `GodotOwnerPredictionAdapter.cs`, `NetworkAvatar.cs`,
    `GodotOwnerPredictionAdapterTests.cs`.
  - Verification: N-frame replay performs one body commit, one presentation
    sample, and zero historical node/cue operations.

- [ ] **P06-11 — Integrate static-only V2 owner prediction.**
  - Purpose: Exercise exact history/reconciliation in the arena while explicitly
    disabling/softening predicted player collision until Phase 9.
  - Target files: `NetworkArena.cs`, `NetworkAvatar.cs`,
    `verify_multiplayer_parity.ps1`.
  - Verification: Host/client unopposed locomotion passes at 0/80/120/200 ms with
    local input latency unchanged; V2 cannot become alpha default.

- [ ] **P06-12 — Add separate-process canonical trace parity.**
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

- [ ] **PX-01 — Stabilize the order-dependent allocation probes.**
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
  - Verification: The full multiplayer suite passes on a cold run immediately
    after a clean build, repeatedly, without per-test ordering assumptions.
