extends IContainerEffect
class_name ItemEquippedEffect

var item_id: int

@export var effect_to_apply: IEffect

func _init(item_id: int = -1, effect_to_apply: IEffect = null):
	self.item_id = item_id
	self.effect_to_apply = effect_to_apply

func apply_effect(target: Node):
	var effect_node = self.instantiate(target)
	target.add_child(effect_node)
	effect_node.apply_effect(target)
	
func effectable_is_compatible(effectable) -> bool:
	return effect_to_apply.effectable_is_compatible(effectable)
	
# Creates copy of the effect.
func copy():
	var cp = ItemEquippedEffect.new(self.item_id, self.effect_to_apply)
	cp.effect_to_apply = self.effect_to_apply.copy()
	return cp
	
func instantiate(target: Node) -> IEffectNode:
	return ItemEquippedEffectNode.new(self.item_id, effect_to_apply.instantiate(target))
	
func get_nested_effect() -> IEffect:
	return self.effect_to_apply


class ItemEquippedEffectNode extends IEffectNode:
	
	var item_id: int
	var effect_to_apply: IEffectNode

	func _init(item_id: int, effect_to_apply: IEffectNode):
		#TODO: Create connection from item to 
		self.item_id = item_id
		self.effect_to_apply = effect_to_apply
		
	# Called when the effect is first applied
	func apply_effect(target: Node):
		if self.effect_to_apply:
			self.effect_to_apply.apply_effect(target)
			add_child(self.effect_to_apply)

	# Called when the effect is removed
	func remove_effect(target: Node):
		if effect_to_apply:
			self.effect_to_apply.remove_effect(target)
		queue_free()
		
