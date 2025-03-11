extends Node

static var id_counter: int = -1

##Enums 
enum DamageType {
	PHYSICAL_DAMAGE,
	FIRE_DAMAGE
}

enum ModifierPriority {
	HIGHEST = 0,
	HIGH = 1,
	NORMAL = 2,
	LOW = 3,
	LOWEST = 4,
}

## Signals
signal on_damage_taken(damage_context: DamageContext)
signal hit_received(hit_context: HitContext)
signal hit_modified(hit_context: HitContext)


## Static Utility Functions

static func get_core_effect(effect: IEffect) -> IEffect:
	if effect is IContainerEffect:
		return effect.get_nested_effect()
	return effect

func get_new_id() -> int:
	if is_multiplayer_authority():
		id_counter += 1
		return id_counter
	return -1
