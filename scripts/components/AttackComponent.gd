extends Area3D
class_name AttackComponent
const IModifiable = preload("res://scripts/interfaces/IModifiable.gd")
const IEffectable = preload("res://scripts/interfaces/IEffectable.gd")


@export var base_attack_payload: AttackPayload = AttackPayload.new([])
@export var modifiers: Array[IModifier]

func _ready() -> void:
	self.area_entered.connect(self._on_area_entered)

func _on_area_entered(area: Area3D) -> void:
	if area is HittableComponent:
		area.hit(self.get_modified_value())
		
func add_modifier(modifier: IModifier):
	modifiers.append(modifier)
	modifiers.sort_custom(IModifier.compare_modifier_by_pritority)

func remove_modifier(modifier: IModifier):
	modifiers.erase(modifier)
	
# Returns the value before any modifiers have been applied.	
func get_base_value():
	return base_attack_payload

# Returns the value after all modifiers have been applied.
func get_modified_value():
	var modified_value = AttackPayload.copy(self.base_attack_payload)
	for modifier in modifiers:
		modified_value = modifier.modify(modified_value)
	return modified_value
	
func add_effect(effect: IEffect):
	effect.apply_effect(self)
