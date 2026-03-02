@tool
extends Control

enum IconType {
	EMULATOR,
	ROOM
}

enum EmulatorType {
	NONE,
	GBA,
	SNES,
	PS1
}

signal item_selected(item)

@export var icon_type: IconType = IconType.EMULATOR

@export_group("Emulator")
@export var emulator_type: EmulatorType = EmulatorType.NONE

@onready var EmuImageBorder = $style/border
@onready var focus_panel = $style/focus

@export_group("Debug")
@export var use_debug_rooms: bool = true
@export var debug_room_count: int = 5

@export_group("Info")
@export var Title: String = ""
@export var Total: int = 0

@export_group("Rooms")
@export var rooms_folder: String = ""
@export var roms_extensions: PackedStringArray = []

@export var libretro_core: String = "":
	set(value):
		libretro_core = value
		_load_image()

@export var custom_texture: Texture2D:
	set(value):
		custom_texture = value
		_apply_texture()

@onready var texture_rect = $EmuImage/TextureRect

var debug_rooms: Array = []
var parent_emulator: Control = null:
	set(value):
		parent_emulator = value
		if icon_type == IconType.ROOM:
			_update_ui_visibility()

func _ready():
	if icon_type == IconType.EMULATOR and use_debug_rooms:
		_setup_debug_rooms()
	
	call_deferred("_load_image")
	_update_ui_visibility()
	set_focused(false)

func _load_image():
	if not is_node_ready():
		return
	
	if custom_texture:
		_apply_texture()
		return
	
	if libretro_core.is_empty():
		return
	
	var image_path = _get_image_from_core(libretro_core)
	
	if not ResourceLoader.exists(image_path):
		push_error("emulator_icon: Image not found at path: " + image_path)
		return
	
	var texture = load(image_path)
	if texture is Texture2D:
		texture_rect.texture = texture

func _apply_texture():
	if not is_node_ready():
		return
	
	if not texture_rect:
		push_error("emulator_icon: TextureRect not found!")
		return
	
	if custom_texture:
		texture_rect.texture = custom_texture
		texture_rect.queue_redraw()
	else:
		DebugCapture.add_log("icon: custom texture is null for " + Title)

func _update_ui_visibility():
	if not is_node_ready():
		return
	
	if EmuImageBorder:
		EmuImageBorder.visible = true

func _get_image_from_core(_core_path: String) -> String:
	return "res://assets/images/catui/logoAPP.png"

func set_core(path: String):
	libretro_core = path
	_load_image()

func set_texture(texture: Texture2D):
	custom_texture = texture

func get_texture() -> Texture2D:
	if texture_rect:
		return texture_rect.texture
	return null

func set_focused(focused: bool):
	if focus_panel:
		focus_panel.visible = focused

func _setup_debug_rooms():
	if icon_type != IconType.EMULATOR:
		return
	
	if not use_debug_rooms:
		return
	
	debug_rooms.clear()
	
	var room_count = debug_room_count if debug_room_count > 0 else randi_range(3, 8)
	
	for i in range(room_count):
		debug_rooms.append({
			"name": "Debug Room " + str(i + 1),
			"type": IconType.ROOM,
			"parent": self
		})
	
	Total = debug_rooms.size()

func get_rooms() -> Array:
	DebugCapture.add_log("getting rooms from: " + rooms_folder)
	
	if rooms_folder.is_empty():
		if use_debug_rooms and debug_rooms.is_empty():
			_setup_debug_rooms()
		return debug_rooms
	
	return _scan_rooms_folder()

func _scan_rooms_folder() -> Array:
	var result = []
	
	DebugCapture.add_log("scan: folder: " + rooms_folder)
	DebugCapture.add_log("scan: extensions: " + str(roms_extensions))
	
	if rooms_folder.is_empty():
		DebugCapture.add_log("scan: rooms_folder is empty!")
		return result
	
	if not DirAccess.dir_exists_absolute(rooms_folder):
		DebugCapture.add_log("scan: folder not exist: " + rooms_folder)
		return result
	
	var dir = DirAccess.open(rooms_folder)
	if not dir:
		DebugCapture.add_log("scan failed: could not open " + rooms_folder)
		return result
	
	# DebugCapture.add_log("dir opened ok, scanning...")
	
	dir.list_dir_begin()
	var file_name = dir.get_next()
	
	DebugCapture.add_log("scan: first file_name: '" + file_name + "'")
	
	var files_found = 0
	var files_matched = 0
	var all_items = []
	
	while file_name != "":
		all_items.append(file_name + ("|DIR" if dir.current_is_dir() else "|FILE"))
		if not dir.current_is_dir():
			files_found += 1
			var extension = file_name.get_extension().to_lower()
			
			var should_add = roms_extensions.is_empty() or extension in roms_extensions
			
			if should_add:
				files_matched += 1
				result.append({
					"name": file_name.get_basename(),
					"path": rooms_folder.path_join(file_name),
					"type": IconType.ROOM,
					"parent": self
				})
		
		file_name = dir.get_next()
	
	dir.list_dir_end()
	
	# DebugCapture.add_log("scan complete. found " + str(files_matched) + " files")
	
	Total = result.size()
	return result

func _input(event: InputEvent):
	if Engine.is_editor_hint():
		return
	
	if not GlobalAutoload.is_context(GlobalAutoload.Context.LIBRARY):
		return
	
	if GlobalAutoload.is_context_fresh():
		return
	
	if event.is_action_pressed("UI_SELECT"):
		_on_selected()

func _on_selected():
	item_selected.emit(self)
