extends Marker3D
class_name Slot

@export var item: ItemData = null

@export var slot_type: Globals.SlotType
@export var slot_animation_player: AnimationPlayer

var item_node: ItemNodeBase

func _ready() -> void:
	self.item_node = item.item_scene.instantiate()
	self.item_node.setup(item, slot_animation_player)
	self.add_child(item_node)

func activate_ability():
	item_node.activate_ability()

func _unhandled_input(event):
	var action_str: String = Globals.AbilityHotKeyDict[self.slot_type]
	if event.is_action_pressed(action_str):
		self.activate_ability()
