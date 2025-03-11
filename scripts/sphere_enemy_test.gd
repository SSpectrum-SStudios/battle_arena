extends Node3D

const IEffectable = preload("res://scripts/interfaces/IEffectable.gd")

@export var components: Array[Node]
@export var entity_id: int

func _ready() -> void:
	if not is_multiplayer_authority():
		return
	entity_id = Globals.get_new_id()
	_populate_components(self)
	Globals.hit_received.connect(self.receive_hit_test)
	
func _populate_components(node: Node) -> void:
	for child in node.get_children():
		if IEffectable.is_IEffectable(child):
			components.append(child)
			child.set_id(entity_id)
		if child.get_child_count() > 0:
			_populate_components(child)
			
func receive_hit_test(context: HitContext):
	if context.hit_entity_id == entity_id:
		self._distribute_effects(context.attack_payload.attacking_effects)
			
func _distribute_effects(effects: Array) -> void:
	for effect in effects:
		for component in components:
			if effect.effectable_is_compatible(component):
				component.add_effect(effect)
