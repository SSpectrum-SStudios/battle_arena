extends Area3D
class_name Hittable

signal take_hit

func hit(damage_list: Array):
	take_hit.emit(damage_list)
	print(self.name, " has been hit.")
