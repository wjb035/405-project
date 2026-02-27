extends Node

const SETTINGS_BUTTONS = preload("res://src/components/settings_button.tscn")
const SettingsData = preload("res://src/screens/settings/SettingsData.gd")

func create_buttons(menu_data: Dictionary, persistence: Node) -> Array:
	var buttons = []
	
	if not menu_data.has("items"):
		return buttons
	
	for item in menu_data["items"]:
		var button = _create_button(item, persistence)
		if button:
			buttons.append(button)
	
	return buttons

func _create_button(item: Dictionary, persistence: Node):
	var button = SETTINGS_BUTTONS.instantiate()
	
	var type_name = item.get("type", "BUTTON")
	button.button_type = SettingsData.BUTTON_TYPES.get(type_name, 1)
	button.label_text = item.get("label", "")
	
	match type_name:
		"CHECKBOX":
			_setup_checkbox(button, item, persistence)
		
		"SLIDER":
			_setup_slider(button, item, persistence)
		
		"SELECTOR":
			_setup_selector(button, item, persistence)
		
		"SUBMENU":
			_setup_submenu(button, item)
		
		"KEYBIND":
			_setup_keybind(button, item)
		
		"BUTTON":
			_setup_action_button(button, item)
		
		"SEPARATOR":
			pass
	
	return button

func _setup_checkbox(button, item: Dictionary, persistence: Node):
	var current_value = persistence.load_setting_value(item)
	button.checkbox_value = current_value if typeof(current_value) == TYPE_BOOL else false
	
	button.value_changed.connect(func(value):
		persistence.save_setting_value(item, value)
	)

func _setup_slider(button, item: Dictionary, persistence: Node):
	button.slider_min = item.get("min", 0.0)
	button.slider_max = item.get("max", 100.0)
	button.slider_step = item.get("step", 1.0)
	
	var current_value = persistence.load_setting_value(item)
	if current_value != null:
		var float_val = float(current_value)
		var normalized = (float_val - button.slider_min) / (button.slider_max - button.slider_min)
		button.slider_value = clamp(normalized, 0.0, 1.0)
	else:
		# if we found nothing just use default
		var def = item.get("default", button.slider_min)
		var normalized = (float(def) - button.slider_min) / (button.slider_max - button.slider_min)
		button.slider_value = clamp(normalized, 0.0, 1.0)
	
	button.value_changed.connect(func(value):
		persistence.save_setting_value(item, value)
	)

func _setup_selector(button, item: Dictionary, persistence: Node):
	var options = item.get("options", [])
	button.selector_options = options
	
	var current_value = persistence.load_setting_value(item)
	var index = options.find(current_value)
	button.selector_index = index if index >= 0 else 0
	
	button.value_changed.connect(func(value):
		persistence.save_setting_value(item, value)
	)
	
	# gotta carry that info
	if item.has("action"):
		button.set_meta("action", item.get("action"))
	if item.has("emulator_id"):
		button.set_meta("emulator_id", item.get("emulator_id"))
	if item.has("disabled"):
		button.set_meta("disabled", item.get("disabled"))

func _setup_submenu(button, item: Dictionary):
	button.submenu_id = item.get("submenu", "")

func _setup_keybind(button, item: Dictionary):
	button.action_name = item.get("action", "")
	
	if InputMap.has_action(button.action_name):
		var events = InputMap.action_get_events(button.action_name)
		if events.size() > 0:
			button.keybind_text = _get_event_string(events[0])
		else:
			button.keybind_text = "---"
	else:
		button.keybind_text = "---"

func _setup_action_button(button, item: Dictionary):
	button.set_meta("action", item.get("action", ""))
	
	# save who we are dealing with
	if item.has("emulator_id"):
		button.set_meta("emulator_id", item["emulator_id"])

func _get_event_string(event: InputEvent) -> String:
	if event is InputEventKey:
		return OS.get_keycode_string(event.physical_keycode)
	elif event is InputEventJoypadButton:
		return "Button " + str(event.button_index)
	elif event is InputEventJoypadMotion:
		return "Axis " + str(event.axis)
	else:
		return "---"
