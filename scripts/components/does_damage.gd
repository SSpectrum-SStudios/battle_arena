extends Area3D

signal entered_hittable


func _on_area_entered(area: Area3D) -> void:
	if area is Hittable:
		entered_hittable.emit()
