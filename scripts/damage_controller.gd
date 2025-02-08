extends Node3D

@onready var items: Node3D = %Items


func _ready() -> void:
	for weapon: DoesDamage in items.weapon_list:
		weapon.entered_hittable.connect(_on_damage_dealt)
		
func _on_damage_dealt(hittable: Hittable, damage_list):
	hittable.hit(damage_list)
