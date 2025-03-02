extends Resource
class_name IModifier


func modify(value: Resource) -> Resource:
	# This function should be overridden in the concrete modifier.
	push_error("modify() not implemented")
	return null
	
func modifiable_is_compatible(modifiable) -> bool:
	push_error("modifiable_is_compatible() not implemented")
	return false

func get_priority()-> Globals.ModifierPriority:
	push_error("get_priority() not implemented")
	return -1

static func compare_modifier_by_pritority(mod1: IModifier, mod2: IModifier):
	return mod1.get_priority() < mod2.get_priority()
