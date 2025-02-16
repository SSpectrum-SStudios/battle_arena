extends BaseComponent
class_name HittableComponent

signal propagate_modifier(modifiers_to_propagate: Array[Modifier])
@onready var hittable_area: IHittable = %Area3D

func _ready() -> void:
	hittable_area.take_hit.connect(on_hittable_hit)

func add_modifier(modifier: Modifier):
	modifier.apply_modifier(self)

func on_hittable_hit(attack_payload: AttackPayload):
	propagate_modifier.emit(attack_payload.attacking_modifiers)
