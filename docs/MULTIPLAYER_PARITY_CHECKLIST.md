# Multiplayer Parity Gate

## Rule

Every change to movement, camera, combat, avatar presentation, health, life
state, spawning, or respawning must be evaluated for both the authority's local
player and a remote client's local player. Work in these areas is not complete
when it succeeds on only one ownership path.

The authority and owning client share the same simulation and presentation
policies. Networking may produce reconciliation corrections, but it must not
create a different control scheme, camera rule, animation transition, health
display, or life-cycle result.

## Automated Gate

Run from the repository root:

```powershell
.\tools\testing\verify_multiplayer_parity.ps1
```

This gate compiles the game, runs the core and multiplayer test suites, then
launches a frame-capped two-process ENet test covering connection, client
prediction, action delivery, authoritative hit resolution, and replicated
damage. It also requires the client to receive the 60 Hz compact authoritative
movement stream and authority-accepted command relay, then complete
authority-clock synchronization. The two-process client deliberately disables
prediction transport startup and must still enter and complete combat through
the mandatory authority relay. Finally, it runs an adversarial three-process
test that accumulates Combatant 3 input at the authority and verifies that the
newest intent is applied immediately instead of replaying a stale FIFO, while
the owner and the other client both see movement. The three-process test also
forces one direct-route failure, verifies authority fallback and reauthentication,
requires live direct movement bundles, and requires a measured direct RTT. It is
required after relevant code changes,
but it cannot determine whether two rendered animations or cameras feel
visually identical.

The focused three-process regression can also be run by itself:

```powershell
.\tools\testing\verify_three_player_movement.ps1
```

## Visible Two-Instance Gate

Launch the visible playtest with:

```powershell
.\tools\testing\launch_local_playtest.ps1
```

For one authority and two clients:

```powershell
.\tools\testing\launch_local_playtest.ps1 -ClientCount 2
```

During the three-instance test, verify that each client reports a synchronized
authority clock, continuously increasing movement-frame count, and normally a
one-tick adaptive presentation delay on localhost. The accepted-relay count
must also increase continuously, and `predicted` should become nonzero while a
remote player is moving. Compare Client A's movement
as rendered by Client B with the same movement rendered by the authority.
The `DIRECT MESH` row should show one authenticated route, a measured RTT,
increasing confirmations, no routine mismatch warning, and fallback only while
the deliberately failed route is recovering.

Verify the following from both windows:

- Mouse and controller look orbit the idle local character without turning the
  body.
- Movement, jump, sprint, crouch, roll, attack, and cancel inputs begin
  immediately and follow the same facing rules.
- Local movement animation, attack animation, death animation, and respawn
  animation/state transitions match.
- Authority confirmation and snapshot repair do not restart an attack clip or
  briefly return the character to locomotion in the middle of an attack.
- The local screen-space health HUD is present from initial spawn through
  damage, death, and every respawn.
- Every world-space name and health display is restored after respawn and is
  never replaced by diagnostic text.
- Camera collision, close-model hiding, player collision, and respawn-camera
  travel behave the same.
- Reconciliation does not rewind the owning client's camera or create visible
  body-facing snaps.

Record any difference as a parity bug even when the authoritative simulation
itself is correct.

## Steam Windows Readiness

Run the credential-free release gate from the repository root:

```powershell
.\tools\testing\verify_steam_windows_readiness.ps1
```

It reruns multiplayer parity, creates a release export, and verifies the staged
Windows PE, Steam runtime DLL, AppID `3820160`, DepotID `3820161`, resolved
content root, `steam_appid.txt` depot exclusion, and absence of obvious secret
files anywhere in the staged depot tree. It does not log into SteamCMD or upload
anything.

Steam transport validation still requires a manual two-account playtest. Check
host/join/invite, direct-route authentication, measured RTT, authority fallback,
movement/attack/death/respawn parity, and clean shutdown on two separate Steam
accounts before promoting a build beyond the alpha branch.
