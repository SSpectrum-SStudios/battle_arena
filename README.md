# battle_arena

## Building Custom Godot Editor

### Requirments

- Visual Studo 2019 or 2022 installed. Make sure to enable C++ in the list of workflows to install.
- Python 3.8+
- .NET sdk found [here](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-8.0.410-windows-x64-installer).
- Scons
- Git

### Installing Scons

You can either install Scons with a virtual python environment, or you can create a virtual environment. These instructions will walk through creating a python virtual envrionment. These instructions are assuming that you're using Windows Powershell. If you want to install scons onto your base python environment you can skip to step 3.

1. Create a folder for all of the different repos you'll be pulling and for the environment. Open powershell in this folder.

2. Create the python environment running `python -m venv .venv`. Now activate the venv by running `.\.venv\Scripts\Activate.ps1`. If you get an error trying to run the `Activate.ps1` file, you will need to enable script running for Powershell. You can make it so that only the current open terminal is allowed to run scripts by running `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`. Then run the activation again. You should see a little `(.venv)` appear on the left side fo your Powershell window.

3. With the python venv activated you can now install Scons by running `pip install scons`. If you chose to install it on your base python environment you can run `python -m pip install scons` instead.

### Compiling the Editor

1. Clone godot 4.4 stable `git clone https://github.com/godotengine/godot.git -b 4.4-stable godot`

2. Navigate to the modules folder: `cd godot/modules`. Clone the GodotSteam repository into the modules folder. `git clone https://github.com/GodotSteam/GodotSteam.git -b godot4 godotsteam`

3. Clone the GodotSteamMultiplayerPeer repository into the modules folder. `git clone https://github.com/GodotSteam/MultiplayerPeer.git -b main godotsteam_multiplayer_peer`

4. Extract the SDK and copy the public and redistributable_bin folders to `godot/modules/godotsteam/sdk/`. Your directory structure should look like:
```
godot/
└─ modules/
   └─ godotsteam/
      └─ sdk/
         ├─ public/
         └─ redistributable_bin/*
```

5. Go back to the base godot folder.

6. Compile the editor using scons `scons platform=windows target=editor module_mono_enabled=yes`

7. Add the `steam_api64.dll` to the bin folder.

8. Go to the `bin` folder: `cd bin`. Generate the C# bindings for Godot. `godot.windows.editor.x86_64.mono.exe --headless --generate-mono-glue ../modules/mono/glue`


