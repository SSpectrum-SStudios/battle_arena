extends Node3D
class_name Item

@export var effects: Array[IEffect]
@export var item_id: int

func get_effects() -> Array[IEffect]:
	var item_wrapped_effects : Array[IEffect] = []
	for effect in effects:
		item_wrapped_effects.append(ItemEquippedEffect.new(self.item_id, effect))
	return item_wrapped_effects
