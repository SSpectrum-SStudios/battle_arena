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
	
func add_modifier(modifier: Modifier):
	if not modifier.clean_up.is_connected(stop_modifier):
		modifier.clean_up.connect(stop_modifier)
	modifiers.append(modifier)
	modifiers.sort_custom(Modifier.compare_modifier_by_pritority)
	
static func compare_modifier_by_pritority(mod1: Modifier, mod2: Modifier):
	return mod1.priority < mod2.priority
