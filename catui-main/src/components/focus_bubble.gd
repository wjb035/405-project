extends Control

@export var bubble_enabled: bool = true
@export var show_delay: float = 0.4
@export var auto_hide_delay: float = 3.0

@onready var label_node = $Label
@onready var animations = $AnimationPlayer

var hide_timer: Timer = null
var show_timer: Timer = null
var pending_position: Vector2 = Vector2.ZERO
var pending_name: String = ""
var is_animating: bool = false

func _ready():
	visible = false
	
	hide_timer = Timer.new()
	hide_timer.one_shot = true
	hide_timer.timeout.connect(_on_hide_timer_timeout)
	add_child(hide_timer)
	
	show_timer = Timer.new()
	show_timer.one_shot = true
	show_timer.timeout.connect(_on_show_timer_timeout)
	add_child(show_timer)

func show_at_position(target_pos: Vector2, item_name: String):
	if not bubble_enabled:
		return
	
	pending_position = target_pos
	pending_name = item_name
	
	if hide_timer.time_left > 0:
		hide_timer.stop()
	
	if show_timer.time_left > 0:
		show_timer.stop()
	
	if visible and not is_animating:
		_quick_hide()
	
	show_timer.start(show_delay)

func _quick_hide():
	is_animating = true
	animations.play_backwards("off")
	await animations.animation_finished
	visible = false
	is_animating = false

func _on_show_timer_timeout():
	if is_animating:
		return
	_do_show()

func _do_show():
	if is_animating:
		return
		
	is_animating = true
	
	if not visible:
		label_node.text = pending_name
		global_position = pending_position
		visible = true
		animations.play("on")
		await animations.animation_finished
	else:
		animations.play_backwards("off")
		await animations.animation_finished
		label_node.text = pending_name
		global_position = pending_position
		animations.play("on")
		await animations.animation_finished
	
	is_animating = false
	
	if auto_hide_delay > 0:
		hide_timer.start(auto_hide_delay)

func hide_bubble():
	if show_timer.time_left > 0:
		show_timer.stop()
	
	if hide_timer.time_left > 0:
		hide_timer.stop()
	
	if is_animating:
		return
	
	is_animating = true
	
	if visible:
		animations.play_backwards("off")
		await animations.animation_finished
		visible = false
	
	is_animating = false

func _on_hide_timer_timeout():
	hide_bubble()
