extends MenuBase

signal item_selected(item_name: String)

@onready var options_container = $_/options/HBoxContainer
@onready var continue_button = $_/options/HBoxContainer/continue

@export var reset_focus_on_open: bool = false
@export var initial_focus: int = 0

@export_group("Animation")
@export var scale_focused: float = 1.3
@export var scale_normal: float = 1.0
@export var lift_amount: float = -25.0
@export var anim_duration: float = 0.15
@export var bounce_scale: float = 1.12
@export var input_delay: float = 0.15

var menu_items: Array[Control] = []
var base_positions: Dictionary = {}
var current_focus: int = 0
var tween: Tween = null
var initialized: bool = false

var quick_menu: Control = null
var _quick_menu_active: bool = false


func on_open():
	if reset_focus_on_open:
		current_focus = initial_focus
	else:
		if not menu_items.is_empty():
			current_focus = clampi(current_focus, 0, menu_items.size() - 1)

	_kill_tween()
	_refresh_items()
	
	call_deferred("_delayed_focus_update")
	
	is_active = false
	get_tree().create_timer(input_delay).timeout.connect(func(): is_active = true)

func on_close():
	if _quick_menu_active and quick_menu:
		quick_menu.close()

func on_input(event: InputEvent):
	if event.is_action_pressed("UI_LEFT"):
		get_viewport().set_input_as_handled()
		_navigate(-1)
	elif event.is_action_pressed("UI_RIGHT"):
		get_viewport().set_input_as_handled()
		_navigate(1)
	elif event.is_action_pressed("UI_SELECT"):
		get_viewport().set_input_as_handled()
		_select_current()
	elif event.is_action_pressed("UI_MENU"):
		if _can_open_quick_menu():
			_open_quick_menu()
		else:
			get_viewport().set_input_as_handled()

func _can_open_quick_menu() -> bool:
	if not continue_button or not continue_button.visible:
		return false
	if current_focus < 0 or current_focus >= menu_items.size():
		return false
	var current_item = menu_items[current_focus]
	if current_item != continue_button:
		return false
	
	return true 

func _open_quick_menu():
	if not quick_menu: 
		return
	
	_quick_menu_active = true
	set_process_input(false) # freeze input so we dont click random stuff behind
	quick_menu.open()


func handle_back():
	if _quick_menu_active and quick_menu:
		quick_menu.close()
		return

	if SFX:
		SFX.play_back()
	item_selected.emit("continue")

func _ready():
	super._ready()
	
	current_focus = initial_focus
	if continue_button:
		continue_button.visible = false
	
	# searching for quick menu on the continue button
	if continue_button:
		quick_menu = continue_button.get_node_or_null("quick_menu")
		if quick_menu and quick_menu.has_method("close"):
			quick_menu.request_close.connect(func():
				_quick_menu_active = false
				set_process_input(true)
			)
			if quick_menu.has_signal("state_action_completed"):
				quick_menu.state_action_completed.connect(func():
					_quick_menu_active = false
					set_process_input(true)
					# tiny nap to stop input bleeding
					await get_tree().create_timer(0.1).timeout
					item_selected.emit("continue")
				)
			quick_menu.visible = false
	
	_refresh_items()
	call_deferred("_apply_focus_instant")
	
	initialized = true

func _delayed_focus_update():
	await get_tree().process_frame
	await get_tree().process_frame
	_refresh_items()
	_apply_focus_instant()

func _refresh_items():
	menu_items.clear()
	
	for child in options_container.get_children():
		if child is Control and child.visible:
			menu_items.append(child)
			child.pivot_offset = Vector2(child.size.x / 2.0, child.size.y)
			if not base_positions.has(child):
				base_positions[child] = child.position

func set_continue_visible(should_show: bool):
	if not continue_button:
		return
	
	var was_hidden = not continue_button.visible
	continue_button.visible = should_show
	
	if should_show and GlobalAutoload.current_game_texture:
		var texture_rect = continue_button.get_node_or_null("icon_texture/TextureRect")
		if texture_rect:
			texture_rect.texture = GlobalAutoload.current_game_texture
	
	if not initialized:
		return
	
	_refresh_items()
	
	if should_show and was_hidden:
		current_focus = 0
	else:
		current_focus = clampi(current_focus, 0, max(0, menu_items.size() - 1))
	
	_apply_focus_instant()

func _navigate(direction: int):
	if menu_items.is_empty():
		return
	
	if SFX:
		SFX.play_nav()
	
	var old_focus = current_focus
	current_focus = wrapi(current_focus + direction, 0, menu_items.size())
	
	# if we leave continue button, bye quick menu
	if _quick_menu_active and quick_menu:
		var old_item = menu_items[old_focus] if old_focus < menu_items.size() else null
		var new_item = menu_items[current_focus] if current_focus < menu_items.size() else null
		
		if old_item == continue_button and new_item != continue_button:
			quick_menu.close()
	
	_animate_focus_change(old_focus, current_focus)

func _select_current():
	if current_focus >= menu_items.size():
		return
	
	if SFX:
		SFX.play_select()
	
	var item = menu_items[current_focus]
	_play_bounce(item)
	item_selected.emit(item.name)

func _apply_focus_instant():
	_kill_tween()
	
	for i in range(menu_items.size()):
		var item = menu_items[i]
		if not base_positions.has(item):
			continue
		
		var is_focused = (i == current_focus)
		var base_y = base_positions[item].y
		
		item.scale = Vector2.ONE * (scale_focused if is_focused else scale_normal)
		item.position.y = base_y + (lift_amount if is_focused else 0.0)
		
		_update_focus_indicator(item, is_focused)

func _animate_focus_change(old_index: int, new_index: int):
	_kill_tween()
	
	tween = create_tween()
	tween.set_parallel(true)
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_CUBIC)
	
	if old_index >= 0 and old_index < menu_items.size():
		var old_item = menu_items[old_index]
		if base_positions.has(old_item):
			var base_y = base_positions[old_item].y
			tween.tween_property(old_item, "scale", Vector2.ONE * scale_normal, anim_duration)
			tween.tween_property(old_item, "position:y", base_y, anim_duration)
			_update_focus_indicator(old_item, false)
	
	if new_index >= 0 and new_index < menu_items.size():
		var new_item = menu_items[new_index]
		if base_positions.has(new_item):
			var base_y = base_positions[new_item].y
			tween.tween_property(new_item, "scale", Vector2.ONE * scale_focused, anim_duration)
			tween.tween_property(new_item, "position:y", base_y + lift_amount, anim_duration)
			_update_focus_indicator(new_item, true)

func _play_bounce(item: Control):
	_kill_tween()
	
	tween = create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_BACK)
	tween.tween_property(item, "scale", Vector2.ONE * scale_focused * bounce_scale, 0.08)
	tween.tween_property(item, "scale", Vector2.ONE * scale_focused, 0.12)

func _update_focus_indicator(item: Control, should_show: bool):
	var focus_panel = item.get_node_or_null("style/focus")
	if focus_panel:
		focus_panel.visible = should_show

func _kill_tween():
	if tween and tween.is_valid():
		tween.kill()
		tween = null
