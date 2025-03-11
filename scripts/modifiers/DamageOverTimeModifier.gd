extends DoesDamageModifier
class_name DamageOverTimeModifier

@export var number_of_ticks: int = 5
@export var time_between_ticks: float = .5
var current_number_ticks: int
var damage_timer: Timer

func modifiable_is_compatible(modifiable) -> bool:
	if modifiable is HealthComponent:
		return true
	else:
		return false

func apply_modifier(health_component: HealthComponent):
	super(health_component)
	if self.get_damage().damage_amount > 0:
		for modifier in modifiers:
			if modifier.modifiable_is_compatible(self):
				modifier.apply_modifier(self)
	current_number_ticks = number_of_ticks
	tick_damage()
	create_timer()

func _on_fire_tick_timer_ended():
	tick_damage()

func create_timer():
	damage_timer = Timer.new()
	self.current_health_component.add_child(damage_timer)
	damage_timer.start(time_between_ticks)
	damage_timer.timeout.connect(_on_fire_tick_timer_ended)

func tick_damage():
	current_health_component.take_damage(modified_damage)
	current_number_ticks -= 1
	
	if current_number_ticks <= 0:
		if damage_timer:
			damage_timer.stop()
			damage_timer.queue_free()
			self.clean_up.emit(self)
	
func get_copy()-> DamageOverTimeModifier:
	var copy = DamageOverTimeModifier.new()
	copy.number_of_ticks = self.number_of_ticks
	copy.time_between_ticks = self.time_between_ticks
	copy.current_number_ticks = self.current_number_ticks
	copy.current_health_component = self.current_health_component
	copy.damage_timer = self.damage_timer
	var damage_copy = Damage.make_copy(self.base_damage)
	copy.base_damage = damage_copy
	for modifier in modifiers:
		if modifier.has_method("get_copy"):
			copy.modifiers.append(modifier.get_copy())
		else:
			copy.modifiers.append(modifier)
	return copy
	
	
		
	
