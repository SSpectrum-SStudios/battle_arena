# External Asset Workflow

## Purpose

Downloaded source packs and unused third-party assets live under `asset_staging/`. The entire directory is ignored by Git and contains a generated `.gdignore`, so Godot does not import its unused contents. It must never be referenced by a Godot scene or tracked resource. Only assets selected for the game are copied into a tracked game directory with their license and source record.

## Set Up the Asset Staging Area

From the repository root, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\assets\setup_external_assets.ps1
```

The script:

1. reads the tracked `tools/assets/external_assets.json` manifest;
2. creates `asset_staging/downloads` and `asset_staging/packages`;
3. downloads each commit-pinned source archive;
4. verifies its SHA-256 checksum before extraction;
5. extracts it into a stable package directory; and
6. writes an ignored local source record beside the extracted package.

Use `-Force` to download and prepare every package again. The script verifies every recursive deletion target before modifying it.

## Initial KayKit Source

The initial automated source is the official KayKit Character Pack: Adventurers 1.0 archive at commit `672074b73ba276876a19e8816ecdc5241817ab47`. It is also distributed through the Godot Asset Library and contains four rigged stylized fantasy characters, weapons, textures, and 75 animations. The pack is CC0-1.0.

The newer itch.io Adventurers and Character Animations downloads do not currently expose durable public archive URLs. Do not put temporary signed itch.io CDN URLs in the manifest. We may evaluate those versions manually and add them when a reproducible source is available.

## Promote an Asset Into the Game

Godot content must not depend on `asset_staging/`. When an asset is approved:

1. copy only the required GLB/GLTF, textures, and animation resources into `assets/third_party/kaykit/`;
2. preserve relative texture dependencies where applicable;
3. copy the source pack's `LICENSE.txt` into the tracked KayKit directory;
4. add a tracked source record containing the manifest ID, pinned version, original path, and any import changes;
5. configure Godot import settings in the tracked destination; and
6. verify a clean clone can run setup, import the promoted files, and build without referencing the ignored staging directory.

Do not commit archive files, unused character variants, unused animations, or `.godot/imported` cache files.
