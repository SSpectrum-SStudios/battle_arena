extends IEffect
class_name DamagingEffect

@export var damage_context: DamageContext


# Called when the effect is first applied
func apply_effect(target: Node):
	push_error("apply() not implemented")
	
func effectable_is_compatible(effectable) -> bool:
	return effectable is HealthComponent
	
# Creates copy of the effect.
func copy():
	return self.duplicate(true)
	
func instantiate(target: Node) -> IEffectNode:
	push_error("instantiate() not implemented")
	return null
