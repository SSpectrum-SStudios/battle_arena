extends Node3D

const IEffectable = preload("res://scripts/interfaces/IEffectable.gd")

@export var components: Array[Node]

func _ready() -> void:
	_populate_components(self)
	Globals.hit_received.connect(self.receive_hit_test)
	
func _populate_components(node: Node) -> void:
	for child in node.get_children():
		if IEffectable.is_IEffectable(child):
			components.append(child)
		if child.get_child_count() > 0:
			_populate_components(child)
			
func receive_hit_test(context: HitContext):
	self._distribute_effects(context.attack_payload.attacking_effects)
			
func _distribute_effects(effects: Array) -> void:
	for effect in effects:
		for component in components:
			if effect.effectable_is_compatible(component):
				component.add_effect(effect)
