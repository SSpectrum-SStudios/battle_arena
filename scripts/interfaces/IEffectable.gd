class_name IEffectable

# Adds an effect (an object that adheres to IEffect)
func add_effect(effect: IEffect):
	push_error("add_effect() not implemented")

static func is_IEffectable(item: Node):
	return item.has_method("add_effect")
