extends Node

signal setting_changed(key: String, value)
signal settings_loaded()

const CONFIG_FILE = "user://settings.json"

var settings: Dictionary = {}
var defaults: Dictionary = {}

func _ready():
	_register_defaults()
	load_settings()

func _register_defaults():
	defaults = {
		"fullscreen": false,
		"vsync": true,
		"fps_limit": 60,
		"emu_volume": 0.8,
		"sfx_volume": 0.8,
		"controller_layout": 0,
		"focus_bubble": true,
		"bubble_delay": 0.3,
		"bubble_autohide": 2.0,
		"home_menu_reset": false,
		"hm_scale_focused": 0.3,
		"hm_scale_normal": 0.0,
		"hm_lift_amount": 0.25,
		"hm_anim_duration": 0.2,
		"hm_bounce_scale": 0.12,
		"hm_input_delay": 0.3,
		"max_save_states": 6,
		"auto_save_interval": 1,
	}

func get_setting(key: String, default_value = null):
	if settings.has(key):
		return settings[key]
	if defaults.has(key):
		return defaults[key]
	return default_value

func set_setting(key: String, value, skip_save: bool = false):
	var old_value = settings.get(key)
	if old_value != value:
		settings[key] = value
		setting_changed.emit(key, value)
		if not skip_save:
			save_settings()

func save_settings():
	var data = {
		"general": settings,
		"keybinds": _serialize_keybinds()
	}
	var file = FileAccess.open(CONFIG_FILE, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(data, "\t"))
		print("settings_manager: settings saved")

func load_settings():
	if not FileAccess.file_exists(CONFIG_FILE):
		print("settings_manager: no config file, using defaults")
		settings = defaults.duplicate()
		settings_loaded.emit()
		return
	
	var file = FileAccess.open(CONFIG_FILE, FileAccess.READ)
	if not file:
		settings = defaults.duplicate()
		settings_loaded.emit()
		return
	
	var json = JSON.new()
	if json.parse(file.get_as_text()) == OK:
		var data = json.data
		if data.has("general"):
			settings = data["general"]
			
			for key in defaults:
				if not settings.has(key):
					settings[key] = defaults[key]
		
		if data.has("keybinds"):
			_deserialize_keybinds(data["keybinds"])
		
		print("settings_manager: settings loaded")
	else:
		settings = defaults.duplicate()
	
	settings_loaded.emit()

func _serialize_keybinds() -> Dictionary:
	var keybinds = {}
	for action in InputMap.get_actions():
		if action.begins_with("ui_") or action.begins_with("UI_"):
			continue
		
		var events = InputMap.action_get_events(action)
		if events.is_empty():
			continue
		
		var event = events[0]
		var event_data = {}
		
		if event is InputEventKey:
			event_data["type"] = "key"
			event_data["keycode"] = event.keycode
		elif event is InputEventJoypadButton:
			event_data["type"] = "joypad_button"
			event_data["button_index"] = event.button_index
		elif event is InputEventJoypadMotion:
			event_data["type"] = "joypad_motion"
			event_data["axis"] = event.axis
			event_data["axis_value"] = event.axis_value
		
		if not event_data.is_empty():
			keybinds[action] = event_data
	
	return keybinds

func _deserialize_keybinds(keybinds: Dictionary):
	for action in keybinds:
		if not InputMap.has_action(action):
			continue
		
		var event_data = keybinds[action]
		var event: InputEvent = null
		
		match event_data.get("type"):
			"key":
				var key_event = InputEventKey.new()
				key_event.keycode = event_data.get("keycode", 0)
				event = key_event
			"joypad_button":
				var btn_event = InputEventJoypadButton.new()
				btn_event.button_index = event_data.get("button_index", 0)
				event = btn_event
			"joypad_motion":
				var motion_event = InputEventJoypadMotion.new()
				motion_event.axis = event_data.get("axis", 0)
				motion_event.axis_value = event_data.get("axis_value", 0.0)
				event = motion_event
		
		if event:
			InputMap.action_erase_events(action)
			InputMap.action_add_event(action, event)
