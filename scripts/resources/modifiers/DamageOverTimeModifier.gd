extends Modifier
class_name DamageOverTimeModifier

@export var number_of_ticks: int = 5
@export var time_between_ticks: float = .5
@export var damage: Damage
var current_number_ticks: int
var current_health_component: HealthComponent
var damage_timer: Timer

func get_damage() -> Damage:
	return damage

func modifiable_is_compatible(modifiable) -> bool:
	if modifiable is HealthComponent:
		return true
	else:
		return false

func apply_modifier(health_component: HealthComponent):
	current_health_component = health_component
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
	if self.damage.damage_amount > 0:
		current_health_component.take_damage(self.damage)
	current_number_ticks -= 1
	
	if current_number_ticks <= 0:
		if damage_timer:
			damage_timer.stop()
			damage_timer.queue_free()
			self.clean_up.emit(self)
	
	
	
	
		
	
