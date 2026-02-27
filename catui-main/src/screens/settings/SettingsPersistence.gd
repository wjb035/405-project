extends Node

const GLOBAL_CONFIG_FILE = "user://settings.json"

var global_settings_cache: Dictionary = {}

func _ready():
	load_global_settings()
	load_keybinds()
	await get_tree().process_frame
	_apply_saved_settings()

func _apply_saved_settings():
	for key in global_settings_cache.keys():
		var value = global_settings_cache[key]
		_apply_global_setting(key, value)

func load_global_settings():
	if not FileAccess.file_exists(GLOBAL_CONFIG_FILE):
		return
	
	var file = FileAccess.open(GLOBAL_CONFIG_FILE, FileAccess.READ)
	if not file:
		return
	
	var json_string = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var error = json.parse(json_string)
	
	if error == OK:
		global_settings_cache = json.data
	else:
		push_error("SettingsPersistence: cant read global settings oops")

func save_global_settings():
	var file = FileAccess.open(GLOBAL_CONFIG_FILE, FileAccess.WRITE)
	if not file:
		push_error("SettingsPersistence: cant save global settings :(")
		return
	
	var json_string = JSON.stringify(global_settings_cache, "\t")
	file.store_string(json_string)
	file.close()

func load_setting_value(item: Dictionary):
	if item.has("emulator_id"):
		return _load_emulator_setting(item)
	else:
		return _load_global_setting(item)

func save_setting_value(item: Dictionary, value):
	if item.has("emulator_id"):
		_save_emulator_setting(item, value)
	else:
		_save_global_setting(item, value)

func _load_emulator_setting(item: Dictionary):
	if not EmulatorConfig:
		return item.get("default")
	
	return EmulatorConfig.get_emulator_setting(
		item["emulator_id"],
		item.get("category", "core"),
		item["key"],
		item.get("default")
	)

func _save_emulator_setting(item: Dictionary, value):
	if not EmulatorConfig:
		return
	
	EmulatorConfig.update_emulator_setting(
		item["emulator_id"],
		item.get("category", "core"),
		item["key"],
		value
	)

func _load_global_setting(item: Dictionary):
	if not item.has("key"):
		return item.get("default")
	
	return global_settings_cache.get(item["key"], item.get("default"))

func _save_global_setting(item: Dictionary, value):
	if not item.has("key"):
		return
	
	global_settings_cache[item["key"]] = value
	save_global_settings()
	
	_apply_global_setting(item["key"], value)

func _apply_global_setting(key: String, value):
	match key:
		"fullscreen":
			if OS.get_name() != "Android":
				if value:
					DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
				else:
					DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
		
		"vsync":
			if OS.get_name() != "Android":
				if value:
					DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_ENABLED)
				else:
					DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		
		"fps_limit":
			Engine.max_fps = int(value) if typeof(value) == TYPE_INT else 60
		
		"emu_volume":
			var main = get_tree().root.get_node_or_null("main")
			if main:
				var game_audio = main.get_node_or_null("Menus/game_screen/AudioStreamPlayer")
				if game_audio:
					game_audio.volume_db = linear_to_db(value / 100.0)
		
		"sfx_volume":
			if SFX:
				SFX.master_volume_db = linear_to_db(value / 100.0)
		
		"debug_console":
			var main = get_tree().root.get_node_or_null("main")
			if main:
				var debug = main.get_node_or_null("DEBUG")
				if debug:
					debug.visible = value
		
		"focus_bubble_enabled":
			var main = get_tree().root.get_node_or_null("main")
			if main:
				var home = main.get_node_or_null("Menus/home")
				if home:
					var bubble = home.get_node_or_null("_/focus_bubble")
					if bubble:
						bubble.bubble_enabled = value
		
		"focus_bubble_opacity":
			var main = get_tree().root.get_node_or_null("main")
			if main:
				var home = main.get_node_or_null("Menus/home")
				if home:
					var bubble = home.get_node_or_null("_/focus_bubble")
					if bubble: bubble.modulate.a = value
		
		"focus_anim_speed":
			var main = get_tree().root.get_node_or_null("main")
			if main:
				var home = main.get_node_or_null("Menus/home")
				if home:
					var bubble = home.get_node_or_null("_/focus_bubble")
					if bubble:
						match value:
							"Slow": bubble.show_delay = 0.6
							"Normal": bubble.show_delay = 0.4
							"Fast": bubble.show_delay = 0.2
		
		"home_show_clock":
			var main = get_tree().root.get_node_or_null("main")
			if main:
				var home = main.get_node_or_null("Menus/home_menu")
				if home: home.set_meta("show_clock", value)
		
		"home_show_battery":
			var main = get_tree().root.get_node_or_null("main")
			if main:
				var home = main.get_node_or_null("Menus/home_menu")
				if home: home.set_meta("show_battery", value)
		
		"controller_layout":
			if ButtonLayoutManager:
				var layout = ButtonLayoutManager.ControllerLayout.XBOX if value == "Xbox" else ButtonLayoutManager.ControllerLayout.PLAYSTATION
				ButtonLayoutManager.set_layout(layout)

const KEYBINDS_FILE = "user://keybinds.json"

func save_keybinds():
	var binds = {}
	for action in InputMap.get_actions():
		if str(action).begins_with("ui_"): continue # nah we dont need ui actions
		
		var events = InputMap.action_get_events(action)
		var events_data = []
		for event in events:
			if event is InputEventKey:
				events_data.append({"type": "key", "code": event.physical_keycode})
			elif event is InputEventJoypadButton:
				events_data.append({"type": "joy_btn", "index": event.button_index})
			elif event is InputEventJoypadMotion:
				events_data.append({"type": "joy_axis", "axis": event.axis, "value": event.axis_value})
		
		if not events_data.is_empty():
			binds[action] = events_data
	
	var file = FileAccess.open(KEYBINDS_FILE, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(binds, "\t"))
		file.close()

func load_keybinds():
	if not FileAccess.file_exists(KEYBINDS_FILE):
		return
		
	var file = FileAccess.open(KEYBINDS_FILE, FileAccess.READ)
	if not file: return
	
	var json = JSON.new()
	if json.parse(file.get_as_text()) == OK:
		var binds = json.data
		for action in binds:
			if InputMap.has_action(action):
				InputMap.action_erase_events(action)
				for ev_data in binds[action]:
					var event = null
					match ev_data["type"]:
						"key":
							event = InputEventKey.new()
							event.physical_keycode = int(ev_data["code"])
						"joy_btn":
							event = InputEventJoypadButton.new()
							event.button_index = int(ev_data["index"])
						"joy_axis":
							event = InputEventJoypadMotion.new()
							event.axis = int(ev_data["axis"])
							event.axis_value = float(ev_data.get("value", 1.0))
					
					if event:
						InputMap.action_add_event(action, event)
