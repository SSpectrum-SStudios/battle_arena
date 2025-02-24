extends Area3D
class_name Hittable

signal take_hit(damage_list: Array[Damage])

func hit(damage_list: Array[Damage]):
	take_hit.emit(damage_list)
