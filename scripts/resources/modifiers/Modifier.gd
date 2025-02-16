extends Resource
class_name Modifier

var priority: Globals.ModifierPriority = Globals.ModifierPriority.NORMAL
@export var modifiers: Array[Modifier]

signal clean_up(modifier: Modifier)


func modifiable_is_compatible(modifiable) -> bool:
	return false

func apply_modifier(modifiable):
	pass

func stop_modifier():
	pass
	
static func compare_modifier_by_pritority(mod1: Modifier, mod2: Modifier):
	return mod1.priority < mod2.priority
