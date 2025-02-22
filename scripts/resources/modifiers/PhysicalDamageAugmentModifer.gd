extends Modifier
class_name PhysicalDamageAugmentModifier

@export var is_augment_multiplier: bool
@export var augment_amount: float
var damage_increased_amount: float = 0

func _init(priority: Globals.ModifierPriority = Globals.ModifierPriority.LOWEST):
	self.priority = priority

func modifiable_is_compatible(modifiable) -> bool:
	return modifiable is AttackComponent or modifiable is DoesDamageModifier
		
func apply_modifier(component):
	if component is AttackComponent:
		var attack_payload: AttackPayload = component.get_attack_payload()
		for attack in attack_payload.attacking_modifiers:
			if attack is DoesDamageModifier:
				attack.modifiers.append(self)
	elif component is DoesDamageModifier:
		var damage: Damage = component.get_damage()
		if  damage.damage_type == Globals.DamageType.PHYSICAL_DAMAGE:
			augment_damage(damage)

func augment_damage(damage: Damage):
	if is_augment_multiplier:
		damage_increased_amount = damage.damage_amount * augment_amount
		damage.damage_amount += damage_increased_amount
	else:
		damage.damage_amount += augment_amount
