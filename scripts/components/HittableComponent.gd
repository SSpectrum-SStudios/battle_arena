extends Area3D
class_name HittableComponent

const IModifiable = preload("res://scripts/interfaces/IModifiable.gd")
const IEffectable = preload("res://scripts/interfaces/IEffectable.gd")

@export var modifiers: Array[IModifier]

var base_most_recent_hit: HitContext = null
var modified_most_recent_hit: HitContext = null
var entity_id: int

func hit(attack_payload: AttackPayload):
	self.base_most_recent_hit = HitContext.new(self.entity_id, attack_payload)
	Globals.hit_received.emit(base_most_recent_hit)
	var modified_context: HitContext = self.get_modified_value()
	if modified_context and not modified_context.attack_payload.attacking_effects.is_empty():
		Globals.hit_modified.emit(modified_context)
	
# Adds a modifier (an object that adheres to IModifier)
func add_modifier(modifier):
	modifiers.append(modifier)
	modifiers.sort_custom(IModifier.compare_modifier_by_pritority)

# Removes a modifier.
func remove_modifier(modifier):
	modifiers.erase(modifier)
	
# Returns the value before any modifiers have been applied.	
func get_base_value():
	return base_most_recent_hit

# Returns the value after all modifiers have been applied.
func get_modified_value():
	if not base_most_recent_hit:
		push_error("Tried copying a null hit.")
		return null
	modified_most_recent_hit = HitContext.copy(base_most_recent_hit)
	for modifier in self.modifiers:
		modified_most_recent_hit = modifier.modify(modified_most_recent_hit)
	return modified_most_recent_hit
	
# Adds an effect (an object that adheres to IEffect)
func add_effect(effect: IEffect):
	effect.apply_effect(self)
