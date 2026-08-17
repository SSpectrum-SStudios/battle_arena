# Handoff — Phase 6 (Owner History and Exact Reconciliation)

Branch `ai_begin`, current at `edf9866`. Everything below is committed and pushed.

---

## Read these first, in this order

1. **`docs/LOCAL_OWNER_PREDICTION_IMPLEMENTATION_CHECKLIST.md`** — the source of
   truth. Do not start from this handoff alone. Read, in order:
   - `## Phase 6 — Owner History and Exact Reconciliation`, and inside it:
     - `### Phase 6 revision: Phase 6A foundations` — the architecture decision and
       the research behind it.
     - `### Stub review of 6A` — three blockers, two fixed and one still open.
     - `### Plan refresh: what Phase 5B changed` and `### Stub review of the
       refresh` — the four constraints the reconciliation layer must honour.
   - `P06-A1` (withdrawn), `P06-A1b`, `P06-A2`, `P06-A3a/b`, `P06-A4`, `P06-A5`,
     `P06-A6`, then `P06-01` through `P06-12`.
   - `## Phase 5B` entries `P5B-01/02/03` — the measured evidence everything else
     cites.
   - The `Completion Rule` section — the working pattern.
2. **`docs/MOVEMENT_TEST_ARENA.md`** — especially *"Which stations actually have
   collision"*. Two arena stations do not test what their names suggest, and that
   fact has already produced one set of decorative gates.
3. **The three probe sources**, if touching the motor:
   `scripts/movement/GodotKinematicCollisionWorldProbe.cs`,
   `MovementMotorParityProbe.cs`, `MovementMotorCostProbe.cs`.

Skim `git log --oneline -15` too. The commit messages carry reasoning that is not
duplicated anywhere else.

---

## State

| Suite / gate | Result |
| --- | --- |
| `BattleArena.Core.Tests` | 221/221 |
| `BattleArena.Multiplayer.Tests` | 948/948 |
| Collision adapter probe | green (Jolt) |
| Motor parity probe | green (Jolt) |
| Motor cost probe | green (Jolt) |

All three probes run from `tools/testing/verify_multiplayer_parity.ps1`.

**Done:** Phases 0–5, Phase 5B, Phase 6A (except the Steam authority transport),
and Phase 6B subsections P06-01 through P06-05.

**Not done:** P06-06 through P06-12, the Steam authority transport, `P05-18`, and
the motor debt below.

---

## Decisions already made — do not re-litigate without new evidence

These were each argued at length and are recorded with their reasoning in the
checklist. Re-opening them costs a lot and has already been done once.

- **Godot's physics is the query service; only the integration step is ours.**
  The reason is not testability and not determinism. It is that
  `PhysicsServer3D::space_step()` is not public API, so Godot cannot re-step its
  world N times in one frame — which makes an engine-integrated `RigidBody3D`
  unusable for rollback, while queries have no such restriction.
- **Do not build an in-process collision world.** Proposed, then withdrawn. See
  `P06-A1` for why, including the premise that was smuggled in (bit-determinism
  treated as a requirement when reconciliation exists precisely because it cannot
  be guaranteed).
- **Jolt is the physics engine** (`project.godot: 3d/physics_engine`). It made
  motion queries ~2.5x cheaper and moved replay depth 16 from unaffordable to 42%
  of budget.
- **Replay depth cap is 16**, measured, enforced bidirectionally by the cost probe.
- **Protobuf stays.** Intentional choice for versioned wire contracts.
- **Steam is the target; ENet is the local harness.** Prediction transport is split
  into control and movement planes on adjacent virtual ports.

---

## Start here

**Run a critic pass over P06-01..05 before building anything on top.**

Nine subsections have been implemented without one. The pattern calls for it, and
its record in this project is that it found blockers in *both* previously
"finished" pieces of work — including one where the tests themselves encoded the
bug and passed. The comparer and policy are what every remaining subsection builds
on, so a defect there propagates into all of them.

Scope the critic to: `OwnerPredictionHistory.cs`, `OwnerReconciliation.cs`,
`OwnerReplayConfigurationContext.cs`, `SceneColliderIdentity.cs`,
`FrameContactRecord.cs`, `GodotSteamPredictionMeshTransport.cs`, and their tests.
Tell it 5B is complete and that P06-A1 was withdrawn, or it will re-propose it.

Then, in order:

1. **P06-06** — static-world reconciliation. Where the pieces meet: apply an
   authority answer, compare, then confirm, replay, or rebase.
2. **P06-07** — queued-future and hard-rebase. Fold in the queued-future consumer:
   `MaximumQueuedFutureStates` and `QueuedFutureCount` exist, but nothing queues,
   nothing drains, and no type holds an entry.
3. **P06-08 / P06-09** — the two Protobuf finalizations, deliberately last so the
   wire encodes what reconciliation actually needed rather than a guess.
4. **P06-10 / 11 / 12** — commit-once Godot adapter, V2 integration, cross-process
   trace parity.

---

## Open items and known traps

**Open blocker (recorded, not fixed):** `P06-A6`'s Steam authority transport. The
authority path is still ENet-only; only the prediction mesh runs over Steam.

**Carried debt, each with a home in the checklist:**

- Duplicate/reordered authority answers. `PruneThrough` drops confirmed frames, so
  a retransmitted state for a confirmed frame becomes `HistoryMiss` → `HardRebase`
  — a duplicate packet produces a visible snap. `OwnerHistoryLookupDecision` cannot
  currently distinguish "gone because confirmed" from "gone because aged out", and
  those want opposite responses. Fold into P06-06 or P06-07.
- `P05-18` attack movement influence. Phase 5 is marked complete but this is
  unchecked, and the parity probe explicitly scopes attacks out — so "the motors
  agree" means "with no attack active", which is most of what a playtest does.
- Motor debt: `RecoverPenetration` scales the depenetration push by `1 + skin`
  instead of adding it. The obvious correction was tried and measured and it broke
  coasting (braking 0.513 m → 0.002 m), so it is documented rather than shipped
  half-understood. Also a climbable ledge shallower than one capsule radius can no
  longer be stepped onto.

**Traps that have already cost time:**

- **The arena's visible stairs have no collision.** Every step in `BuildStairs` is
  `collisionEnabled: false`; the only collider is a smooth ramp. A "stair climb"
  test there exercises no step solver and stays green with it fully broken. Use the
  0.35 m ledge at Z = 32.
- **The slalom has a clear corridor down the middle.** A 0.42 m capsule between
  0.55 m cylinders at X = −12 and X = −8 touches nothing at X ≈ −10.5. Aim at an
  obstacle *and* off its axis — dead centre gives a head-on stop with zero lateral
  deflection.
- **`string.GetHashCode` is per-process randomised in .NET.** Using it for any
  identity that crosses processes produces something that looks stable and passes
  every single-process test. This is how the RID identity bug survived four phases.
- **A probe that queries before the physics space is populated passes vacuously.**
  Every probe settles frames first and asserts geometry exists before anything
  else.
- **Four order-dependent allocation tests were deleted with their models** (PX-01).
  If a new allocation-ceiling test appears, expect it to be order-dependent.

---

## The rule that came out of this session

**Prefer a headless probe against the real engine over a model of the engine.**

Phase 1 produced evidence by building models. Phase 5B produced better evidence in
a fraction of the code by running the real thing headlessly and asserting on it.
Every place this plan modelled something instead of measuring it, the model turned
out to be both larger and less accurate — ~3,100 lines of them were deleted in
`P06-A5`, and the withdrawn `P06-A1` was about to repeat the mistake at the scale
of a physics engine.

Corollary, learned the same way: **a gate that cannot fail is worse than no gate.**
Three of the motor parity probe's seven metrics originally measured open ground
while claiming to measure obstacles, ceilings and steps. Always ask what would have
to break for a green gate to go red, and check that the answer is not "nothing".
