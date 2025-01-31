extends MultiplayerSpawner

@export var playerScene : PackedScene

var players = {}

func _ready() -> void:
	self.spawn_function = spawnPlayer
	if is_multiplayer_authority():
		self.spawn(1)
		multiplayer.peer_connected.connect(spawn)
		multiplayer.peer_disconnected.connect(removePlayer)
		
func spawnPlayer(data) -> PackedScene:
	var p = playerScene.instantiate()
	p.set_multiplayer_authority(data)
	players[data] = p
	return p
	
func removePlayer(data) -> void:
	players[data].queue_free()
	players.erase(data)
	
