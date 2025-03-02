extends IModifier
class_name PhysicalDamageAugmentModifier

@export var is_augment_multiplier: bool
@export var augment_amount: float


func modify(value: Resource) -> Resource:
	if value is not AttackPayload:
		return value
	
	for effect: IEffect in value.attacking_effects:
		var core_effect = Globals.get_core_effect(effect)
		if core_effect is not DamagingEffect:
			return value
		
		var context : DamageContext = core_effect.damage_context
		if context.damage_type != Globals.DamageType.PHYSICAL_DAMAGE:
			return value
			
		if is_augment_multiplier:
			context.damage_amount *= augment_amount
		else:
			context.damage_amount += augment_amount
				
	return value
	

func modifiable_is_compatible(modifiable) -> bool:
	return modifiable is AttackComponent

func get_priority()-> Globals.ModifierPriority:
	return Globals.ModifierPriority.LOWEST
