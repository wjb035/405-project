extends MenuBase

signal rooms_loaded(rooms: Array)
signal rooms_failed(error: String)
signal game_metadata_updated(rom_path: String, data: Dictionary)

@export var rooms_options: Control
@export var game_load: Control

@onready var retro_achievements = $retro_achievement
@onready var library_container = $MarginContainer/VBoxContainer/LibraryContainer

var metadata_cache: Dictionary = {}


var options_menu_items: Array[Control] = []
var options_menu_focus_index: int = 0
var is_options_menu_active: bool = false

func _ready():
	_setup_connections()
	_setup_options_menu()
	_load_saved_cores()

func _setup_connections():
	if retro_achievements:
		retro_achievements.game_data_loaded.connect(_on_retro_game_data_loaded)
		retro_achievements.cover_downloaded.connect(_on_retro_cover_downloaded)
		retro_achievements.queue_progress.connect(_on_retro_queue_progress)
		retro_achievements.queue_finished.connect(_on_retro_queue_finished)
	
	visibility_changed.connect(_on_visibility_changed)
	
	# keep library in sync
	if EmulatorConfig:
		EmulatorConfig.core_removed.connect(remove_emulator_by_core)
		EmulatorConfig.core_updated.connect(_on_core_updated)
		EmulatorConfig.core_added.connect(_on_core_added)

func get_menu_id() -> String:
	return "library"

func get_menu_context() -> GlobalAutoload.Context:
	return GlobalAutoload.Context.LIBRARY

func _load_saved_cores():
	# wait a frame so the UI doesn't lock up immediately
	
	var cores = EmulatorConfig.get_all_cores()
	for core_id in cores.keys():
		var core_data = cores[core_id]
		var core_path = core_data.get("core_path", "")
		var rooms_folder = core_data.get("rooms_folder", "")
		
		if core_path != "":
			var should_add = true
			if OS.get_name() != "Android" and not FileAccess.file_exists(core_path):
				should_add = false
			
			if should_add:
				add_emulator_from_core(core_id, core_path, rooms_folder)

	if OS.get_name() == "Android":
		add_emulator_from_core("android_apps", "Android Apps", "")


func _on_visibility_changed():
	if not visible:
		if is_options_menu_active:
			is_options_menu_active = false
			if rooms_options:
				rooms_options.visible = false
	else:
		if library_container:
			await get_tree().process_frame
			if visible:
				library_container.force_layout_update()

func _setup_options_menu():
	if not rooms_options:
		return
	
	var vbox = rooms_options.get_node_or_null("Control/MarginContainer/VBoxContainer")
	if not vbox:
		return
	
	options_menu_items.clear()
	
	for child in vbox.get_children():
		if child is Control:
			options_menu_items.append(child)
	
	_update_options_menu_focus()

func _unhandled_input(event: InputEvent):
	var current_context = GlobalAutoload.current_context
	
	if current_context != GlobalAutoload.Context.LIBRARY and current_context != GlobalAutoload.Context.OPTIONS_MENU:
		return
	
	if event.is_action_pressed("UI_MENU"):
		if is_options_menu_active:
			hide_options_menu()
		else:
			show_options_menu()
		get_viewport().set_input_as_handled()
		return
	
	if not is_options_menu_active:
		if event.is_action_pressed("UI_BACK"):
			get_viewport().set_input_as_handled()
		return
	
	if event.is_action_pressed("UI_UP"):
		_navigate_options_menu(-1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_DOWN"):
		_navigate_options_menu(1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_SELECT"):
		if SFX:
			SFX.play_select()
		_select_options_menu_item()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_BACK"):
		if SFX:
			SFX.play_back()
		hide_options_menu()
		get_viewport().set_input_as_handled()

func _navigate_options_menu(direction: int):
	if options_menu_items.is_empty():
		return
	
	var new_index = options_menu_focus_index + direction
	new_index = clampi(new_index, 0, options_menu_items.size() - 1)
	
	if new_index != options_menu_focus_index:
		if SFX:
			SFX.play_nav()
		options_menu_focus_index = new_index
		_update_options_menu_focus()

func _update_options_menu_focus():
	for i in range(options_menu_items.size()):
		var item = options_menu_items[i]
		if not item:
			continue
		
		var focus_panel = item.get_node_or_null("style/focus")
		if focus_panel:
			focus_panel.visible = (i == options_menu_focus_index)

func _select_options_menu_item():
	if options_menu_focus_index >= options_menu_items.size():
		return
	
	var item = options_menu_items[options_menu_focus_index]
	if not item:
		return
	
	var item_name = item.name.to_lower()
	
	match item_name:
		"add":
			_add_to_home()
		"edit":
			pass

func _add_to_home():
	if not library_container:
		return
	
	var focused_item = _get_focused_room()
	if not focused_item:
		return
	
	var main_node = get_node_or_null("/root/main")
	if not main_node:
		return
	
	var home_node = main_node.get("home")
	if not home_node:
		return
	
	var home_icons_grid = home_node.get("home_icons_grid")
	if not home_icons_grid:
		return
	
	var HOME_ICON_SCENE = load("res://src/components/home_icon.tscn")
	if not HOME_ICON_SCENE:
		return
	
	var new_icon = HOME_ICON_SCENE.instantiate()
	
	if "Title" in focused_item:
		new_icon.game_name = focused_item.Title
	
	if focused_item.has_meta("rom_path"):
		new_icon.game_url = focused_item.get_meta("rom_path")
	
	if "custom_texture" in focused_item and focused_item.custom_texture:
		new_icon.custom_texture = focused_item.custom_texture
	
	# if we found metadata (like a nicer cover or icon) for this rom, let's use it
	if new_icon.game_url != "" and metadata_cache.has(new_icon.game_url):
		var data = metadata_cache[new_icon.game_url]
		if "icon_path" in data:
			new_icon.custom_image_path = data["icon_path"]
		elif "cover_path" in data:
			new_icon.custom_image_path = data["cover_path"]
	
	new_icon.icon_size = 0
	
	home_icons_grid.add_game_icon(new_icon)
	
	hide_options_menu()

func _get_focused_room() -> Control:
	if not library_container:
		return null
	
	if not library_container.is_showing_rooms:
		return null
	
	var items = library_container.items
	var focused_index = library_container.focused_index
	
	if focused_index >= 0 and focused_index < items.size():
		return items[focused_index]
	
	return null

func show_options_menu():
	if not rooms_options:
		return
		
	if not library_container or not library_container.is_showing_rooms:
		return
	
	is_options_menu_active = true
	rooms_options.visible = true
	options_menu_focus_index = 0
	_update_options_menu_focus()
	
	GlobalAutoload.set_context(GlobalAutoload.Context.OPTIONS_MENU)

func hide_options_menu():
	if not rooms_options:
		return
	
	is_options_menu_active = false
	rooms_options.visible = false
	
	GlobalAutoload.set_context(GlobalAutoload.Context.LIBRARY)

func scan_folder(folder_path: String, valid_extensions: PackedStringArray = []) -> Array:
	var rooms = []
	
	if folder_path.is_empty():
		rooms_failed.emit("Folder path is empty")
		return rooms
	
	if not DirAccess.dir_exists_absolute(folder_path):
		rooms_failed.emit("Folder does not exist: " + folder_path)
		return rooms
	
	var dir = DirAccess.open(folder_path)
	if not dir:
		rooms_failed.emit("Could not open folder: " + folder_path)
		return rooms
	
	dir.list_dir_begin()
	var file_name = dir.get_next()
	
	while file_name != "":
		if not dir.current_is_dir():
			var extension = file_name.get_extension().to_lower()
			
			if valid_extensions.is_empty() or extension in valid_extensions:
				rooms.append({
					"name": file_name.get_basename(),
					"file_name": file_name,
					"path": folder_path.path_join(file_name),
					"extension": extension,
					"size": _get_file_size(folder_path.path_join(file_name))
				})
		
		file_name = dir.get_next()
	
	dir.list_dir_end()
	
	rooms.sort_custom(_sort_by_name)
	
	rooms_loaded.emit(rooms)
	return rooms

func scan_emulator_for_metadata(emulator_node: Control):
	if not retro_achievements:
		return
	
	if not retro_achievements.is_configured():
		return
	
	retro_achievements.scan_emulator_rooms(emulator_node)

func scan_all_emulators_for_metadata():
	if not retro_achievements:
		return
	
	if not library_container:
		return
	
	for child in library_container.get_children():
		if child is Control and "icon_type" in child:
			if child.icon_type == 0:
				scan_emulator_for_metadata(child)

func lookup_rom_metadata(rom_path: String, console_hint: String = ""):
	if not retro_achievements:
		return
	
	if retro_achievements.has_cached_data(rom_path):
		var cached_data = retro_achievements.get_cached_data(rom_path)
		game_metadata_updated.emit(rom_path, cached_data)
		return
	
	if not retro_achievements.is_configured():
		return
	
	var game_data = await retro_achievements.identify_rom_by_hash(rom_path)
	
	if game_data.get("found", false):
		metadata_cache[rom_path] = game_data
		game_metadata_updated.emit(rom_path, game_data)
	else:
		var empty_data = {
			"title": rom_path.get_file().get_basename(),
			"found": false
		}
		metadata_cache[rom_path] = empty_data
		game_metadata_updated.emit(rom_path, empty_data)

func get_rom_metadata(rom_path: String) -> Dictionary:
	if retro_achievements and retro_achievements.has_cached_data(rom_path):
		return retro_achievements.get_cached_data(rom_path)
	return {}

func _on_retro_game_data_loaded(rom_path: String, data: Dictionary):
	metadata_cache[rom_path] = data
	game_metadata_updated.emit(rom_path, data)

func _on_retro_cover_downloaded(rom_path: String, local_path: String):
	if metadata_cache.has(rom_path):
		if "cover" in local_path:
			metadata_cache[rom_path]["cover_path"] = local_path
		elif "icon" in local_path:
			metadata_cache[rom_path]["icon_path"] = local_path
		game_metadata_updated.emit(rom_path, metadata_cache[rom_path])

func _on_retro_queue_progress(_current: int, _total: int):
	pass

func _on_retro_queue_finished():
	pass

func _get_file_size(path: String) -> int:
	var file = FileAccess.open(path, FileAccess.READ)
	if file:
		var file_size = file.get_length()
		file.close()
		return file_size
	return 0

func _sort_by_name(a: Dictionary, b: Dictionary) -> bool:
	return a["name"].naturalnocasecmp_to(b["name"]) < 0

func get_folder_summary(folder_path: String, valid_extensions: PackedStringArray = []) -> Dictionary:
	var rooms = scan_folder(folder_path, valid_extensions)
	var total_size = 0
	
	for room in rooms:
		total_size += room["size"]
	
	return {
		"count": rooms.size(),
		"total_size": total_size,
		"path": folder_path
	}

func format_size(bytes: int) -> String:
	if bytes < 1024:
		return str(bytes) + " B"
	elif bytes < 1024 * 1024:
		return str(snapped(bytes / 1024.0, 0.1)) + " KB"
	elif bytes < 1024 * 1024 * 1024:
		return str(snapped(bytes / (1024.0 * 1024.0), 0.1)) + " MB"
	else:
		return str(snapped(bytes / (1024.0 * 1024.0 * 1024.0), 0.1)) + " GB"

func get_api_stats() -> Dictionary:
	if retro_achievements:
		return retro_achievements.get_stats()
	return {}

func clear_metadata_cache():
	metadata_cache.clear()
	if retro_achievements:
		retro_achievements.clear_cache()

func add_emulator_from_core(core_id: String, core_path: String, rooms_folder: String = ""):
	DebugCapture.add_log("add_emu: core_id=" + core_id)
	DebugCapture.add_log("add_emu: core_path=" + core_path)
	DebugCapture.add_log("add_emu: rooms_folder=" + rooms_folder)
	
	if not library_container:
		return
	
	var EMULATOR_ICON_SCENE = load("res://src/components/emulator_icon.tscn")
	if not EMULATOR_ICON_SCENE:
		push_error("Library: Could not load emulator_icon.tscn")
		return
	
	var emulator_instance = EMULATOR_ICON_SCENE.instantiate()
	
	emulator_instance.icon_type = 0
	emulator_instance.Title = core_id.capitalize()
	emulator_instance.libretro_core = core_path
	emulator_instance.rooms_folder = rooms_folder
	emulator_instance.use_debug_rooms = rooms_folder.is_empty()
	emulator_instance.set_meta("core_id", core_id)
	
	var extensions = EmulatorConfig.get_extensions_for_core(core_path)
	DebugCapture.add_log("add_emu: extensions=" + str(extensions))
	if not extensions.is_empty():
		emulator_instance.roms_extensions = extensions
	
	library_container.add_child(emulator_instance)
	
	await get_tree().process_frame
	
	emulator_instance.pivot_offset = emulator_instance.size / 2.0
	
	if library_container.has_method("force_layout_update"):
		library_container.force_layout_update()

func remove_emulator_by_core(core_id: String):
	if not library_container:
		return
	
	for child in library_container.get_children():
		if child.has_meta("core_id") and child.get_meta("core_id") == core_id:
			child.queue_free()
			break
	
	await get_tree().process_frame
	
	if library_container.has_method("force_layout_update"):
		library_container.force_layout_update()

func update_emulator_folder(core_id: String, rooms_folder: String):
	DebugCapture.add_log("update_folder: core_id=" + core_id)
	DebugCapture.add_log("update_folder: rooms_folder=" + rooms_folder)
	
	if not library_container:
		DebugCapture.add_log("update_folder: library_container is null!")
		return
	
	var found = false
	for child in library_container.get_children():
		if child.has_meta("core_id") and child.get_meta("core_id") == core_id:
			found = true
			child.rooms_folder = rooms_folder
			child.use_debug_rooms = rooms_folder.is_empty()
			
			if "libretro_core" in child and child.libretro_core != "":
				var extensions = EmulatorConfig.get_extensions_for_core(child.libretro_core)
				DebugCapture.add_log("update_folder: extensions=" + str(extensions))
				if not extensions.is_empty():
					child.roms_extensions = extensions
			break
	
	if not found:
		DebugCapture.add_log("update_folder: emulator not found for core_id=" + core_id)

func _on_core_updated(core_id: String):
	if EmulatorConfig:
		var folder = EmulatorConfig.get_core_rooms_folder(core_id)
		update_emulator_folder(core_id, folder)

func _on_core_added(core_id: String):
	if EmulatorConfig:
		var path = EmulatorConfig.get_libretro_core(core_id)
		var folder = EmulatorConfig.get_core_rooms_folder(core_id)
		add_emulator_from_core(core_id, path, folder)
