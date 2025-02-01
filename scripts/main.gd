extends Node3D


var lobby_id = 0
var peer = SteamMultiplayerPeer.new()

@onready var multiplayer_spawner = $MultiplayerSpawner

func _ready() -> void:
	multiplayer_spawner.spawn_function = spawn_level
	peer.lobby_created.connect(_on_lobby_created)
	Steam.lobby_match_list.connect(_on_lobby_match_list)
	open_lobby_list()

func hide_ui():
	$Host.hide()
	$LobbyContainer/LobbyList.hide()
	$Refresh.hide()

func _on_host_pressed() -> void:
	peer.create_lobby(SteamMultiplayerPeer.LOBBY_TYPE_PUBLIC)
	multiplayer.multiplayer_peer = peer
	multiplayer_spawner.spawn("res://test_world.tscn")
	hide_ui()

func spawn_level(data):
	var level = (load(data) as PackedScene).instantiate()
	return level
	
func _on_lobby_created(is_conncected, id):
	if is_conncected:
		self.lobby_id = id
		Steam.setLobbyData(lobby_id, "name", str(Steam.getPersonaName()+"'s Lobby"))
		Steam.setLobbyJoinable(lobby_id, true)
		print(lobby_id)
		
func join_lobby(id):
	peer.connect_lobby(id)
	multiplayer.multiplayer_peer = peer
	lobby_id = id
	hide_ui()
	
	
func open_lobby_list():
	Steam.addRequestLobbyListDistanceFilter(Steam.LOBBY_DISTANCE_FILTER_WORLDWIDE)
	Steam.requestLobbyList()
	
func _on_lobby_match_list(lobbies):
	
	for lobby in lobbies:
		var lobby_name = Steam.getLobbyData(lobby, "name")
		var mem_count = Steam.getNumLobbyMembers(lobby)
		
		var button = Button.new()
		button.set_text(str(lobby_name, " | Player Count: ", mem_count))
		button.set_size(Vector2(100,5))
		button.pressed.connect(join_lobby.bind(lobby))
		$LobbyContainer/LobbyList.add_child(button)
	


func _on_refresh_pressed() -> void:
	if $LobbyContainer/LobbyList.get_child_count() > 0:
		for button in $LobbyContainer/LobbyList.get_children():
			button.queue_free()
		open_lobby_list()
			
