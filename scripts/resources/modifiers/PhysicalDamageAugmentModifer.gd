extends Modifier
class_name PhysicalDamageAugmentModifier

@export var is_augment_multiplier: bool
@export var augment_amount: float
var damage_increased_amount: float = 0

func _init(priority: Globals.ModifierPriority = Globals.ModifierPriority.LOWEST):
	self.priority = priority

func modifiable_is_compatible(modifiable) -> bool:
	return modifiable is AttackComponent
		
func apply_modifier(component: BaseComponent):
	if component is AttackComponent:
		var attack_payload: AttackPayload = component.get_attack_payload()
		for attack in attack_payload.attacking_modifiers:
			if attack.has_method("get_damage"):
				var damage: Damage = attack.get_damage()
				if damage.damage_type == Globals.DamageType.PHYSICAL_DAMAGE:
					augment_damage(attack)

func augment_damage(attack):
	if is_augment_multiplier:
		damage_increased_amount = attack.damage_amount * augment_amount
		attack.damage_amount += damage_increased_amount
	else:
		attack.damage_amount += augment_amount
