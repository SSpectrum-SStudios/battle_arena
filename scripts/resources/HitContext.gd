extends Resource
class_name HitContext

@export var hit_entity_id: int #ID of entity that was hit
@export var attack_payload: AttackPayload # Attack Payload that was received

func _init(id: int, payload: AttackPayload) -> void:
	self.hit_entity_id = id
	self.attack_payload = payload

static func copy(context_to_copy: HitContext) -> HitContext:
	var id = context_to_copy.hit_entity_id
	var payload = AttackPayload.copy(context_to_copy.attack_payload)
	return HitContext.new(id, payload)
