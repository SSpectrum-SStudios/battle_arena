extends Resource
class_name AttackPayload

var attacking_modifiers : Array[Modifier]


func _init(attacking_modifiers) -> void:
	self.attacking_modifiers = attacking_modifiers
