extends Node3D
class_name ItemNodeBase

var item_data: ItemData
var animation_player: AnimationPlayer

func setup(item_data: ItemData, animation_player: AnimationPlayer):
	self.item_data = item_data
	self.animation_player = animation_player

func activate_ability():
	pass

	
