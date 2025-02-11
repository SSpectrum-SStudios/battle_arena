extends Damage
class_name PhysicalDamage

func damage_func(health_component: HealthComponent):
	if self.damage_amount > 0:
		print("Telling health Component to take ", self.damage_amount, " damage.")
		health_component.take_damage(self.damage_amount)
