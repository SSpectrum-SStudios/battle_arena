extends Node3D

@onready var health_component: HealthComponent = %HealthComponent

func _on_does_damage_take_hit(damage_list: Array[Damage]) -> void:
	for damage in damage_list:
		damage.damage_func(health_component)
