extends BaseComponent
class_name HealthComponent

@export var max_health: float
@export var modifiers: Array[Modifier]
var current_health: float

signal health_at_zero

func _ready() -> void:
	current_health = max_health
	
func take_damage(damage: Damage):
	current_health -= damage.damage_amount
	if current_health <= 0:
		current_health = 0
		health_at_zero.emit()
	print("Current Health: ", self.current_health)
	
func add_modifier(modifier: Modifier):
	modifier.clean_up.connect(remove_modifier)
	modifiers.append(modifier)
	modifier.apply_modifier(self)
		
func remove_modifier(modifier: Modifier):
	self.modifiers.erase(modifier)
