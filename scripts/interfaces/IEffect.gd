extends Resource
class_name IEffect

# Called when the effect is first applied
func apply_effect(target: Node):
	push_error("apply() not implemented")
	
func effectable_is_compatible(effectable) -> bool:
	return false
	
# Creates copy of the effect.
func copy() -> IEffect:
	push_error("copy() not implemented")
	return null
	
func instantiate(target: Node) -> IEffectNode:
	push_error("instantiate() not implemented")
	return null
