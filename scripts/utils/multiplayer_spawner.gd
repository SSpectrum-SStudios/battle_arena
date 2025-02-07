extends MultiplayerSpawner

@export var playerScene : PackedScene

var players = {}

func _ready() -> void:
	self.spawn_function = spawnPlayer
	if is_multiplayer_authority():
		self.spawn(1)
		players[1].global_position = Vector3(0,5,0)
		multiplayer.peer_connected.connect(spawn)
		multiplayer.peer_disconnected.connect(removePlayer)
		
func spawnPlayer(data):
	var p: Node3D = playerScene.instantiate()
	p.set_multiplayer_authority(data)
	players[data] = p
	#p.global_position = Vector3(0,5,0)
	return p
	
func removePlayer(data) -> void:
	players[data].queue_free()
	players.erase(data)
	
