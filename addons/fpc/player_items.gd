extends Node3D

@export var weapon: PackedScene

@onready var hand: Marker3D = %Hand
@onready var attack_animation_player: AnimationPlayer = %WeaponAnimation

func _ready() -> void:
	var weapon1 = weapon.instantiate()
	hand.add_child(weapon1)

func _process(delta: float) -> void:
	if Input.is_action_just_pressed("attack"):
		attack_animation_player.play("swing")


func _on_weapon_animation_animation_finished(anim_name: StringName) -> void:
	if anim_name == "swing":
		attack_animation_player.play("RESET")
