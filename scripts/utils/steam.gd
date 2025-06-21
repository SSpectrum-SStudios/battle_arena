extends Node


func _ready():
	OS.set_environment("SteamAppID", str(3820160))
	OS.set_environment("SteamGameID", str(3820160))
	Steam.steamInitEx()
	
func _process(delta: float) -> void:
	Steam.run_callbacks()
