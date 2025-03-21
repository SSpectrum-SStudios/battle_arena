extends Resource
class_name ItemData

@export var name: String
@export var item_scene: PackedScene
@export var item_icon: Texture2D

@export var active_ability: IAbility
@export var effects: Array[IEffect]
@export var item_id: int

func _init() -> void:
	item_id = Globals.get_new_id()

func get_effects() -> Array[IEffect]:
	var item_wrapped_effects : Array[IEffect] = []
	for effect in effects:
		item_wrapped_effects.append(ItemEquippedEffect.new(self.item_id, effect))
	return item_wrapped_effects
	
func activate_ability():
	pass
