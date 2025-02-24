extends Node3D

@onready var health_component: HealthComponent = %HealthComponent

func _on_hittable_take_hit(damage_list: Array[Damage]) -> void:
	for damage in damage_list:
		if damage is FireDamage:
			damage.damage_func(health_component)


func _on_health_component_health_at_zero() -> void:
	pass
