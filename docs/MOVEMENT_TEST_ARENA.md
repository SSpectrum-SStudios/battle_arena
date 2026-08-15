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
