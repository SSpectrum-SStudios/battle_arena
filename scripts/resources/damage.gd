extends Resource
class_name Damage

enum damageType {physical, fire}

@export var damage_amount : float
var damage_type: damageType

func damage_func(health_component: HealthComponent):
	pass
