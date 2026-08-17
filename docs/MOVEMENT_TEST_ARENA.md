# Movement Test Arena

The movement laboratory lives at
`res://scenes/movement/movement_test_arena.tscn`. Run that scene directly from
the Godot editor to test movement without entering the multiplayer launcher.

## Current Controls

- WASD or left stick: camera-relative movement
- Left Ctrl or controller L3: sprint
- Space or controller A: jump; release early for a shorter hop
- Hold Left Shift or controller B while stationary: crouch
- Press Left Shift or controller B while moving: committed roll
- Mouse or right stick: camera orbit
- Escape: release the mouse
- Click inside the game: recapture the mouse without performing an action
- Backspace: reset to the starting position

Controls use the shared remappable input actions. These are defaults, not
hard-coded gameplay keys.

## Test Stations

- Open turning area for starts, stops, reversals, and camera orbit.
- 50-meter marked lane for acceleration and braking comparisons.
- Alternating slalom markers for speed-dependent steering.
- Stairs and connected ramps for floor adhesion and slope behavior.
- Cross-slope platform for lateral movement on an incline.
- Narrow path for fine analog and camera-relative control.
- Unmodified 0.10 m, 0.20 m, and 0.35 m box ledges that can be approached from every direction.
- Purple crouch, roll, and blocked-clearance obstacles.

Live diagnostics show physical horizontal speed, configured run and sprint
targets, character-facing yaw, locomotion mode, vertical velocity, Godot floor
contact, and incidental step resolution. Falling out of the arena automatically
resets the character.

The dedicated slope stations use true convex wedge collision rather than
rotated boxes. Their low edges meet the ground without an above-ground vertical
lip, while their high edges are authored flush with their destination
platforms. Visible stair flights use a continuous hidden traversal surface;
isolated curbs continue to exercise the generic step-up motor.

The generic step-up motor does not depend on these authored traversal surfaces.
It performs capsule sweeps upward for clearance, forward along the complete
requested direction, and downward to acquire a walkable landing. A bounded
forward-assist distance handles tangent capsule contact at convex corners. The
candidate is rejected when it fails clearance, directional progress, landing
normal, or maximum-rise validation.

`res://scenes/movement/headless_step_probe.tscn` is the real-physics regression
suite for this boundary. It covers head-on, pure lateral, diagonal, walking
parallel before strafing onto a ledge, and an over-height negative case.

`res://scenes/movement/headless_jump_probe.tscn` validates the full jump and
early-release short hop against real Godot capsule collision. It verifies the
cartoonish peak-height range, a standing diagonal jump just beyond the 4-by-4
meter slalom-pillar route, weaker lateral-only travel, and that every path
returns cleanly to the floor.

`res://scenes/movement/headless_crouch_roll_probe.tscn` sends real remappable
input events through the movement player and verifies roll travel, committed
completion, hold crouching, and the 1.80/1.25/0.95-meter capsule profiles.

## Tuning

Base values are authored in
`res://assets/classes/fighter/movement.json`. The scene loads and validates that
profile at startup, then passes its immutable compiled attributes to the same
plain C# simulator intended for offline play, server authority, client
prediction, and reconciliation replay.

This arena uses the shared deterministic jump/fall simulator. The base fighter
has an intentionally cartoonish standing jump of roughly 3.9 meters, a modest
sprint-height bonus, smoothly blended rising/apex/falling gravity, a sharp descent, terminal velocity,
120 milliseconds of coyote time and input buffering, and a short-hop jump cut
when Space/A is released early. Forward/back air control is stronger than lateral control,
and steering authority decreases with speed. Walking off an edge enters the falling curve
immediately instead of producing a flat airborne glide. All timing is compiled
to integer simulation ticks for prediction and replay.

Sprint remains active in the air by design. A run jump stays at run speed unless
the player presses Sprint; doing so accelerates toward sprint speed and provides
additional airborne maneuvering authority without discarding takeoff momentum.
The contextual Crouch/Roll action uses actual horizontal velocity at the press
edge. Entry momentum remains the roll baseline and an authored temporary boost
is layered on top; completion removes only that boost. Releasing the input
completes at the 0.32-second minimum, while holding extends the roll to its
0.85-second maximum and substantially farther travel. Direction remains mostly
committed, and the 0.35-second cooldown begins after completion. Holding the
input while airborne with sufficient momentum queues a roll for landing;
releasing it before landing clears that queue. Leaving a ledge or hitting a wall
never cancels an active roll. Standing still requires a full-shape clearance
query.

Knight locomotion uses a synchronized two-dimensional animation blend driven by
actual velocity relative to character facing. Idle, walk, run, sprint, backward,
and lateral clips contribute continuously; an authored airborne clip blends in
when the simulation leaves the floor. The animation graph is presentation-only
and never changes authoritative movement.

Rolling uses the Quaternius Universal Animation Library's CC0 full-body `Roll`
clip. The presentation adapter retargets rotations relative to each skeleton's
rest pose, rescales the limited root/hip translation, and discards unmapped
tracks. This produces a genuine tuck rather than rotating the Knight as a rigid
object. The animation is timed against the simulated maximum roll; releasing
after the minimum exits early, while holding allows the full presentation and
farther simulated travel. Animation and imported root motion never change
authoritative displacement.

## Switching motors

The player node exports `MotorMode`, which selects which motor drives movement:

- **Legacy** (default) — the accepted Godot driver, using `MoveAndSlide` against
  a live `CharacterBody3D`. The arena behaves exactly as it always has.
- **ExplicitQueryMotor** — the Phase 5 explicit-state kinematic motor, resolving
  motion from replayable value state through explicit-transform queries.

Both modes read the same authored `movement.json`, so switching changes only how
the resulting motion is integrated against the world. Any difference in feel is
therefore the motor's doing and not a difference in tuning, which is what makes
the comparison worth running at all.

The selected mode is printed at startup as
`[MovementTestPlayer] Motor mode: <mode>`, so a headless run records which motor
produced its trace.

Under `ExplicitQueryMotor` the node is positioned *from* simulation each frame
and nothing reads the body transform back into state. That inversion is the point
of the phase: it is what allows a past frame to be restored and resimulated
without moving the character the player is looking at.

## Headless gates over this arena

Two probes run this course without a window, both wired into
`tools/testing/verify_multiplayer_parity.ps1`.

`res://scenes/movement/movement_motor_parity_probe.tscn` (P5B-02) drives both
motors over the course and compares what a player perceives: top run and sprint
speed, frames to reach top speed, braking distance, jump apex and airtime, ledge
climb height, crouched versus standing travel under a low tunnel, roll distance,
and forward travel plus lateral deflection against a slalom obstacle. Run it with
`tools/testing/run_movement_motor_parity.ps1`.

It does not assert per-frame position parity over a long trace. Both motors are
closed loops over different collision algorithms, so divergence compounds and any
tolerance wide enough to pass a long trace is wide enough to hide a regression.
Instead it gates the *path* at the best of five frame alignments and gates the
*phase* separately at one frame — the two motors are one frame out of phase by
integration order, and separating those questions is what keeps the gate
meaningful.

`res://scenes/movement/movement_motor_cost_probe.tscn` (P5B-03) measures the
explicit motor's query count and microseconds per frame at replay depths from 1 to
32, over open ground and a hostile corner, and fails when the 95th percentile
exceeds a quarter of a 60 Hz frame at or below the supported replay depth. Run it
with `tools/testing/run_movement_motor_cost.ps1`.

### Which stations actually have collision

Worth knowing before writing a gate against this course, because two stations do
not test what their names suggest:

- **The visible stair flight has no collision.** Every step in `BuildStairs` is
  created with `collisionEnabled: false`; the only collider is a smooth
  15-degree traversal ramp. A "stair climb" measured there is a walk up a slope
  and exercises no step solver at all. The parity probe's climb station uses the
  **0.35 m box ledge** at Z = 32 instead, which is a real step under the 0.4 m
  step height.
- **The slalom leaves a clear corridor down the middle.** With a 0.42 m capsule
  against 0.55 m cylinders at X = -12 and X = -8, a character running down X ≈
  -10.5 touches nothing. An obstacle gate has to be spawned on the obstacle line,
  and slightly off its axis — dead-centre gives a head-on stop with no lateral
  deflection at all.
