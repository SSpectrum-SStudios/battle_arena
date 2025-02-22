extends Node3D
class_name PlayerController

@export var components : Array[BaseComponent]
@export var items : Array[Item]

func _ready() -> void:
	_populate_components(self)
	_populate_items(self)
	var modifiers : Array[Modifier] = get_modifiers_from_items()
	self._distribute_modifiers(modifiers)
	
func get_modifiers_from_items() -> Array[Modifier]:
	var modifiers : Array[Modifier]
	for item in self.items:
		if item.has_method("get_modifiers"):
			modifiers.append_array(item.get_modifiers())
	return modifiers

func _populate_components(node):
	for N in node.get_children():
		if N is BaseComponent:
			components.append(N)
			if N.has_signal("propagate_modifier"):
				N.propagate_modifier.connect(_distribute_modifiers)
		if N.get_child_count() > 0:
			_populate_components(N)
			
func _populate_items(node):
	for N in node.get_children():
		if N is Item:
			items.append(N)
		if N.get_child_count() > 0:
			_populate_items(N)
			
func _distribute_modifiers(modifiers: Array[Modifier]):
	for modifier in modifiers:
		for component in self.components:
			if modifier.modifiable_is_compatible(component):
				component.add_modifier(modifier)
	
