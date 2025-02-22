extends Node3D

@export var components : Array[BaseComponent]

func _ready() -> void:
	_populate_components(self)
	
func _populate_components(node):
	for N in node.get_children():
		if N is BaseComponent:
			components.append(N)
			if N.has_signal("propagate_modifier"):
				N.propagate_modifier.connect(_distribute_modifiers)
		if N.get_child_count() > 0:
			_populate_components(N)
			
func _distribute_modifiers(modifiers: Array[Modifier]):
	for modifier in modifiers:
		for component in self.components:
			if modifier.modifiable_is_compatible(component):
				component.add_modifier(modifier)
