# Input and Controls

## Design Principle

Gameplay code reads semantic Godot input actions rather than physical keys, mouse buttons, or controller buttons. A future controls menu may replace any action's events without changing movement, combat, camera, or item code.

`VerticalSliceInput` currently installs a default only when the equivalent event is not already assigned. It also exposes methods to inspect and replace an action's runtime bindings. Persistent player profiles and the remapping user interface are intentionally deferred until after the combat scene is validated.

## Default Gameplay Bindings

| Action | Keyboard and mouse | Controller |
|---|---|---|
| Move | `W`, `A`, `S`, `D` | Left stick |
| Look | Mouse | Right stick |
| Jump | `Space` | A / Cross |
| Sprint | `Left Ctrl` | Left-stick click |
| Crouch / Roll | `Left Shift` | `B` |
| Attack | Left mouse | Right trigger |
| Switch camera | `V` | Right-stick click |
| Item activation 1 | `Q` | Left shoulder |
| Item activation 2 | `2` | Right shoulder |
| Item activation 3 | `3` | X / Square |
| Item activation 4 | `E` | Y / Triangle |
| Item activation 5 | `R` | D-pad down |
| Item activation 6 | `F` | D-pad up |

The vertical-slice poison aura is temporarily assigned to item activation 1. The other item actions are defined now so item scripts never need dedicated physical-key logic.

`Escape` releases the mouse during the desktop test. Clicking inside the game recaptures it. The attack action remains suppressed until that recapture click is physically released, preventing Godot's already-updated global action state from starting an attack. This is a local window-management convenience rather than an item or combat action.

## Remapping Boundary

A complete controls screen should:

1. Enumerate the semantic action catalog.
2. Capture keyboard, mouse, or controller events.
3. Detect conflicts and let the player confirm or cancel them.
4. Replace events through the binding service.
5. Save the player's bindings under `user://` rather than changing project files.
6. Restore device-appropriate defaults on request.

Prompts should resolve their displayed glyph or key name from the current binding instead of hard-coding labels such as `Q` or `A`.
