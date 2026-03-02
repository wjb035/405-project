@tool
extends MenuBase

const SETTINGS_OPTION = preload("res://src/components/settings_option.tscn")
const SETTINGS_BUTTONS = preload("res://src/components/settings_button.tscn")
const SettingsData = preload("res://src/screens/settings/SettingsData.gd")

var submenu_buttons: Array = []
var option_instances: Array = []
var current_focus_index: int = 0

const INPUT_DELAY = 0.2

var is_waiting_for_input: bool = false
var pending_action_name: String = ""
var pending_button: Control = null

var game_screen_node: Control
var focus_bubble_node: Control
var home_menu_node: Control

const CONFIG_FILE = "user://settings.json"
var settings_cache: Dictionary = {}
var is_loading_config: bool = false

var settings_persistence: Node
var settings_renderer: Node

var menu_stack: Array = []
var current_menu_id: String = ""
var current_submenu_focus: int = 0

var core_file_dialog: FileDialog = null
var folder_dialog: FileDialog = null
var pending_core_id: String = ""
var pending_core_path: String = ""

@export_group("Layout")
@export var spacing: int = 20:
	set(value):
		spacing = value

@export_group("Navigation")
@export var columns_for_navigation: int = 4

@export_group("Containers")
@export var options_container: HFlowContainer
@export var submenu_container: VBoxContainer
@export var sub_menu_scroll: ScrollContainer
@export var menu_tittle_label: Label

@export_group("Menus")
@export var keybind_menu: Control

func get_menu_id() -> String:
	return "settings"

func get_menu_context() -> GlobalAutoload.Context:
	return GlobalAutoload.Context.SETTINGS

func on_menu_registered():
	initialize_settings()

func initialize_settings():
	if not game_screen_node:
		game_screen_node = get_node_or_null("/root/main/Menus/game_screen")
	
	if not home_menu_node:
		home_menu_node = get_node_or_null("/root/main/Menus/home_menu")
		
	# clean up old containers so it doesnt break
	if options_container:
		for child in options_container.get_children():
			child.queue_free()
		options_container.visible = false # hide the old menu
		
	if submenu_container:
		submenu_container.visible = true
		
	# Start with main menu
	menu_stack.clear()
	_push_menu("main")

func _ready():
	# android needs vsync off just trust me on this one
	if OS.get_name() == "Android":
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		print("settings: android detected, forcing vsync off for performance")
		
	super._ready()
	
	settings_persistence = preload("res://src/screens/settings/SettingsPersistence.gd").new()
	settings_renderer = preload("res://src/screens/settings/SettingsRenderer.gd").new()
	add_child(settings_persistence)
	add_child(settings_renderer)
	
	if not Engine.is_editor_hint():
		pass

func on_open():
	if Engine.is_editor_hint(): return
	
	# always start fresh at main menu
	menu_stack.clear()
	_push_menu("main")
	
	is_active = false
	get_tree().create_timer(INPUT_DELAY).timeout.connect(func(): is_active = true)


func _push_menu(menu_id: String):
	menu_stack.append(menu_id)
	_show_menu(menu_id)

func _pop_menu():
	if menu_stack.size() > 1:
		menu_stack.pop_back()
		var previous_menu = menu_stack.back()
		_show_menu(previous_menu)

func _show_menu(menu_id: String):
	current_menu_id = menu_id
	
	if menu_id == "main":
		_show_main_menu()
	else:
		_show_submenu(menu_id)

func _show_main_menu():
	if options_container:
		options_container.visible = true
		# wipe it clean to rebuild
		for child in options_container.get_children():
			child.queue_free()
		option_instances.clear()
		
		var items = SettingsData.get_main_menu_items()
		for item in items:
			var option = SETTINGS_OPTION.instantiate()
			options_container.add_child(option)
			option_instances.append(option)
			
			option.update_title(item.get("label", ""), true)
			# center pivot so the zoom looks cute
			option.pivot_offset = option.size / 2
			# icons later maybe?
			
			option.set_meta("submenu_id", item.get("submenu", ""))
			option.set_meta("disabled", item.get("disabled", false))
			
	if submenu_container:
		submenu_container.visible = false
		
	if menu_tittle_label:
		menu_tittle_label.text = "Settings" #tittle btw
		
	current_focus_index = 0
	call_deferred("_update_main_focus")

func _show_submenu(menu_id: String):
	if options_container:
		options_container.visible = false
	if submenu_container:
		submenu_container.visible = true
		
	for child in submenu_container.get_children():
		child.queue_free()
	
	var menu_data = SettingsData.get_menu_data(menu_id)
		
	if menu_tittle_label:
		menu_tittle_label.text = menu_data.get("label", "Settings")
		
	var buttons = settings_renderer.create_buttons(menu_data, settings_persistence)
	submenu_buttons = []
	
	for button in buttons:
		submenu_container.add_child(button)
		
		button.button_type = button.button_type
		button.label_text = button.label_text
		if button.get("checkbox_value") != null: button.checkbox_value = button.checkbox_value
		if button.get("slider_value") != null: button.slider_value = button.slider_value
		if button.get("selector_index") != null: button.selector_index = button.selector_index
		
		submenu_buttons.append(button)
		
		if button.button_type == SettingsData.BUTTON_TYPES.SUBMENU:
			button.submenu_selected.connect(_on_submenu_selected)
		elif button.button_type == SettingsData.BUTTON_TYPES.BUTTON:
			button.button_pressed.connect(_on_action_button_pressed.bind(button))
			
	# reset focus and scroll
	if sub_menu_scroll:
		sub_menu_scroll.scroll_vertical = 0
		
	current_submenu_focus = 0
	
	# find first clickable thing (skip separators or it gets stuck)
	for i in range(submenu_buttons.size()):
		if submenu_buttons[i].button_type != SettingsData.BUTTON_TYPES.SEPARATOR:
			current_submenu_focus = i
			break
			
	call_deferred("_update_focus")

func _update_main_focus():
	for i in range(option_instances.size()):
		var option = option_instances[i]
		var is_focused = (i == current_focus_index)
		
		# use component api
		option.set_active(is_focused)
		
		# if disabled, make it look like a ghost
		if option.get_meta("disabled", false):
			option.modulate.a = 0.3
			
		# lil zoom animation just/vibes
		var target_scale = Vector2(1.1, 1.1) if is_focused else Vector2(1.0, 1.0)
		var tween = create_tween()
		tween.tween_property(option, "scale", target_scale, 0.1).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
		
		# Move focus bubble if it exists
		if is_focused:
			move_focus_visual(option)

func _on_submenu_selected(submenu_id: String):
	if SFX: SFX.play_select()
	_push_menu(submenu_id)

func _on_action_button_pressed(button):
	if SFX: SFX.play_select()
	var action = button.get_meta("action") if button.has_meta("action") else ""
	
	match action:
		"import_core":
			_open_import_core_dialog()
		"delete_core":
			var core_id = button.get_meta("emulator_id")
			if core_id:
				EmulatorConfig.remove_libretro_core(core_id)
				_show_submenu("emulation_main") # refresh list bye bye
		"change_rom_folder":
			var core_id = button.get_meta("emulator_id")
			if core_id:
				_open_rom_folder_dialog(core_id)

func _open_import_core_dialog():
	# lets find that core
	if OS.get_name() == "Windows":
		DisplayServer.file_dialog_show("Select Libretro Core", "", "", false, DisplayServer.FILE_DIALOG_MODE_OPEN_FILE, ["*.dll"], _on_core_selected_native)
	else:
		var file_dialog = FileDialog.new()
		file_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
		file_dialog.access = FileDialog.ACCESS_FILESYSTEM
		file_dialog.filters = ["*.so, *.dll, *.dylib ; Libretro Cores"]
		file_dialog.use_native_dialog = false # using godot style for android
		file_dialog.file_selected.connect(_on_core_selected_godot)
		add_child(file_dialog)
		file_dialog.popup_centered(Vector2(800, 600))

func _on_core_selected_native(status, selected_paths, _selected_filter_index):
	if status and not selected_paths.is_empty():
		_register_core(selected_paths[0])

func _on_core_selected_godot(path):
	_register_core(path)

func _register_core(path):
	var filename = path.get_file().get_basename()
	# clean up the name to get a nice ID
	var core_id = filename.replace("_libretro", "").replace("libretro_", "")
	core_id = core_id.replace("_android", "").replace("_windows", "").replace("_linux", "")
	
	if EmulatorConfig:
		EmulatorConfig.set_libretro_core(core_id, path)
		# refresh the menu to show the new toy
		_show_submenu("emulation_main")

func _open_rom_folder_dialog(core_id: String):
	# time to find where the games are hiding
	if OS.get_name() == "Windows":
		DisplayServer.file_dialog_show("Select ROMs Folder", "", "", false, DisplayServer.FILE_DIALOG_MODE_OPEN_DIR, [], _on_rom_folder_selected_native.bind(core_id))
	else:
		var file_dialog = FileDialog.new()
		file_dialog.file_mode = FileDialog.FILE_MODE_OPEN_DIR
		file_dialog.access = FileDialog.ACCESS_FILESYSTEM
		file_dialog.use_native_dialog = false # android prefers this
		file_dialog.dir_selected.connect(_on_rom_folder_selected_godot.bind(core_id))
		add_child(file_dialog)
		file_dialog.popup_centered(Vector2(800, 600))

func _on_rom_folder_selected_native(status, selected_paths, _index, core_id):
	if status and not selected_paths.is_empty():
		_update_core_folder(core_id, selected_paths[0])

func _on_rom_folder_selected_godot(path, core_id):
	_update_core_folder(core_id, path)

func _update_core_folder(core_id: String, path: String):
	if EmulatorConfig:
		EmulatorConfig.set_core_rooms_folder(core_id, path)
		# show the new path so user is happy
		_show_submenu("manage_core_" + core_id)

func on_input(event: InputEvent):
	if is_waiting_for_input:
		_handle_input_capture(event)
		return
	
	if Engine.is_editor_hint(): return
	
	_handle_menu_input(event)

func _start_keybind_listening(button):
	var action = button.action_name
	_start_input_capture(button, action)

func _handle_menu_input(event: InputEvent):
	if current_menu_id == "main":
		_handle_main_menu_input(event)
	else:
		_handle_submenu_input(event)

func _handle_main_menu_input(event: InputEvent):
	if option_instances.is_empty():
		return
		
	var columns = columns_for_navigation
	var rows = ceil(option_instances.size() / float(columns))
	var current_row = floor(current_focus_index / float(columns))
	var current_col = current_focus_index % columns
	
	if event.is_action_pressed("UI_RIGHT"):
		if current_col < columns - 1 and current_focus_index + 1 < option_instances.size():
			current_focus_index += 1
			_play_nav_sound()
	elif event.is_action_pressed("UI_LEFT"):
		if current_col > 0:
			current_focus_index -= 1
			_play_nav_sound()
	elif event.is_action_pressed("UI_DOWN"):
		if current_row < rows - 1:
			var next_index = current_focus_index + columns
			if next_index < option_instances.size():
				current_focus_index = next_index
				_play_nav_sound()
	elif event.is_action_pressed("UI_UP"):
		if current_row > 0:
			current_focus_index -= columns
			_play_nav_sound()
	elif event.is_action_pressed("UI_SELECT"):
		var option = option_instances[current_focus_index]
		
		# nope, cant click disabled stuff
		if option.get_meta("disabled", false):
			return
			
		var submenu_id = option.get_meta("submenu_id")
		if submenu_id:
			_on_submenu_selected(submenu_id)
			
	_update_main_focus()

func _handle_submenu_input(event: InputEvent):
	if submenu_buttons.is_empty():
		return
		
	if event.is_action_pressed("UI_DOWN"):
		_navigate_menu(1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_UP"):
		_navigate_menu(-1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_LEFT"):
		_adjust_menu_value(-1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_RIGHT"):
		_adjust_menu_value(1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_SELECT"):
		var button = submenu_buttons[current_submenu_focus]
		
		# nope, cant touch this
		if button.get_meta("disabled", false):
			return
			
		if SFX: SFX.play_select()
		_activate_menu_item()
		get_viewport().set_input_as_handled()

func _play_nav_sound():
	if SFX: SFX.play_nav()
	get_viewport().set_input_as_handled()

func _navigate_menu(direction: int):
	if submenu_buttons.is_empty():
		return
		
	if SFX: SFX.play_nav()
	
	var next_index = current_submenu_focus
	var attempts = submenu_buttons.size() # safety break so we dont freeze the game lol
	
	while attempts > 0:
		next_index = wrap(next_index + direction, 0, submenu_buttons.size())
		attempts -= 1
		
		# ignore separator
		if submenu_buttons[next_index].button_type != SettingsData.BUTTON_TYPES.SEPARATOR:
			current_submenu_focus = next_index
			break
	
	_update_focus()
	_scroll_to_focus()

func _adjust_menu_value(direction: int):
	var button = submenu_buttons[current_submenu_focus]
	
	match button.button_type:
		SettingsData.BUTTON_TYPES.SLIDER:
			var step = button.slider_step
			# var range_val = button.slider_max - button.slider_min
			var current = button.get_value()
			button.set_value(current + (step * direction))
			
		SettingsData.BUTTON_TYPES.SELECTOR:
			if direction > 0:
				button.selector_next()
			else:
				button.selector_prev()
				
		SettingsData.BUTTON_TYPES.CHECKBOX:
			# Maybe allow left/right to toggle? standard is usually SELECT.
			pass

func _activate_menu_item():
	var button = submenu_buttons[current_submenu_focus]
	
	match button.button_type:
		SettingsData.BUTTON_TYPES.CHECKBOX:
			button.checkbox_value = !button.checkbox_value
			
		SettingsData.BUTTON_TYPES.BUTTON:
			button._on_button_pressed() # Trigger signal
			
		SettingsData.BUTTON_TYPES.SUBMENU:
			button.trigger_submenu()
			
		SettingsData.BUTTON_TYPES.KEYBIND:
			_start_keybind_listening(button)
			
		SettingsData.BUTTON_TYPES.SELECTOR:
			if button.has_meta("action"):
				_on_action_button_pressed(button)

func _update_focus():
	if submenu_buttons.size() > current_submenu_focus:
		var button = submenu_buttons[current_submenu_focus]
		move_focus_visual(button)

func move_focus_visual(target_node: Control):
	# If there is a shared focus bubble, move it.
	if focus_bubble_node:
		focus_bubble_node.move_to(target_node)
	
	# Also ensure button knows it's focused if it has internal state
	for btn in submenu_buttons:
		var focus = btn.get_node_or_null("Focus") 
		if focus: focus.visible = (btn == target_node)
		
		var focus_style = btn.get_node_or_null("style/focus")
		if focus_style: focus_style.visible = (btn == target_node)

func _scroll_to_focus():
	if submenu_buttons.is_empty(): return
	var button = submenu_buttons[current_submenu_focus]
	
	if sub_menu_scroll:
		# Basic manual scrolling logic
		var button_pos = button.position.y
		var scroll_pos = sub_menu_scroll.scroll_vertical
		var container_height = sub_menu_scroll.size.y
		
		if button_pos < scroll_pos:
			sub_menu_scroll.scroll_vertical = button_pos
		elif button_pos + button.size.y > scroll_pos + container_height:
			sub_menu_scroll.scroll_vertical = button_pos + button.size.y - container_height

func handle_back():
	if is_waiting_for_input:
		return

	# Only allow going back if we are deeper in the menu
	if menu_stack.size() > 1:
		if SFX: SFX.play_back()
		_pop_menu()
	# Else: Blocked. No escape via BACK button. Use HOME to leave.

func _unhandled_input(event):
	if is_waiting_for_input:
		if event.is_pressed() and not event.is_echo():
			_handle_input_capture(event)
			get_viewport().set_input_as_handled()
		return
		
	super._unhandled_input(event)

# --- Keybind Capture Logic ---

func _start_input_capture(button: Control, action_name: String):
	is_waiting_for_input = true
	pending_action_name = action_name
	pending_button = button
	
	if button.has_method("set_waiting"):
		button.set_waiting(true)
		
	if keybind_menu:
		keybind_menu.visible = true
		if keybind_menu.has_method("open"):
			keybind_menu.open()

func _cancel_input_capture():
	is_waiting_for_input = false
	if pending_button and pending_button.has_method("set_waiting"):
		pending_button.set_waiting(false)
	pending_button = null
	pending_action_name = ""
	
	if keybind_menu:
		if keybind_menu.has_method("close"):
			keybind_menu.close()
		else:
			keybind_menu.visible = false

func _save_config():
	if settings_persistence:
		settings_persistence.save_global_settings()
		settings_persistence.save_keybinds()

func _handle_input_capture(event: InputEvent):
	if not is_waiting_for_input: return
	if event is InputEventMouseMotion: return
	
	# Ignore initial press if it was the one that triggered the capture
	# (Usually handled by is_echo check or separate state, but MenuBase unhandled_input handles order)
	
	if event.is_pressed():
		get_viewport().set_input_as_handled()
		
		InputMap.action_erase_events(pending_action_name)
		InputMap.action_add_event(pending_action_name, event)
		
		_save_config()
		_update_keybind_label(pending_button, pending_action_name)
		
		if SFX: SFX.play_select()
		
		_cancel_input_capture()

func _update_keybind_label(button: Control, action_name: String):
	if not button: return
	var events = InputMap.action_get_events(action_name)
	if not events.is_empty():
		var event = events[0]
		var text = ""
		if event is InputEventKey:
			text = OS.get_keycode_string(event.physical_keycode)
		elif event is InputEventJoypadButton:
			text = "Joy Btn " + str(event.button_index)
		elif event is InputEventJoypadMotion:
			text = "Joy Axis " + str(event.axis)
			
		if button.has_method("set_value_text"):
			button.set_value_text(text)
