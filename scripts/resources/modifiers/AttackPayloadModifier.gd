extends Modifier
class_name AttackPayloadModifier



func modifiable_is_compatible(modifiable) -> bool:
	return modifiable is AttackComponent
		

func apply_modifier(modifiable):
	if modifiable is AttackComponent:
		modifiable.attack_payload.attacking_modifiers.append_array(modifiers)
