extends Control

@export_group("Menus")
@export var home_menu: Control
@export var home: Control
@export var settings: Control
@export var game_screen: Control
@export var library: Control
@export var saves: Control
@export var boot: Control
@onready var library_container = $Menus/library/MarginContainer/VBoxContainer/LibraryContainer

var is_game_running: bool = false
var is_game_paused: bool = false
var previous_context = GlobalAutoload.Context.GAMEPLAY
var current_emulator: String = ""

const RESUME_DELAY = 0.2
const ANIM_DURATION = 0.3
const ANIM_FADE = 0.2
const SCALE_OVERLAY = Vector2(1.05, 1.05)
const SCALE_SCREEN = Vector2(0.95, 0.95)

var is_resuming: bool = false
var menu_tweens: Dictionary = {}
var home_menu_cooldown: bool = false
const HOME_MENU_COOLDOWN_TIME = 0.5

var registered_menus: Array[Control] = []

@onready var menus_node = $Menus
var dynamic_menus: Dictionary = {}
var active_dynamic_menu: String = ""

const AutoSaveManagerScript = preload("res://src/autoload/AutoSaveManager.gd")
var auto_save_manager: Node

func _ready():
	
	SFX.play_nav()
	
	auto_save_manager = AutoSaveManagerScript.new()
	auto_save_manager.name = "AutoSaveManager"
	add_child(auto_save_manager)

	# Temporary: Clear old hashes to apply header stripping fixes
	# RomHasher.clear_cache()
	# print("MAIN: ROM hash cache cleared for update")

	_hide_all_menus()
	_register_dynamic_menus()
	
	if boot:
		boot.visible = true
		boot.modulate.a = 1.0
		boot.boot_completed.connect(_on_boot_completed)
	else:
		_show_home_screen()
	
	if home_menu:
		home_menu.item_selected.connect(_on_home_menu_item_selected)
	
	if library_container:
		library_container.room_selected.connect(_on_room_selected)
	
	if home:
		home.game_selected.connect(_on_game_selected)
	
	GlobalAutoload.context_changed.connect(_on_context_changed)
	
	EmulatorConfig.emulator_config_changed.connect(_on_emulator_config_changed)

func _on_boot_completed():
	if boot:
		var tween = create_tween()
		tween.tween_property(boot, "modulate:a", 0.0, 0.3)
		tween.tween_callback(func():
			boot.visible = false
			_show_home_screen()
		)

func _register_dynamic_menus():
	if not menus_node:
		return

	var ignored_nodes = [home_menu, game_screen, home]
	
	registered_menus.clear()
	dynamic_menus.clear()
	
	for child in menus_node.get_children():
		if child in ignored_nodes or not child is MenuBase:
			continue
		
		_register_menu(child)

func _register_menu(menu: MenuBase):
	var menu_id = menu.get_menu_id()
	
	dynamic_menus[menu_id] = menu
	registered_menus.append(menu)
	
	if menu.has_signal("back_requested"):
		menu.back_requested.connect(_hide_dynamic_menu.bind(menu_id))
	
	if menu.has_signal("game_selected"):
		menu.game_selected.connect(_on_game_selected)
	
	if menu.has_method("on_menu_registered"):
		menu.on_menu_registered()

func _show_dynamic_menu(menu_key: String):
	if not dynamic_menus.has(menu_key):
		return
		
	var menu = dynamic_menus[menu_key]
	if menu.visible:
		return
	
	_transition_to_menu(menu)
	active_dynamic_menu = menu_key
	
	var context = menu.get_menu_context()
	GlobalAutoload.set_context(context)
	
	if menu.has_method("on_menu_shown"):
		menu.on_menu_shown()

func _hide_dynamic_menu(menu_key: String = ""):
	if menu_key == "":
		menu_key = active_dynamic_menu
		
	if dynamic_menus.has(menu_key):
		_animate_hide(dynamic_menus[menu_key])
	
	_show_home_menu()
	GlobalAutoload.set_context(GlobalAutoload.Context.HOME_MENU)

func _on_emulator_config_changed(emulator_id: String, _config: Dictionary):
	# only update if its the emulator running right now
	if emulator_id == current_emulator and is_game_running:
		_apply_emulator_settings(emulator_id)

func _transition_to_menu(target_menu: Control):
	if home_menu and home_menu.visible:
		_animate_hide(home_menu)

	for menu in registered_menus:
		if not menu: continue
		
		if menu == target_menu:
			if not menu.visible:
				menu.visible = true
				_animate_show(menu)
			menu.move_to_front()
		else:
			if menu.visible:
				_animate_hide(menu)

func _unhandled_input(event: InputEvent):
	if GlobalAutoload.is_context_fresh():
		return
	
	var current_context = GlobalAutoload.current_context
	
	if event.is_action_pressed("UI_HOME"):
		if home_menu_cooldown:
			return
		
		get_viewport().set_input_as_handled()
		if SFX:
			SFX.play_home()
		
		home_menu_cooldown = true
		get_tree().create_timer(HOME_MENU_COOLDOWN_TIME).timeout.connect(func(): home_menu_cooldown = false)
		
		if current_context == GlobalAutoload.Context.HOME_MENU:
			_hide_home_menu()
		else:
			_show_home_menu()
		return
	
	if current_context == GlobalAutoload.Context.HOME_MENU:
		if event.is_action_pressed("UI_BACK"):
			get_viewport().set_input_as_handled()
			if SFX:
				SFX.play_back()
			_hide_home_menu()

func _on_context_changed(new_context):
	match new_context:
		GlobalAutoload.Context.HOME_MENU:
			pass
		GlobalAutoload.Context.SETTINGS:
			pass
		GlobalAutoload.Context.LIBRARY:
			pass
		GlobalAutoload.Context.GAMEPLAY:
			pass

func _on_home_menu_item_selected(item_name: String):
	match item_name:
		"continue":
			_hide_home_menu(true)
		"home":
			
			if is_game_running:
				LibretroPlayer.SetPaused(true)
				if game_screen:
					_animate_hide(game_screen)
					if game_screen.has_method("set_process_input"):
						game_screen.set_process_input(false)
			
			_show_home_screen()
			_hide_home_menu()

		_:
			if dynamic_menus.has(item_name):
				_show_dynamic_menu(item_name)

func _show_home_screen():
	_hide_all_menus()
	
	if home:
		home.visible = true
		_animate_show(home)
	
	previous_context = GlobalAutoload.Context.DASHBOARD
	GlobalAutoload.set_context(GlobalAutoload.Context.DASHBOARD)

func _show_home_menu():
	if current_emulator != "" and not is_game_running:
		is_game_running = true

	if home_menu:
		if is_game_running:
			home_menu.set_continue_visible(true)
			is_game_paused = true
			LibretroPlayer.SetPaused(true)
		else:
			home_menu.set_continue_visible(false)
		
		home_menu.visible = true
		home_menu.z_index = 100
		home_menu.move_to_front()
		
		if home_menu.has_method("on_open"):
			home_menu.on_open()
			
		_animate_show(home_menu)
	
	if game_screen and game_screen.has_method("set_process_input"):
		game_screen.set_process_input(false)
	
	previous_context = GlobalAutoload.current_context
	GlobalAutoload.set_context(GlobalAutoload.Context.HOME_MENU)

func _hide_home_menu(force_gameplay: bool = false):
	if home_menu:
		_animate_hide(home_menu)
	
	if is_game_running and (force_gameplay or previous_context == GlobalAutoload.Context.GAMEPLAY):
		_resume_game()
	else:
		if previous_context != GlobalAutoload.Context.GAMEPLAY:
			GlobalAutoload.set_context(previous_context)
		
		pass



func _hide_all_menus():
	if home_menu:
		home_menu.visible = false
	if home:
		home.visible = false
		
	for menu in registered_menus:
		if menu:
			menu.visible = false

func _animate_show(menu: Control):
	if not menu:
		return
	
	if menu_tweens.has(menu) and menu_tweens[menu] and menu_tweens[menu].is_valid():
		menu_tweens[menu].kill()
	
	menu.move_to_front()
	menu.modulate.a = 0.0
	menu.scale = SCALE_OVERLAY
	menu.pivot_offset = menu.size / 2.0
	
	var tween = create_tween()
	menu_tweens[menu] = tween
	tween.set_parallel(true)
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.tween_property(menu, "modulate:a", 1.0, ANIM_FADE)
	tween.tween_property(menu, "scale", Vector2.ONE, ANIM_DURATION)

func _animate_hide(menu: Control):
	if not menu:
		return
	
	if "is_active" in menu:
		menu.is_active = false
	
	if menu_tweens.has(menu) and menu_tweens[menu] and menu_tweens[menu].is_valid():
		menu_tweens[menu].kill()
	
	menu.pivot_offset = menu.size / 2.0
	
	var tween = create_tween()
	menu_tweens[menu] = tween
	tween.set_parallel(true)
	tween.set_ease(Tween.EASE_IN)
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.tween_property(menu, "modulate:a", 0.0, ANIM_FADE)
	tween.tween_property(menu, "scale", SCALE_OVERLAY, ANIM_DURATION)
	tween.chain().tween_callback(func(): 
		menu.visible = false
		menu.z_index = 0
	)

func _on_game_selected(rom_path: String):
	_start_game(rom_path, "", "")

func _on_room_selected(rom_path: String, core_path: String, core_id: String):
	_hide_all_menus()
	_start_game(rom_path, core_path, core_id)
	GlobalAutoload.set_context(GlobalAutoload.Context.GAMEPLAY)

func _start_game(rom_path: String, core_path: String = "", core_id: String = ""):
	if SFX:
		SFX.play_select()
	
	if game_screen:
		game_screen.modulate.a = 0.0
		game_screen.visible = true
	
	if core_id != "":
		current_emulator = core_id
	else:
		# attempt to auto-detect the core id for this rom
		var found_core_id = EmulatorConfig.get_core_id_for_rom(rom_path)
		if found_core_id != "":
			current_emulator = found_core_id
		else:
			current_emulator = EmulatorConfig.get_system_id_from_extension(rom_path)
	
	GlobalAutoload.current_system_name = current_emulator
	GlobalAutoload.current_rom_path = rom_path
		
	_apply_emulator_settings(current_emulator)
	
	if core_path != "":
		LibretroPlayer.LoadGameWithCore(rom_path, core_path, core_id)
	else:
		LibretroPlayer.LoadGame(rom_path, current_emulator)
	
	if game_screen:
		_animate_show(game_screen)
	
	is_game_running = true
	is_game_paused = false
	
	if auto_save_manager and auto_save_manager.has_method("start_monitoring"):
		auto_save_manager.start_monitoring(rom_path)

func _resume_game():
	if not is_game_running:
		return
	
	for menu in registered_menus:
		if menu and menu.visible:
			_animate_hide(menu)
	
	if game_screen:
		game_screen.visible = true
		game_screen.modulate.a = 1.0
		game_screen.move_to_front()
	
	is_resuming = true
	is_game_paused = false
	LibretroPlayer.SetPaused(false)
	
	await get_tree().create_timer(RESUME_DELAY).timeout
	
	if game_screen and game_screen.has_method("set_process_input"):
		game_screen.set_process_input(true)
		
	is_resuming = false
	GlobalAutoload.set_context(GlobalAutoload.Context.GAMEPLAY)

func _apply_emulator_settings(emulator_id: String):
	if not game_screen:
		return
	
	var stretch_val = EmulatorConfig.get_emulator_setting(emulator_id, "video", "stretch_mode", "Fill")
	var stretch: int = 2 # default (VideoSize.FILL)
	
	if typeof(stretch_val) == TYPE_STRING:
		match stretch_val:
			"Small": stretch = 0 # VideoSize.SMALL
			"Large": stretch = 1 # VideoSize.LARGE
			"Fill": stretch = 2 # VideoSize.FILL
			"Proportional": stretch = 3 # VideoSize.PROPORTIONAL
			_: stretch = 2
	elif typeof(stretch_val) == TYPE_INT:
		stretch = stretch_val
		
	var tex_filter_val = EmulatorConfig.get_emulator_setting(emulator_id, "video", "texture_filter", "Inherit")
	var tex_filter: int = 0 # VideoTextureFilter.INHERIT
	
	if typeof(tex_filter_val) == TYPE_STRING:
		match tex_filter_val:
			"Inherit": tex_filter = 0
			"Nearest": tex_filter = 1 # fallback
			"Linear": tex_filter = 2  # fallback smooth
			"Nearest Mipmap": tex_filter = 1 # VideoTextureFilter.NEAREST_MIPMAP
			"Linear Mipmap": tex_filter = 2 # VideoTextureFilter.LINEAR_MIPMAP
			_: tex_filter = 0
	elif typeof(tex_filter_val) == TYPE_INT:
		tex_filter = tex_filter_val
	
	game_screen.size_mode = stretch
	game_screen.video_texture_filter = tex_filter
	game_screen.apply_video_size(stretch)
	game_screen.apply_texture_filter(tex_filter)

func _close_game():
	if auto_save_manager and auto_save_manager.has_method("stop_monitoring"):
		auto_save_manager.stop_monitoring()

	LibretroPlayer.StopGame()
	
	if game_screen:
		_animate_hide(game_screen)
	
	is_game_running = false
	is_game_paused = false
	current_emulator = ""
