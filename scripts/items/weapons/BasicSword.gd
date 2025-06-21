extends ItemNodeBase
class_name BasicSword

@export var ability_animation: Animation
@export var reset_animation: Animation

func setup(item_data: ItemData, animation_player: AnimationPlayer):
	super(item_data, animation_player)
	var anim_lib := self.animation_player.get_animation_library("")
	anim_lib.add_animation(self.ability_animation.resource_name, self.ability_animation)
	anim_lib.add_animation(self.reset_animation.resource_name, self.reset_animation)
	
	self.animation_player.animation_finished.connect(self.on_animation_finished)
	
func activate_ability():
	self.animation_player.play(self.ability_animation.resource_name)


func on_animation_finished(anim_name: StringName):
	if anim_name == self.ability_animation.resource_name:
		self.animation_player.play(self.reset_animation.resource_name)
