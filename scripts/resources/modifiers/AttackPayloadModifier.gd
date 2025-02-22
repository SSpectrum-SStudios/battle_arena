extends Modifier
class_name AttackPayloadModifier


func _init(priority: Globals.ModifierPriority = Globals.ModifierPriority.HIGHEST):
	self.priority = priority

func modifiable_is_compatible(modifiable) -> bool:
	return modifiable is AttackComponent
		

func apply_modifier(modifiable):
	if modifiable is AttackComponent:
		for modifier in modifiers:
			if modifier.has_method("get_copy"):
				modifiable.attack_payload.attacking_modifiers.append(modifier.get_copy())
			else:
				modifiable.attack_payload.attacking_modifiers.append(modifier)
