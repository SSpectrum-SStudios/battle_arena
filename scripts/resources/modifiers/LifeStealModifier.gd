extends Modifier
class_name LifeStealModifier

@export_range(0, 1) var life_steal_percentage: float = 0.2

signal damage_dealt(damage: float)

func _init(priority: Globals.ModifierPriority = Globals.ModifierPriority.LOW):
	self.priority = priority

func modifiable_is_compatible(modifiable) -> bool:
	if modifiable is HealthComponent:
		return true
	elif modifiable is AttackComponent:
		return true
	elif modifiable is DoesDamageModifier:
		return true
	return false

func apply_modifier(modifiable):
	for modifier in modifiers:
		if modifier.modifiable_is_compatible(self):
			modifier.apply_modifier(self)
	if modifiable is AttackComponent:
		for attack in modifiable.attack_payload.attacking_modifiers:
			if attack is DoesDamageModifier:
				attack.add_modifier(self)
	elif modifiable is HealthComponent:
		if not damage_dealt.is_connected(modifiable.heal):
			damage_dealt.connect(modifiable.heal)
	elif modifiable is DoesDamageModifier:
		if not modifiable.current_health_component.damage_taken.is_connected(send_lifesteal_amount):
			modifiable.current_health_component.damage_taken.connect(send_lifesteal_amount)
		

func send_lifesteal_amount(damage: Damage):
	var lifesteal_amount = damage.damage_amount * self.life_steal_percentage
	self.damage_dealt.emit(lifesteal_amount)

func stop_modifier():
	
	self.clean_up.emit()
