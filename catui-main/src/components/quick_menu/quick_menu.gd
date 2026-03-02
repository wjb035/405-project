extends Control

signal request_close
signal state_action_completed

const QUICK_BUTTON_SCENE = preload("res://src/components/quick_menu/quick_button.tscn")

# Based on user description: $Control/MarginContainer/ScrollContainer/VBoxContainer/
# We attempt to find this, or fallback to searching for a VBoxContainer
@onready var container = get_node_or_null("_/MarginContainer/ScrollContainer/VBoxContainer")
@onready var scroll_container = get_node_or_null("_/MarginContainer/ScrollContainer")

var buttons: Array = []
var focus_index: int = 0
var current_menu: String = "main" # main, load, save
var is_processing_action: bool = false

func _ready():
	if not container:
		container = find_child("VBoxContainer", true, false)
		if not container:
			push_warning("QuickMenu: No VBoxContainer found for buttons!")
	
	if not scroll_container:
		scroll_container = find_child("ScrollContainer", true, false)
		if not scroll_container:
			push_warning("QuickMenu: No ScrollContainer found for auto-scroll!")
	
	visible = false
	set_process_input(false)

func open():
	self.visible = true
	set_process_input(true)
	_show_main_options()
	
	# Ensure we are in front
	move_to_front()
	
	# Juice: Pop in
	scale = Vector2(0.8, 0.8)
	modulate.a = 0.0
	var tween = create_tween()
	tween.set_parallel(true)
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_BACK)
	tween.tween_property(self, "scale", Vector2.ONE, 0.3)
	tween.tween_property(self, "modulate:a", 1.0, 0.2)
	
	focus_index = 0
	_update_focus()

func close():
	set_process_input(false)
	# Juice: Pop out
	var tween = create_tween()
	tween.set_parallel(true)
	tween.set_ease(Tween.EASE_IN)
	tween.set_trans(Tween.TRANS_BACK)
	tween.tween_property(self, "scale", Vector2(0.8, 0.8), 0.2)
	tween.tween_property(self, "modulate:a", 0.0, 0.2)
	tween.chain().tween_callback(func(): 
		visible = false
		_reset_menu()
		request_close.emit()
	)

func _reset_menu():
	current_menu = "main"
	focus_index = 0
	is_processing_action = false
	_clear_container()

func _input(event):
	if not visible: return
	
	if is_processing_action:
		get_viewport().set_input_as_handled()
		return
	
	# debug input
	# if event.is_pressed(): print("QuickMenu input: ", event.as_text())
	
	if event.is_action_pressed("UI_UP"):
		_navigate(-1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_DOWN"):
		_navigate(1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_SELECT"): # Enter/Space
		_activate_current()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_BACK") or event.is_action_pressed("UI_MENU"):
		if current_menu == "main":
			close()
		else:
			_show_main_options()
		get_viewport().set_input_as_handled()

func _navigate(dir: int):
	if buttons.is_empty(): return
	var old_focus = focus_index
	focus_index = wrapi(focus_index + dir, 0, buttons.size())
	if old_focus != focus_index:

		_update_focus()

func _update_focus():
	for i in range(buttons.size()):
		if i < buttons.size():
			buttons[i].set_focused(i == focus_index)
	
	if scroll_container and focus_index >= 0 and focus_index < buttons.size():
		var focused_button = buttons[focus_index]
		
		await get_tree().process_frame
		
		var button_top = focused_button.position.y
		var button_bottom = button_top + focused_button.size.y
		var scroll_top = scroll_container.scroll_vertical
		var scroll_bottom = scroll_top + scroll_container.size.y
		
		if button_bottom > scroll_bottom:
			var target_scroll = button_bottom - scroll_container.size.y + 10
			scroll_container.scroll_vertical = int(target_scroll)
		elif button_top < scroll_top:
			var target_scroll = button_top - 10
			scroll_container.scroll_vertical = max(0, int(target_scroll))

func _activate_current():
	if buttons.is_empty(): return
	var btn = buttons[focus_index]
	var action = btn.action_name
	
	if SFX:
		SFX.play_select()
	
	match action:
		"menu_load":
			_show_load_options()
		"menu_save":
			_on_save_state_options()
		"no_action":
			pass
		_:
			if action.begins_with("load_file:"):
				var file_name = action.replace("load_file:", "")
				_load_state_by_filename(file_name)
			elif action.begins_with("save_slot_"):
				var slot = action.replace("save_slot_", "")
				_save_state_file(slot)

func _clear_container():
	for child in container.get_children():
		child.queue_free()
	buttons.clear()

func _add_button(text: String, action: String):
	var btn = QUICK_BUTTON_SCENE.instantiate()
	# attach script manually if scene doesn't have it
	container.add_child(btn)
	btn.setup(text, action)
	buttons.append(btn)

func _show_main_options():
	current_menu = "main"
	_clear_container()
	_add_button("Load", "menu_load")
	_add_button("Save", "menu_save")
	focus_index = 0
	_update_focus()

func _show_load_options():
	current_menu = "load"
	_clear_container()
	
	var rom_path = GlobalAutoload.current_rom_path
	if rom_path == "":
		_add_button("No game", "no_action")
		focus_index = 0
		_update_focus()
		return
	
	var game_name = rom_path.get_file().get_basename()
	var save_dir = "user://saves/%s/" % game_name
	
	if not DirAccess.dir_exists_absolute(save_dir):
		_add_button("No saves", "no_action")
		focus_index = 0
		_update_focus()
		return
	
	var dir = DirAccess.open(save_dir)
	if not dir:
		_add_button("Error", "no_action")
		focus_index = 0
		_update_focus()
		return
	
	var saves = []
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if not dir.current_is_dir() and file_name.ends_with(".state"):
			var json_file = file_name.replace(".state", ".json")
			var json_path = save_dir.path_join(json_file)
			
			var save_data = {
				"file": file_name,
				"timestamp": "",
				"type": "unknown"
			}
			
			if FileAccess.file_exists(json_path):
				var f = FileAccess.open(json_path, FileAccess.READ)
				if f:
					var json = JSON.new()
					if json.parse(f.get_as_text()) == OK:
						var data = json.data
						save_data["timestamp"] = data.get("timestamp", "")
						save_data["type"] = data.get("type", "unknown")
					f.close()
			
			saves.append(save_data)
		file_name = dir.get_next()
	
	if saves.is_empty():
		_add_button("No saves", "no_action")
		focus_index = 0
		_update_focus()
		return
	
	saves.sort_custom(func(a, b): return a["timestamp"] > b["timestamp"])
	
	var auto_counter = 1
	var slot_saves = []
	var auto_saves = []
	
	for save in saves:
		if save["type"] == "auto":
			auto_saves.append(save)
		else:
			slot_saves.append(save)
	
	for save in slot_saves:
		var label = ""
		var action = "load_file:" + save["file"]
		
		if save["type"] == "manual":
			var slot = save.get("slot", "?")
			label = "Slot %s" % slot
		else:
			label = save["file"].replace(".state", "")
		
		_add_button(label, action)
	
	for save in auto_saves:
		var label = "Auto %d" % auto_counter
		var action = "load_file:" + save["file"]
		_add_button(label, action)
		auto_counter += 1
	
	focus_index = 0
	_update_focus()

func _format_timestamp(timestamp: String) -> String:
	if timestamp == "":
		return "Unknown"
	
	var parts = timestamp.split("T")
	if parts.size() >= 2:
		var date = parts[0]
		var time = parts[1].split(".")[0]
		return "%s %s" % [date, time]
	return timestamp

func _on_save_state_options():
	current_menu = "save"
	_clear_container()
	
	for i in range(1, 4):
		_add_button("Slot %d" % i, "save_slot_%d" % i)
	
	focus_index = 0
	_update_focus()

func _load_state_file(slot: String):
	var rom_path = GlobalAutoload.current_rom_path
	if rom_path == "":
		print("quick_menu: no rom path found")
		close()
		return
		
	var game_name = rom_path.get_file().get_basename()
	var save_dir = "user://saves/%s/" % game_name
	
	if not DirAccess.dir_exists_absolute(save_dir):
		print("quick_menu: no saves folder found")
		close()
		return
	
	var state_file = ""
	if slot == "auto":
		state_file = _find_latest_auto_save(save_dir)
	else:
		state_file = "slot_%s.state" % slot
	
	if state_file == "":
		print("quick_menu: no save found for slot ", slot)
		close()
		return
		
	var state_path = save_dir.path_join(state_file)
	var global_path = ProjectSettings.globalize_path(state_path)
	
	if not FileAccess.file_exists(global_path):
		close()
		return
	
	var player = get_node_or_null("/root/LibretroPlayer")
	if player and player.has_method("LoadState"):
		player.LoadState(global_path)
	else:
		push_error("quick_menu: libretroplayer node not found!")
		
	state_action_completed.emit()
	
	is_processing_action = true
	await get_tree().create_timer(0.2).timeout
	close()

func _find_latest_auto_save(save_dir: String) -> String:
	var dir = DirAccess.open(save_dir)
	if not dir: return ""
	
	var auto_saves = []
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if not dir.current_is_dir() and file_name.begins_with("auto_") and file_name.ends_with(".state"):
			auto_saves.append(file_name)
		file_name = dir.get_next()
	
	if auto_saves.is_empty():
		return ""
	
	auto_saves.sort()
	return auto_saves[-1]

func _load_state_by_filename(file_name: String):
	var rom_path = GlobalAutoload.current_rom_path
	if rom_path == "":
		print("quick_menu: no rom path found")
		close()
		return
	
	var game_name = rom_path.get_file().get_basename()
	var save_dir = "user://saves/%s/" % game_name
	var state_path = save_dir.path_join(file_name)
	var global_path = ProjectSettings.globalize_path(state_path)
	
	if not FileAccess.file_exists(global_path):
		close()
		return
	
	var player = get_node_or_null("/root/LibretroPlayer")
	if player and player.has_method("LoadState"):
		player.LoadState(global_path)
	else:
		push_error("quick_menu: libretroplayer node not found!")
	
	state_action_completed.emit()
	
	is_processing_action = true
	await get_tree().create_timer(0.2).timeout
	close()

func _save_state_file(slot: String):
	var rom_path = GlobalAutoload.current_rom_path
	if rom_path == "":
		print("quick_menu: no rom path found for saving")
		close()
		return
		
	var game_name = rom_path.get_file().get_basename()
	var save_dir = "user://saves/%s/" % game_name
	
	if not DirAccess.dir_exists_absolute(save_dir):
		DirAccess.make_dir_recursive_absolute(save_dir)
	
	var timestamp = Time.get_datetime_string_from_system()
	var filename_base = "slot_%s" % slot
	var state_path = save_dir + filename_base + ".state"
	var img_path = save_dir + filename_base + ".png"
	var json_path = save_dir + filename_base + ".json"
	
	var global_state_path = ProjectSettings.globalize_path(state_path)
	var global_img_path = ProjectSettings.globalize_path(img_path)
	
	var player = get_node_or_null("/root/LibretroPlayer")
	if player:
		if player.has_method("SaveState"):
			player.SaveState(global_state_path)
		
		if player.has_method("CaptureScreenshot"):
			player.CaptureScreenshot(global_img_path)
		
		var meta = {
			"timestamp": timestamp,
			"type": "manual",
			"slot": slot,
			"rom_path": rom_path
		}
		var file = FileAccess.open(json_path, FileAccess.WRITE)
		if file:
			file.store_string(JSON.stringify(meta, "\t"))
			file.close()
	else:
		push_error("quick_menu: libretroplayer node not found!")
		
	state_action_completed.emit()
	
	is_processing_action = true
	await get_tree().create_timer(0.2).timeout
	close()
