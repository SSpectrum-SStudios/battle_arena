extends BaseComponent
class_name HealthComponent

@export var max_health: float
@export var modifiers: Array[Modifier]
var current_health: float

signal health_at_zero
signal damage_taken(damage: Damage)

func _ready() -> void:
	current_health = max_health
	
func take_damage(damage: Damage):
	current_health -= damage.damage_amount
	damage_taken.emit(damage)
	if current_health <= 0:
		current_health = 0
		health_at_zero.emit()
	print("Current Health: ", self.current_health)
	
func heal(hp_to_heal: float):
	current_health += hp_to_heal
	if current_health > max_health:
		current_health = max_health
	print("Current Health: ", self.current_health)
	
func add_modifier(modifier: Modifier):
	if not modifier.clean_up.is_connected(remove_modifier):
		modifier.clean_up.connect(remove_modifier)
	modifiers.append(modifier)
	if modifier.modifiable_is_compatible(self):
		modifier.apply_modifier(self)
		
func remove_modifier(modifier: Modifier):
	modifier.clean_up.disconnect(remove_modifier)
	self.modifiers.erase(modifier)
