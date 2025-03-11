extends CharacterBody3D

@onready var head: Node3D = %Head

# Change to Reference to components later
@export var speed: float = 8.0
@export var airspeed: float = 6.0
@export var jump_speed: float = 5.0
@export var gravity: float = 9.8
@export var mouse_sensitivity: float = 0.005

@onready var camera_3d: Camera3D = %Camera3D

var escaped: bool = false
func _ready() -> void:
	if not is_multiplayer_authority():
		return
		
	camera_3d.make_current()
	# Capture the mouse for camera control.
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	
func _input(event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return
	if event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
		escaped = true
	if event is InputEventMouseButton:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
		escaped = false
	if event is InputEventMouseMotion and not escaped:
		# Rotate the body left/right (yaw) based on horizontal mouse movement.
		rotate_y(-event.relative.x * mouse_sensitivity)
		
		# Rotate the head up/down (pitch) based on vertical mouse movement.
		# We work in degrees and clamp the pitch between -90 and 90 degrees.
		var current_pitch = head.rotation_degrees.x
		current_pitch -= event.relative.y * mouse_sensitivity * 100.0
		current_pitch = clamp(current_pitch, -90, 90)
		head.rotation_degrees.x = current_pitch

func _physics_process(delta: float) -> void:
	if not is_multiplayer_authority():
		return
	var input_direction: Vector3 = Vector3.ZERO
	
	# Get local directions.
	var forward: Vector3 = -transform.basis.z
	var right: Vector3 = transform.basis.x
	
	# Build the input direction vector.
	if Input.is_action_pressed("move_forward"):
		input_direction += forward
	if Input.is_action_pressed("move_backward"):
		input_direction -= forward
	if Input.is_action_pressed("move_right"):
		input_direction += right
	if Input.is_action_pressed("move_left"):
		input_direction -= right
		
	input_direction = input_direction.normalized()
	
	# Update horizontal velocity.
	var horizontal_velocity
	if is_on_floor():
		horizontal_velocity = input_direction * speed
	else:
		horizontal_velocity = input_direction * airspeed
		
	velocity.x = horizontal_velocity.x
	velocity.z = horizontal_velocity.z
	
	# Handle gravity and jump.
	if not is_on_floor():
		velocity.y -= gravity * delta
	else:
		if Input.is_action_just_pressed("jump"):
			velocity.y = jump_speed
	
	# Move the character and slide on collisions.
	move_and_slide()
