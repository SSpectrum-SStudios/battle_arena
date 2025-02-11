extends Damage
class_name FireDamage

@export var number_of_ticks: int = 5
@export var time_between_ticks: float = .5
var current_number_ticks: int
var current_health_component: HealthComponent
var fire_damage_timer: Timer

func damage_func(health_component: HealthComponent):
	current_health_component = health_component
	current_number_ticks = number_of_ticks
	tick_fire_damage()
	create_timer()

func _on_fire_tick_timer_ended():
	tick_fire_damage()

func create_timer():
	fire_damage_timer = Timer.new()
	self.current_health_component.add_child(fire_damage_timer)
	fire_damage_timer.start(time_between_ticks)
	fire_damage_timer.timeout.connect(_on_fire_tick_timer_ended)

func tick_fire_damage():
	if self.damage_amount > 0:
		current_health_component.take_damage(self.damage_amount)
	current_number_ticks -= 1
	
	if current_number_ticks <= 0:
		fire_damage_timer.stop()
		fire_damage_timer.queue_free()
	
	
	
	
	
		
	
