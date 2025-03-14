extends IModifier
class_name  ApplyAttackingIDmodifier

@export var attacker_id: int = -1

func _init(attacker_id: int) -> void:
	self.attacker_id = attacker_id

func modify(value: Resource) -> Resource:
	if value is not AttackPayload:
		return value
	
	for effect: IEffect in value.attacking_effects:
		var core_effect = Globals.get_core_effect(effect)
		if core_effect is not DamagingEffect:
			return value
		
		var context : DamageContext = core_effect.damage_context
		context.attacker_id = self.attacker_id
				
	return value
	
func modifiable_is_compatible(modifiable) -> bool:
	return modifiable is AttackComponent

func get_priority()-> Globals.ModifierPriority:
	return Globals.ModifierPriority.LOWEST
