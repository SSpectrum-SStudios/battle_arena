extends Node
class_name HealthComponent

const IModifiable = preload("res://scripts/interfaces/IModifiable.gd")
const IEffectable = preload("res://scripts/interfaces/IEffectable.gd")

@export var health: HealthResource = HealthResource.new()
@export var modifiers: Array[IModifier] = []
@export var entity_id: int

var modified_health_cached : HealthResource

@export var synced_max_health: float: 
	get:
		return health.max_health
	set(value):
		health.max_health = value
		
@export var synced_current_health: float: 
	get:
		return health.current_health
	set(value):
		health.current_health = value 

@export var synced_mod_max_health: float: 
	get:
		return modified_health_cached.max_health
	set(value):
		modified_health_cached.max_health = value
		
@export var synced_mod_current_health: float: 
	get:
		return modified_health_cached.current_health
	set(value):
		modified_health_cached.current_health = value 
		
signal health_at_zero
# Adds a modifier (an object that adheres to IModifier)
func add_modifier(modifier: IModifier):
	modifiers.append(modifier)
	modifiers.sort_custom(IModifier.compare_modifier_by_pritority)
	_recalculate_modified_hp()

# Removes a modifier.
func remove_modifier(modifier: IModifier):
	modifiers.erase(modifier)
	_recalculate_modified_hp()
	
# Returns the value before any modifiers have been applied. Read Only	
func get_base_value():
	return self.health.duplicate(true)

# Returns the value after all modifiers have been applied.
func get_modified_value():
	return modified_health_cached
	
func add_effect(effect: IEffect):
	effect.apply_effect(self)

func set_id(id: int):
	entity_id = id

func _ready() -> void:
	health.current_health = health.max_health
	self.modified_health_cached = health.duplicate(true)
	self._recalculate_modified_hp()

func take_damage(damage_context: DamageContext):
	if not is_multiplayer_authority():
		return
	if modified_health_cached.current_health - damage_context.damage_amount <= 0:
		damage_context.damage_amount = modified_health_cached.current_health
		modified_health_cached.current_health = 0
		health_at_zero.emit()
	else:
		modified_health_cached.current_health -= damage_context.damage_amount
		
	Globals.on_damage_taken.emit(damage_context)
	print("Taking damage: ", damage_context.damage_amount)
	print("Current Health: ", self.modified_health_cached.current_health)
	
func heal(hp_to_heal: float):
	if not is_multiplayer_authority():
		return
	modified_health_cached.current_health += hp_to_heal
	if modified_health_cached.current_health > modified_health_cached.max_health:
		modified_health_cached.current_health = modified_health_cached.max_health
	print("Healing: ", hp_to_heal)
	print("Current Health: ", self.modified_health_cached.current_health)

func _recalculate_modified_hp():
	if not is_multiplayer_authority():
		return
	var ratio: float = modified_health_cached.current_health / modified_health_cached.max_health
	self.health.current_health = self.health.max_health * ratio
	self.modified_health_cached = health.duplicate(true)
	for modifier in self.modifiers:
		self.modified_health_cached = modifier.modify(self.modified_health_cached)
