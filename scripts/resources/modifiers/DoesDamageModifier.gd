extends Modifier
class_name DoesDamageModifier

@export var base_damage: Damage
var modified_damage: Damage
var current_health_component: HealthComponent

func apply_modifier(modifiable):
	current_health_component = modifiable
	modified_damage = Damage.make_copy(base_damage)

func get_damage() -> Damage:
	if modified_damage == null:
		modified_damage = Damage.make_copy(base_damage)
	return modified_damage
