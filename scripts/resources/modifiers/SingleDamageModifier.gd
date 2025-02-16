extends Modifier
class_name SingleDamageModifier

@export var damage: Damage

func get_damage() -> Damage:
	return damage

func modifiable_is_compatible(modifiable) -> bool:
	if modifiable is HealthComponent:
		return true
	else:
		return false

func apply_modifier(health_component: HealthComponent):
	health_component.take_damage(damage)
	self.clean_up.emit(self)
