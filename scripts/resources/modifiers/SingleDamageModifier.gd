extends DoesDamageModifier
class_name SingleDamageModifier

func modifiable_is_compatible(modifiable) -> bool:
	if modifiable is HealthComponent:
		return true
	else:
		return false

func apply_modifier(health_component: HealthComponent):
	super(health_component)
	for modifier in modifiers:
		modifier.apply_modifier(self)
	if self.get_damage().damage_amount > 0:
		health_component.take_damage(self.get_damage())
	self.clean_up.emit(self)

func get_copy() -> SingleDamageModifier:
	var copy = SingleDamageModifier.new()
	var damage_copy = Damage.make_copy(self.base_damage)
	copy.base_damage = damage_copy
	for modifier in modifiers:
		if modifier.has_method("get_copy"):
			copy.modifiers.append(modifier.get_copy())
		else:
			copy.modifiers.append(modifier)
	return copy
