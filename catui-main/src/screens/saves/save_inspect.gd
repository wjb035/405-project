extends MenuBase

signal action_selected(action, save_data)

@onready var preview_container = $Control
@onready var options_container = $home_options_menu/Control/ScrollContainer/VBoxContainer
@onready var options_scroll = $home_options_menu/Control/ScrollContainer

const SAVE_INSTANCE_SCENE = preload("res://src/screens/saves/save_instance.tscn")
const SAVE_COMPONENT_SCENE = preload("res://src/screens/saves/save_component.tscn")
const SAVE_COMPONENT_SCRIPT = preload("res://src/screens/saves/save_component.gd")

var current_save_data: Dictionary = {}
var current_save_path: String = ""

var focused_option_index: int = 0
var option_items: Array = []


func setup_context(save_data: Dictionary, save_path: String):
	current_save_data = save_data
	current_save_path = save_path


func on_open():
	_create_preview_icon()
	_create_option_list()
	
	focused_option_index = 0
	
	if options_scroll:
		options_scroll.scroll_vertical = 0
		
	await get_tree().process_frame
	_update_focus_visuals()
	_scroll_to_active()
	
	pivot_offset = size / 2
	scale = Vector2(1.1, 1.1)
	modulate.a = 0.0
	
	var tween = create_tween()
	tween.set_parallel(true)
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.tween_property(self, "scale", Vector2.ONE, 0.3)
	tween.tween_property(self, "modulate:a", 1.0, 0.2)
	
	var instance = preview_container.get_child(0) if preview_container.get_child_count() > 0 else null
	if instance and instance.has_method("set_animation_state"):
		instance.set_animation_state("OPEN")

func close():
	var tween = create_tween()
	tween.set_parallel(true)
	tween.set_ease(Tween.EASE_IN)
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.tween_property(self, "scale", Vector2(1.1, 1.1), 0.2)
	tween.tween_property(self, "modulate:a", 0.0, 0.2)
	
	await tween.finished
	super.close()

func on_close():
	pass

func on_input(event: InputEvent):
	if event.is_action_pressed("UI_UP"):
		get_viewport().set_input_as_handled()
		_navigate(-1)
		
	elif event.is_action_pressed("UI_DOWN"):
		get_viewport().set_input_as_handled()
		_navigate(1)
		
	elif event.is_action_pressed("UI_SELECT"):
		get_viewport().set_input_as_handled()
		_select_current_option()


func _create_preview_icon():
	for child in preview_container.get_children():
		child.queue_free()
	
	var instance = SAVE_INSTANCE_SCENE.instantiate()
	preview_container.add_child(instance)
	
	instance.set_anchors_preset(Control.PRESET_FULL_RECT)
	instance.offset_left = 0
	instance.offset_top = 0
	instance.offset_right = 0
	instance.offset_bottom = 0
	instance.grow_horizontal = Control.GROW_DIRECTION_BOTH
	instance.grow_vertical = Control.GROW_DIRECTION_BOTH
	
	if instance.has_method("setup"):
		instance.setup(current_save_data, current_save_path)
	
	if instance.has_method("set_focused"):
		instance.set_focused(false)
		
	if instance.has_method("set_animation_state"):
		instance.set_animation_state("OPEN")

func _create_option_list():
	for child in options_container.get_children():
		child.queue_free()
	option_items.clear()
	
	if current_save_path == "": return
	
	var dir = DirAccess.open(current_save_path)
	if dir:
		var files = []
		dir.list_dir_begin()
		var f = dir.get_next()
		while f != "":
			if not dir.current_is_dir() and f.ends_with(".json"):
				files.append(f)
			f = dir.get_next()
			
		files.sort()
		files.reverse()
		
		for fs in files:
			var full_path = current_save_path.path_join(fs)
			var meta_data = _quick_load_meta(full_path)
			
			var label = "Load Game"
			if meta_data.has("timestamp"):
				var ts = meta_data["timestamp"]
				var parts = ts.split("T")
				if parts.size() == 2:
					var date_parts = parts[0].split("-")
					var time_parts = parts[1].split(":")
					if date_parts.size() == 3:
						label = date_parts[2] + "/" + date_parts[1] + " " + time_parts[0] + ":" + time_parts[1]
					else:
						label = ts
			
			var icon_path = ""
			if meta_data.has("image_path"):
				icon_path = meta_data["image_path"]
				
			_add_menu_option(label, icon_path, "load_file", full_path, meta_data)

func _quick_load_meta(path: String) -> Dictionary:
	var f = FileAccess.open(path, FileAccess.READ)
	if f:
		var json = JSON.new()
		if json.parse(f.get_as_text()) == OK:
			var data = json.data
			if not data.has("image_path"):
				data["image_path"] = path.replace(".json", ".png")
			return data
	return {}

func _add_menu_option(label: String, icon_path: String, action: String, file_path: String = "", meta_data: Dictionary = {}):
	var instance = SAVE_COMPONENT_SCENE.instantiate()
	instance.set_script(SAVE_COMPONENT_SCRIPT)
	options_container.add_child(instance)
	
	if instance.has_method("setup"):
		instance.setup(label, icon_path, action)
	
	instance.set_meta("file_path", file_path)
	instance.set_meta("save_data", meta_data)
	
	option_items.append(instance)

func _navigate(direction: int):
	if option_items.is_empty(): return
	
	focused_option_index = wrapi(focused_option_index + direction, 0, option_items.size())
	_update_focus_visuals()
	_scroll_to_active()
	_update_preview_from_selection()

func _update_preview_from_selection():
	if option_items.is_empty(): return
	var item = option_items[focused_option_index]
	var meta = item.get_meta("save_data") if item.has_meta("save_data") else {}
	var path = item.get_meta("file_path") if item.has_meta("file_path") else ""
	
	if meta.is_empty(): return
	
	var instance = preview_container.get_child(0) if preview_container.get_child_count() > 0 else null
	if instance and instance.has_method("setup"):
		instance.setup(meta, path)

func _update_focus_visuals():
	for i in range(option_items.size()):
		var item = option_items[i]
		if item.has_method("set_focused"):
			item.set_focused(i == focused_option_index)

func _scroll_to_active():
	if options_scroll and not option_items.is_empty():
		var item = option_items[focused_option_index]
		if options_scroll.has_method("ensure_control_visible"):
			options_scroll.ensure_control_visible(item)

func _select_current_option():
	var item = option_items[focused_option_index]
	var action = item.action_name
	
	if action == "delete_file":
		pass
			
	var save_data = item.get_meta("save_data")
	
	emit_signal("action_selected", action, save_data)
