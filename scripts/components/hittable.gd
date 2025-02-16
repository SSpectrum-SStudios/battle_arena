extends Area3D
class_name IHittable

signal take_hit(attack_payload: AttackPayload)

func hit(attack_payload: AttackPayload):
	take_hit.emit(attack_payload)
