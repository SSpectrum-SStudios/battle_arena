extends Node3D
class_name Item

@export var modifiers: Array[Modifier]

func get_modifiers() -> Array[Modifier]:
	return modifiers
