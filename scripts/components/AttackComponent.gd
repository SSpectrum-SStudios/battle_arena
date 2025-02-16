extends BaseComponent
class_name AttackComponent

@export var base_attacks: Array[Modifier]
var attack_payload: AttackPayload
var modifiers: Array[Modifier]

func _on_area_entered(area: Area3D) -> void:
	if area is IHittable:
		attack_payload.new(base_attacks.duplicate())
		for modifier in modifiers:
			modifier.apply_modifier(self)
		area.hit(attack_payload)
		
func get_attack_payload() -> AttackPayload:
	return self.attack_payload

func add_modifier_to_payload(attack: Modifier):
	self.attack_payload.append(attack)

func add_modifier(modifier: Modifier):
	modifier.clean_up.connect(remove_modifier)
	modifiers.append(modifier)
	modifiers.sort_custom(Modifier.compare_modifier_by_pritority)

func remove_modifier(modifier: Modifier):
	modifiers.erase(modifier)
