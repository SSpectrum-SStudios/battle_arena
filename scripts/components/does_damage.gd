extends Area3D
class_name DoesDamage

signal entered_hittable(hittable: Hittable, damage_list)

@export var damages: Array[Damage]

func _on_area_entered(area: Area3D) -> void:
	if area is Hittable:
		entered_hittable.emit(area,damages)
