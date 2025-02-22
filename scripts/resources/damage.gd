extends Resource
class_name Damage

@export var damage_amount : float
@export var damage_type: Globals.DamageType

static func make_copy(damage: Damage) -> Damage:
	var copy = Damage.new()
	copy.damage_amount = damage.damage_amount
	copy.damage_type = damage.damage_type
	return copy
