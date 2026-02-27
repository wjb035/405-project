@tool
extends Container

signal room_selected(rom_path: String, core_path: String, core_id: String)

@onready var background_image = $"../../../style/background_image"
@onready var background_image2 = $"../../../style/background_image2"
@onready var FocusTitle = $"../../../FocusTitle"

@export_group("UI Visibility")
@export var emulator_only_elements: Array[Control] = []
@export var room_only_elements: Array[Control] = []

@export_group("Layout")
@export var item_spacing: float = 350.0:
	set(value):
		item_spacing = value
		if not Engine.is_editor_hint():
			_update_positions()

@export var vertical_center: float = 0.5:
	set(value):
		vertical_center = value
		queue_sort()

@export var normal_scale: float = 0.8:
	set(value):
		normal_scale = value
		_update_all_scales()

@export var animation_speed: float = 8.0
@export var position_smooth: float = 12.0
@export var fade_distance: float = 600.0
@export var min_opacity: float = 0.4
@export var start_offset: float = 0.0

var items: Array = []
var focused_index: int = 0
var container_offset: float = 0.0
var target_offset: float = 0.0
var item_target_positions: Dictionary = {}
var navigation_history: Array = []
var is_showing_rooms: bool = false
var current_emulator: Control = null

var is_transitioning: bool = false

var input_enabled: bool = false
const INPUT_DELAY: float = 0.2

var is_changing_focus: bool = false
var can_navigate: bool = true
const NAV_COOLDOWN: float = 0.15

var current_background_tween: Tween = null

var icon_queue: Array = []

func _ready():
	if Engine.is_editor_hint():
		return
	
	if AndroidManager:
		AndroidManager.app_icon_loaded.connect(_on_android_icon_loaded)
	
	await get_tree().process_frame
	await get_tree().process_frame
	
	_collect_items()
	_connect_item_signals()
	_update_focus(0, true)
	_update_title()
	_update_ui_visibility()
	
	GlobalAutoload.context_changed.connect(_on_context_changed)
	resized.connect(_on_resized)
	
	if AndroidManager.is_available:
		if not AndroidManager.app_icon_loaded.is_connected(_on_android_icon_loaded):
			AndroidManager.app_icon_loaded.connect(_on_android_icon_loaded)

	connect_to_library()

func force_layout_update():
	if items.is_empty():
		return
	
	_recalculate_scroll_target()
	container_offset = target_offset
	_calculate_target_positions()
	
	for item in items:
		if item_target_positions.has(item):
			item.position = item_target_positions[item]
	
	_update_positions()

func _on_resized():
	_recalculate_scroll_target()
	container_offset = target_offset
	_calculate_target_positions()
	for item in items:
		if item_target_positions.has(item):
			item.position = item_target_positions[item]

func _on_context_changed(new_context):
	if new_context == GlobalAutoload.Context.LIBRARY:
		input_enabled = false
		get_tree().create_timer(INPUT_DELAY).timeout.connect(func(): input_enabled = true)

func _connect_item_signals():
	for item in items:
		if item.has_signal("item_selected"):
			if not item.item_selected.is_connected(_on_item_selected):
				item.item_selected.connect(_on_item_selected)

func _notification(what):
	if what == NOTIFICATION_SORT_CHILDREN:
		if not is_transitioning:
			_arrange_children()

func _arrange_children():
	if is_transitioning and not Engine.is_editor_hint():
		return
	
	var visible_children = []
	for child in get_children():
		if child is Control and child.visible:
			visible_children.append(child)
			child.pivot_offset = child.size / 2.0
	
	items = visible_children
	
	if items.is_empty():
		return
	
	container_offset = start_offset + _get_first_item_offset()
	target_offset = container_offset
	
	var center_y = size.y * vertical_center
	
	for i in range(items.size()):
		var item = items[i]
		if not is_instance_valid(item):
			continue
		
		var base_x = i * item_spacing
		var final_x = base_x + container_offset - item.size.x / 2.0
		var final_y = center_y - item.size.y / 2.0
		
		item.position = Vector2(final_x, final_y)
		item.scale = Vector2(normal_scale, normal_scale)
		item_target_positions[item] = item.position

func _collect_items():
	items.clear()
	for child in get_children():
		if child is Control and child.visible:
			items.append(child)
			child.pivot_offset = child.size / 2.0
	
	_setup_fixed_positions()

func _setup_fixed_positions():
	if items.is_empty():
		return
	
	_recalculate_scroll_target()
	container_offset = target_offset
	_calculate_target_positions()
	
	for item in items:
		if item_target_positions.has(item):
			item.position = item_target_positions[item]

func _recalculate_scroll_target():
	if items.is_empty():
		return
	
	target_offset = start_offset + _get_first_item_offset() - focused_index * item_spacing

func _get_first_item_offset() -> float:
	if items.size() > 0 and is_instance_valid(items[0]):
		return items[0].size.x * normal_scale / 2.0
	return 0.0

func _calculate_target_positions():
	if items.is_empty():
		return
	
	if is_transitioning:
		return
	
	var center_y = size.y * vertical_center
	
	for i in range(items.size()):
		var item = items[i]
		if not is_instance_valid(item):
			continue
		
		var base_x = i * item_spacing
		var final_x = base_x + container_offset - item.size.x / 2.0
		var final_y = center_y - item.size.y / 2.0
		
		item_target_positions[item] = Vector2(final_x, final_y)

func _update_positions():
	if items.is_empty():
		return
	
	if is_transitioning:
		return
	
	var delta = get_process_delta_time()
	
	for i in range(items.size()):
		var item = items[i]
		if not is_instance_valid(item):
			continue
		
		if not item_target_positions.has(item):
			continue
		
		var target_pos = item_target_positions[item]
		item.position = item.position.lerp(target_pos, position_smooth * delta)
		
		item.scale = Vector2(normal_scale, normal_scale)
		
		var target_opacity = 1.0
		if i > focused_index:
			var distance_from_focused = (i - focused_index) * item_spacing
			if distance_from_focused > fade_distance:
				target_opacity = max(min_opacity, 1.0 - (distance_from_focused - fade_distance) / fade_distance)
		
		item.modulate.a = lerp(item.modulate.a, target_opacity, position_smooth * delta)

func _process(delta):
	if Engine.is_editor_hint():
		return
	
	if is_transitioning:
		return
	
	container_offset = lerp(container_offset, target_offset, animation_speed * delta)
	_calculate_target_positions()
	_update_positions()
	
	if not icon_queue.is_empty():
		var batch = 2
		while batch > 0 and not icon_queue.is_empty():
			var pkg = icon_queue.pop_front()
			AndroidManager.get_app_icon_async(pkg)
			batch -= 1

func _unhandled_input(event: InputEvent):
	if Engine.is_editor_hint():
		return
	
	if not GlobalAutoload.is_context(GlobalAutoload.Context.LIBRARY):
		return
	
	if not input_enabled:
		return
	
	if GlobalAutoload.is_context_fresh():
		return
	
	if event.is_action_pressed("ui_right"):
		_change_focus(1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("ui_left"):
		_change_focus(-1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_SELECT"):
		if SFX:
			SFX.play_select()
		_handle_selection()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_BACK"):
		if is_showing_rooms:
			if SFX:
				SFX.play_back()
			_go_back()
			get_viewport().set_input_as_handled()

func _change_focus(direction: int):
	if is_changing_focus:
		return
	
	if not can_navigate:
		return
	
	var new_index = focused_index + direction
	
	if new_index >= 0 and new_index < items.size():
		if SFX:
			SFX.play_nav()
		
		is_changing_focus = true
		can_navigate = false
		
		_update_focus(new_index)
		
		get_tree().create_timer(NAV_COOLDOWN).timeout.connect(func():
			can_navigate = true
		, CONNECT_ONE_SHOT)

func _update_focus(new_index: int, instant: bool = false):
	focused_index = new_index
	_recalculate_scroll_target()
	
	for i in range(items.size()):
		var item = items[i]
		if item and item.has_method("set_focused"):
			item.set_focused(i == focused_index)
	
	if instant:
		container_offset = target_offset
		_calculate_target_positions()
		for item in items:
			if item_target_positions.has(item):
				item.position = item_target_positions[item]
		is_changing_focus = false
	else:
		_update_background()
		_update_title()
		
		get_tree().create_timer(0.1).timeout.connect(func():
			is_changing_focus = false
		, CONNECT_ONE_SHOT)

func _update_background():
	if Engine.is_editor_hint():
		return
	
	if focused_index >= items.size():
		return
	
	var item = items[focused_index]
	if not is_instance_valid(item):
		return
	
	if "icon_type" in item and item.icon_type != 1:
		return
	
	if not item.has_node("EmuImage/TextureRect"):
		return
	
	var texture_rect = item.get_node("EmuImage/TextureRect")
	var new_texture = texture_rect.texture
	
	if not new_texture:
		return
	
	if background_image and background_image.texture == new_texture:
		return
	
	if not background_image2 or not background_image:
		if background_image:
			background_image.texture = new_texture
			background_image.modulate.a = 1.0
		return
	
	if current_background_tween and current_background_tween.is_valid():
		current_background_tween.kill()
	
	if background_image2.visible and background_image2.texture:
		background_image.texture = background_image2.texture
		background_image.modulate.a = 1.0
		background_image.scale = Vector2.ONE
		background_image.position = Vector2.ZERO
	
	background_image2.texture = new_texture
	background_image2.visible = true
	background_image2.modulate.a = 0.0
	background_image2.scale = Vector2.ONE
	background_image2.position = Vector2.ZERO
	
	var bg_rect = background_image2.get_rect()
	var bg_size = bg_rect.size
	if bg_size == Vector2.ZERO:
		bg_size = Vector2(1920, 1080)
	background_image2.pivot_offset = bg_size / 2.0
	
	var initial_scale = 1.15
	background_image2.scale = Vector2(initial_scale, initial_scale)
	background_image2.position = -bg_size * (initial_scale - 1.0) / 2.0
	
	current_background_tween = create_tween()
	current_background_tween.set_parallel(true)
	current_background_tween.set_ease(Tween.EASE_OUT)
	current_background_tween.set_trans(Tween.TRANS_CUBIC)
	
	current_background_tween.tween_property(background_image2, "modulate:a", 1.0, 0.4)
	current_background_tween.tween_property(background_image2, "scale", Vector2.ONE, 0.6)
	current_background_tween.tween_property(background_image2, "position", Vector2.ZERO, 0.6)
	
	if background_image.texture:
		current_background_tween.tween_property(background_image, "modulate:a", 0.0, 0.25)
	
	current_background_tween.chain().tween_callback(_on_background_transition_done)

func _on_background_transition_done():
	if not background_image or not background_image2:
		return
	
	if background_image2.texture:
		background_image.texture = background_image2.texture
	background_image.modulate.a = 1.0
	background_image.scale = Vector2.ONE
	background_image.position = Vector2.ZERO
	
	background_image2.visible = false
	background_image2.texture = null
	background_image2.modulate.a = 0.0
	background_image2.scale = Vector2.ONE
	background_image2.position = Vector2.ZERO
	
	current_background_tween = null

func _update_title():
	if Engine.is_editor_hint():
		return
	
	if focused_index >= items.size():
		return
	
	if not FocusTitle:
		return
	
	var item = items[focused_index]
	if not is_instance_valid(item):
		return
	
	if "Title" in item:
		FocusTitle.text = item.Title
	else:
		FocusTitle.text = ""

func _update_ui_visibility():
	if Engine.is_editor_hint():
		return
	
	for element in emulator_only_elements:
		if is_instance_valid(element):
			element.visible = not is_showing_rooms
	
	for element in room_only_elements:
		if is_instance_valid(element):
			element.visible = is_showing_rooms

func _update_all_scales():
	if Engine.is_editor_hint():
		for i in range(get_child_count()):
			var child = get_child(i)
			if child is Control:
				child.scale = Vector2(normal_scale, normal_scale)

func _on_item_selected(item):
	if focused_index >= items.size():
		return
	
	if items[focused_index] != item:
		return
	
	_handle_selection()

func _handle_selection():
	if focused_index >= items.size():
		return
	
	var selected_item = items[focused_index]
	
	if "icon_type" not in selected_item:
		return
	
	if selected_item.icon_type == 0:
		_show_rooms_for_emulator(selected_item)
	elif selected_item.icon_type == 1:
		
		# android apps logic
		if selected_item.has_meta("is_android_app") and selected_item.get_meta("is_android_app"):
			var pkg_name = selected_item.get_meta("package_name", "")
			if pkg_name != "":
				if AndroidManager.is_available:
					AndroidManager.launch_app(pkg_name)
			return

		var rom_path = selected_item.get_meta("rom_path", "")
		if rom_path != "":
			if "custom_texture" in selected_item:
				GlobalAutoload.current_game_texture = selected_item.custom_texture
			
			var core_path = ""
			var core_id = ""
			if selected_item.parent_emulator:
				if "libretro_core" in selected_item.parent_emulator:
					core_path = selected_item.parent_emulator.libretro_core
				if selected_item.parent_emulator.has_meta("core_id"):
					core_id = selected_item.parent_emulator.get_meta("core_id")
			
			room_selected.emit(rom_path, core_path, core_id)

func _show_rooms_for_emulator(emulator):
	navigation_history.append({
		"items": items.duplicate(),
		"focused_index": focused_index,
		"emulator": current_emulator
	})
	
	current_emulator = emulator
	is_showing_rooms = true
	
	_hide_current_items()
	_create_room_instances(emulator)

func _create_room_instances(emulator):
	is_transitioning = true
	
	var EMULATOR_ICON_SCENE = load("res://src/components/emulator_icon.tscn")
	if not EMULATOR_ICON_SCENE:
		push_error("LibraryContainer: Could not load emulator_icon.tscn")
		is_transitioning = false
		return
	
	item_target_positions.clear()
	items.clear()
	
	var created_items = []
	var library_script = get_node_or_null("../../..")
	
	var is_android_apps = false
	if emulator.has_meta("core_id") and emulator.get_meta("core_id") == "android_apps":
		is_android_apps = true
	
	var rooms_data = []
	if is_android_apps:
		if AndroidManager.is_available:
			rooms_data = AndroidManager.cached_apps
			if rooms_data.is_empty():
				rooms_data = AndroidManager.get_installed_apps()
		else:
			push_error("LibraryContainer: Android Manager unavailable for apps")
			is_transitioning = false
			return
	else:
		rooms_data = emulator.get_rooms()

	if rooms_data.is_empty():
		push_error("LibraryContainer: No rooms data found for emulator")
		is_transitioning = false
		return
	
	for i in range(rooms_data.size()):
		var room_data = rooms_data[i]
		var room_instance = EMULATOR_ICON_SCENE.instantiate()
		
		room_instance.icon_type = 1
		room_instance.use_debug_rooms = false
		room_instance.parent_emulator = emulator
		
		# initially visible but transparent to allow positioning calculation
		room_instance.visible = true
		room_instance.modulate.a = 0.0
		
		if is_android_apps:
			room_instance.Title = room_data["name"]
			room_instance.set_meta("package_name", room_data["package"])
			room_instance.set_meta("is_android_app", true)
			
			if AndroidManager.cached_icons.has(room_data["package"]):
				room_instance.custom_texture = AndroidManager.cached_icons[room_data["package"]]
			else:
				icon_queue.append(room_data["package"])
		else:
			room_instance.Title = room_data["name"]
			if room_data.has("path"):
				room_instance.set_meta("rom_path", room_data["path"])
				
				if library_script and library_script.has_method("get_rom_metadata"):
					var metadata = library_script.get_rom_metadata(room_data["path"])
					if not metadata.is_empty():
						if metadata.get("title", "") != "":
							room_instance.Title = metadata["title"]
						if metadata.get("icon_path", "") != "":
							var cover_texture = _load_cover_texture(metadata["icon_path"])
							if cover_texture:
								room_instance.custom_texture = cover_texture
		
		add_child(room_instance)
		created_items.append(room_instance)
	
	await get_tree().process_frame
	await get_tree().process_frame
	
	var center_y = size.y * vertical_center
	
	for i in range(created_items.size()):
		var room = created_items[i]
		room.pivot_offset = room.size / 2.0
		room.scale = Vector2(normal_scale, normal_scale)
		
		var base_x = i * item_spacing
		var final_x = base_x + start_offset + _get_first_item_offset() - room.size.x / 2.0
		var final_y = center_y - room.size.y / 2.0
		room.position = Vector2(final_x, final_y)
		item_target_positions[room] = room.position
		
		items.append(room)
		
		if room.has_signal("item_selected"):
			if not room.item_selected.is_connected(_on_item_selected):
				room.item_selected.connect(_on_item_selected)
	
	for room in created_items:
		room.modulate.a = 1.0
	
	_update_focus(0, true)
	_update_background()
	_update_title()
	_update_ui_visibility()
	
	if library_script and library_script.has_method("scan_emulator_for_metadata"):
		library_script.scan_emulator_for_metadata(emulator)
	
	is_transitioning = false

func _hide_current_items():
	for item in items:
		item.visible = false

func _go_back():
	if navigation_history.is_empty():
		return
	
	is_transitioning = true
	
	for item in items:
		if item.icon_type == 1:
			item.queue_free()
	
	await get_tree().process_frame
	await get_tree().process_frame
	
	var previous_state = navigation_history.pop_back()
	items = previous_state["items"]
	focused_index = previous_state["focused_index"]
	current_emulator = previous_state["emulator"]
	
	item_target_positions.clear()
	
	for item in items:
		item.visible = true
		item.pivot_offset = item.size / 2.0
		item.modulate.a = 1.0
	
	_update_focus(focused_index, true)
	
	is_showing_rooms = false
	is_transitioning = false
	
	if background_image:
		background_image.texture = null
	if background_image2:
		background_image2.texture = null
		background_image2.visible = false
	
	_update_title()
	_update_ui_visibility()

func _load_cover_texture(path: String) -> Texture2D:
	if path.is_empty():
		return null
	
	var absolute_path = path
	if path.begins_with("user://"):
		absolute_path = path.replace("user://", OS.get_user_data_dir() + "/")
	
	if not FileAccess.file_exists(absolute_path):
		return null
	
	var image = Image.new()
	var error = image.load(absolute_path)
	if error != OK:
		return null
	
	return ImageTexture.create_from_image(image)

func _on_game_metadata_updated(rom_path: String, data: Dictionary):
	for item in items:
		if not is_instance_valid(item):
			continue
		
		if item.get_meta("rom_path", "") == rom_path:
			if data.get("title", "") != "":
				item.Title = data["title"]
			
			if data.get("icon_path", "") != "":
				var cover_texture = _load_cover_texture(data["icon_path"])
				if cover_texture:
					item.custom_texture = cover_texture
			
			if item == items[focused_index]:
				_update_title()
				_update_background()

func _on_android_icon_loaded(package_name: String, texture: Texture2D):
	if not texture:
		return
		
	for item in items:
		if not is_instance_valid(item):
			continue
			
		if item.has_meta("package_name") and item.get_meta("package_name") == package_name:
			item.custom_texture = texture
			
			# update background if this is the currently focused item
			if item == items[focused_index]:
				_update_background()
			
			break

func connect_to_library():
	var library = get_node_or_null("../../..")
	if library and library.has_signal("game_metadata_updated"):
		if not library.game_metadata_updated.is_connected(_on_game_metadata_updated):
			library.game_metadata_updated.connect(_on_game_metadata_updated)
