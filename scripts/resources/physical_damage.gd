extends Damage
class_name PhysicalDamage

func damage_func(health_component: HealthComponent):
	if self.damage_amount > 0:
		health_component.take_damage(self.damage_amount)
