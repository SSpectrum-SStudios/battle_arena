# Steam Windows Build Pipeline

Battle Arena uses Steam App ID `3820160` and Windows Depot ID `3820161`. Steam is an additional discovery and transport path; offline play and direct IP/ENet joining remain available.

## Local export

Run the following from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\steam\export_windows.ps1 -Release
```

The script exports to `build\steam\windows`, ensures `steam_api64.dll` is present, and creates `steam_appid.txt` for launching the build directly outside Steam. The App ID file is excluded from the depot upload.

The Godot executable can be overridden with `-GodotExecutable` or the `GODOT_MONO_CONSOLE` environment variable. The export preset defaults to the tracked `Steam Windows` release preset and can be overridden with `-Preset`.

## Upload to SteamPipe

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\steam\upload_windows.ps1 -SteamAccount YOUR_STEAMWORKS_ACCOUNT
```

The script uses the SDK at `C:\Users\Robert\Downloads\steamworks_sdk_162\sdk` by default. Override it with `-SteamworksSdk` or `STEAMWORKS_SDK`.

SteamCMD handles password and Steam Guard prompts interactively. Never place a password, Steam Guard code, or API key in a script or repository file.

The upload creates a Steamworks build but does not make it live. In Steamworks, assign the uploaded build to a password-protected test branch and grant both tester accounts access before attempting a two-computer test.

## Multiplayer test

1. Launch the game through Steam on two different Steam accounts.
2. On the host, select **Host Steam**, then use **Invite Friends** or share the displayed lobby ID.
3. The second player accepts the invitation, or pastes the lobby ID and selects **Join Steam**.
4. Once the host sees the remote player, select **Start Match**.

Steam P2P cannot be meaningfully tested with two processes signed into the same Steam account. Continue using the direct IP buttons for same-computer regression tests.
