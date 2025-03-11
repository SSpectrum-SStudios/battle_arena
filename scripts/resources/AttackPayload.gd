extends Resource
class_name AttackPayload

@export var attacking_effects : Array[IEffect]


func _init(attacking_effects: Array[IEffect] = []) -> void:
	self.attacking_effects = attacking_effects
	
static func copy(payload_to_copy: AttackPayload) -> AttackPayload:
	var attacking_effect_copy : Array[IEffect] = []
	for effect in payload_to_copy.attacking_effects:
		attacking_effect_copy.append(effect.copy())
	return AttackPayload.new(attacking_effect_copy)
