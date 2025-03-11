class_name IModifiable

# Adds a modifier (an object that adheres to IModifier)
func add_modifier(modifier):
	# This function should be overridden in the concrete class.
	push_error("add_modifier() not implemented")

# Removes a modifier.
func remove_modifier(modifier):
	# This function should be overridden in the concrete class.
	push_error("remove_modifier() not implemented")
	
# Returns the value before any modifiers have been applied.	
func get_base_value():
	# This function should be overridden in the concrete class.
	push_error("get_base_value() not implemented")

# Returns the value after all modifiers have been applied.
func get_modified_value():
	# This function should be overridden in the concrete class.
	push_error("get_modified_value() not implemented")

static func is_IModifiable(item):
	var value: bool = item.has_method("add_modifier")
	value = value && item.has_method("remove_modifier")
	value = value && item.has_method("get_base_value")
	value = value && item.has_method("get_modified_value")
	return item.has("IModifiable") && value
