extends IModifier
class_name AttackPayloadModifier

@export var effect_for_payload: IEffect

func modify(value: Resource) -> Resource:
	if value is AttackPayload:
		value.attacking_effects.append(effect_for_payload)
	
	return value
	
func modifiable_is_compatible(modifiable) -> bool:
	return modifiable is AttackComponent

func get_priority()-> Globals.ModifierPriority:
	return Globals.ModifierPriority.HIGH
