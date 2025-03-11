extends Resource
class_name HealthResource

@export var max_health: float:
	set(new_value):
		if max_health != new_value:
			max_health = new_value
			emit_changed()

@export var current_health: float:
	set(new_value):
		if current_health != new_value:
			current_health = new_value
			emit_changed()
