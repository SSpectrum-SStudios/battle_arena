extends Node3D

@export var weapon: PackedScene

@onready var hand: Marker3D = %Hand
@onready var attack_animation_player: AnimationPlayer = %WeaponAnimation

var weapon_list : Array[DoesDamage]


func _ready() -> void:
	var weapon1 = weapon.instantiate()
	hand.add_child(weapon1)
	for child in weapon1.get_children():
		if child is DoesDamage:
			child.monitoring = false
			weapon_list.append(child)
			
	

func _process(delta: float) -> void:
	if is_multiplayer_authority():
		if Input.is_action_just_pressed("attack"):
			for weapon in weapon_list:
				weapon.monitoring = true
			attack_animation_player.play("swing")

			


func _on_weapon_animation_animation_finished(anim_name: StringName) -> void:
	if anim_name == "swing":
		attack_animation_player.play("RESET")
		for weapon in weapon_list:
				weapon.monitoring = false
