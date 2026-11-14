extends MultiplayerSpawner

@export var network_player: PackedScene = preload("res://Scenes/Player.tscn")

func _ready() -> void:
	if network_player == null:
		push_error("A cena do jogador não foi atribuída ao MultiplayerSpawner.")
		return

	multiplayer.peer_connected.connect(spawn_player)
	multiplayer.peer_disconnected.connect(remove_player)
	HighLevelNetworkHandler.server_started.connect(spawn_server_player)

func spawn_server_player() -> void:
	spawn_player(multiplayer.get_unique_id())

func spawn_player(id: int) -> void:
	if !multiplayer.is_server():
		return

	if network_player == null:
		push_error("A cena do jogador não foi atribuída ao MultiplayerSpawner.")
		return

	var player: Node = network_player.instantiate()
	player.name = str(id)
	var player_2d := player as Node2D
	if player_2d == null:
		push_error("A cena do jogador precisa herdar Node2D.")
		player.queue_free()
		return
	player_2d.position = Vector2(400 + (id - 1) * 80, 450)

	var target_parent: Node = get_node_or_null(spawn_path)
	if target_parent == null:
		target_parent = get_parent()
		if target_parent == null:
			push_error("Nenhum pai válido foi encontrado para spawnar o jogador.")
			return
		target_parent.call_deferred("add_child", player)
		return

	target_parent.call_deferred("add_child", player)

func remove_player(id: int) -> void:
	if !multiplayer.is_server():
		return

	call_deferred("_remove_player", id)

func _remove_player(id: int) -> void:
	var player_container: Node = get_node_or_null(spawn_path)
	if player_container == null:
		player_container = get_parent()
	if player_container == null:
		return

	var player: Node = player_container.get_node_or_null(str(id))
	if player != null:
		player.queue_free()
