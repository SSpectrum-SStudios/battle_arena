extends IEffect
class_name LifeStealEffect

# Percentage of damage dealt that is converted to healing (e.g., 0.1 = 10%)
@export var lifesteal_percentage: float = 0.1


# Applies the effect to the target by instantiating the effect node and adding it as a child
func apply_effect(target: Node):
	var effect_node = instantiate(target)
	target.add_child(effect_node)
	effect_node.apply_effect(target)

# Ensures the effect is only compatible with HealthComponent
func effectable_is_compatible(effectable) -> bool:
	return effectable is HealthComponent

# Creates a deep copy of the effect for independent instances
func copy() -> LifeStealEffect:
	var new_effect = self.duplicate(true)
	return new_effect

# Instantiates the LifeStealEffectNode with the lifesteal percentage
func instantiate(_target: Node) -> IEffectNode:
	return LifeStealEffectNode.new(self.lifesteal_percentage)
	
class LifeStealEffectNode extends IEffectNode:
	# Percentage of damage to convert to healing
	var lifesteal_percentage: float
	
	# Reference to the HealthComponent this node is attached to
	var target: HealthComponent
	
	# Player ID associated with the HealthComponent
	var player_id: int

	# Constructor to initialize the lifesteal percentage
	func _init(percentage: float):
		self.lifesteal_percentage = percentage

	# Sets up the effect by storing the target and connecting to the damage signal
	func apply_effect(target: Node):
		self.target = target as HealthComponent
		# Assumes HealthComponent has a player_id property
		self.player_id = self.target.player_id
		Globals.on_damage_taken.connect(self._on_damage_taken)

	# Cleans up the effect by disconnecting the signal and freeing the node
	func remove_effect(_target: Node):
		Globals.on_damage_taken.disconnect(self._on_damage_taken)
		queue_free()

	# Handles the damage taken signal and applies healing if conditions are met
	func _on_damage_taken(damage_context: DamageContext):
		if damage_context.attacker_id == self.player_id:
			var heal_amount = damage_context.damage_amount * lifesteal_percentage
			target.heal(heal_amount)
