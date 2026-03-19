extends Node3D

enum State {
	LOOP,       
	HOVER,      
	OPEN,       
	DELETE 
}

@export var game_cover: MeshInstance3D

var current_state: State = State.LOOP
var time: float = 0.0

@onready var default_save_model = $save_icon

const BASE_ROT_X = 0.0
const BASE_ROT_Z = 0.0

var target_speed: float = 1.0
var current_speed: float = 1.0

var target_amp: float = 0.05
var current_amp: float = 0.05

var state_settings = {
	State.LOOP: { "speed": 1.0, "amp": 0.05 },
	State.HOVER: { "speed": 2.5, "amp": 0.2 },
	State.OPEN: { "speed": 1.5, "amp": 6.28 },
	State.DELETE: { "speed": 15.0, "amp": 0.1 }
}

func _ready():
	_update_targets()

func _process(delta):
	current_speed = lerp(current_speed, target_speed, delta * 5.0)
	current_amp = lerp(current_amp, target_amp, delta * 5.0)
	
	time += delta * current_speed
	
	if default_save_model:
		_animate_default_model()

func _animate_default_model():
	
	match current_state:
		State.OPEN:
			default_save_model.rotation.y = wrapf(time, 0, TAU)
			default_save_model.rotation.x = 0
			default_save_model.rotation.z = 0
			
		State.DELETE:
			default_save_model.rotation.y = sin(time) * current_amp
			default_save_model.rotation.z = cos(time * 0.8) * (current_amp * 0.5)
			
		_:
			default_save_model.rotation.y = sin(time) * current_amp
			
			default_save_model.rotation.x = 0.0
			default_save_model.rotation.z = 0.0

func set_focused(focused: bool):
	set_state(State.HOVER if focused else State.LOOP)

func set_state(new_state: State):
	if current_state == new_state:
		return
		
	current_state = new_state
	_update_targets()
	
	var anim_player = find_child("AnimationPlayer", true, false)
	if anim_player:
		var anim_name = ""
		match current_state:
			State.LOOP: anim_name = "loop"
			State.HOVER: anim_name = "hover"
			State.OPEN: anim_name = "open"
			State.DELETE: anim_name = "delete"
		
		if anim_player.has_animation(anim_name):
			anim_player.play(anim_name)

func _update_targets():
	if state_settings.has(current_state):
		var settings = state_settings[current_state]
		target_speed = settings["speed"]
		target_amp = settings["amp"]
