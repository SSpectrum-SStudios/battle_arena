extends Node3D
class_name PlayerController

# Preload the IEffectable interface
const IEffectable = preload("res://scripts/interfaces/IEffectable.gd")

# Array of components (nodes that follow IEffectable)
@export var components: Array[Node]

# Array of items
@export var items: Array[Item]

func _ready() -> void:
	# Populate components and items from the node tree
	_populate_components(self)
	_populate_items(self)
	
	# Get effects from items and distribute them
	var effects = get_effects_from_items()
	_distribute_effects(effects)

# Collect effects from all items
func get_effects_from_items() -> Array[IEffect]:
	var effects: Array[IEffect] = []  # Array of IEffect instances
	for item in items:
		if item.has_method("get_effects"):
			effects.append_array(item.get_effects())
	return effects

# Recursively find all IEffectable components in the node tree
func _populate_components(node: Node) -> void:
	for child in node.get_children():
		if IEffectable.is_IEffectable(child):
			components.append(child)
		if child.get_child_count() > 0:
			_populate_components(child)

# Recursively find all Item nodes in the node tree
func _populate_items(node: Node) -> void:
	for child in node.get_children():
		if child is Item:
			items.append(child)
		if child.get_child_count() > 0:
			_populate_items(child)

# Distribute effects to compatible components
func _distribute_effects(effects: Array) -> void:
	for effect in effects:
		for component in components:
			if effect.effectable_is_compatible(component):
				component.add_effect(effect)
