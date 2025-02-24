extends Node3D
class_name HealthComponent

@export var max_health: float
var current_health: float

signal health_at_zero

func _ready() -> void:
	current_health = max_health
	
func take_damage(damage: float):
	current_health -= damage
	if current_health <= 0:
		current_health = 0
		health_at_zero.emit()
	print("Current Health: ", self.current_health)
