extends DamagingEffect
class_name SingleDamageEffect

# Called when the effect is first applied
func apply_effect(target: Node):
	var effect_node: IEffectNode = instantiate(target)
	target.add_child(effect_node)
	effect_node.apply_effect(target)
	
func instantiate(target: Node) -> IEffectNode:
	return SingleDamageEffectNode.new(self.damage_context)

class SingleDamageEffectNode extends IEffectNode:

	var damage_context: DamageContext
	
	func _init(context: DamageContext) -> void:
		self.damage_context = context
		
	func apply_effect(target: Node):
		var target_comp = target as HealthComponent
		target_comp.take_damage(self.damage_context)
		self.remove_effect(target_comp)

	func remove_effect(target: Node):
		queue_free()
