extends IContainerEffect
class_name IntervalEffect

@export var effect_to_apply: IEffect


@export var number_of_ticks: int = 5
@export var time_between_ticks: float = .5


func _init(effect_to_apply: IEffect = null, num_ticks: int = 0, time_between_ticks: float = 0):
	self.effect_to_apply = effect_to_apply
	self.number_of_ticks = num_ticks
	self.time_between_ticks = time_between_ticks

func apply_effect(target: Node):
	var effect_node = self.instantiate(target)
	target.add_child(effect_node)
	effect_node.apply_effect(target)
	
func effectable_is_compatible(effectable) -> bool:
	return effect_to_apply.effectable_is_compatible(effectable)
	
# Creates copy of the effect.
func copy() -> IEffect:
	return self.duplicate(true)
	
func instantiate(target: Node) -> IEffectNode:
	return IntervalEffectNode.new(effect_to_apply, target, number_of_ticks, time_between_ticks)
	
func get_nested_effect() -> IEffect:
	return self.effect_to_apply


class IntervalEffectNode extends IEffectNode:
	
	var item_id: String
	var effect_to_apply: IEffect
	var target: Node
	var number_of_ticks: int = 5
	var time_between_ticks: float = .5
	var current_number_ticks: int
	var effect_timer: Timer

	func _init(effect_to_apply: IEffect, target: Node, num_ticks: int, time_bet_ticks: float):
		self.effect_to_apply = effect_to_apply
		self.target = target
		self.number_of_ticks = num_ticks
		self.time_between_ticks = time_bet_ticks
		self.current_number_ticks = number_of_ticks
		
	func apply_effect(target: Node):
			tick_effect()
			create_timer()

	# Called when the effect is removed
	func remove_effect(target: Node):
		if self.effect_timer and not self.effect_timer.is_stopped():
				self.effect_timer.stop()
				self.effect_timer.timeout.disconnect(self._on_timer_ended)
				self.effect_timer.queue_free()
		queue_free()

	func create_timer():
		self.effect_timer = Timer.new()
		self.effect_timer.autostart = true
		effect_timer.timeout.connect(self._on_timer_ended)
		add_child(effect_timer)
		#effect_timer.start(time_between_ticks)
		

	func tick_effect():
		if self.effect_to_apply:
			var effect_to_apply_node = self.effect_to_apply.instantiate(self.target)
			effect_to_apply_node.apply_effect(self.target)
			self.target.add_child(effect_to_apply_node)
		current_number_ticks -= 1
		
		if current_number_ticks <= 0:
			self.remove_effect(self.target)

	func _on_timer_ended():
		self.tick_effect()
