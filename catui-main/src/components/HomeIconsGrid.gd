@tool
class_name GridContainerView
extends Container

signal game_selected(rom_path: String)
signal move_mode_ended
signal icon_deleted
signal icon_size_changed(size_name: String)
signal alert_requested(message: String)
signal edit_requested

const HOME_ICON_SCENE = preload("res://src/components/home_icon.tscn")

@export_group("Container Configs")
@export var cell_size: Vector2 = Vector2(100, 100):
	set(value):
		cell_size = value
		queue_sort()

@export var item_margin: int = 0:
	set(value):
		item_margin = value
		queue_sort()

@export_group("Icons Debug Mode")
@export var debug_enabled: bool = false:
	set(value):
		debug_enabled = value
		if Engine.is_editor_hint():
			call_deferred("_regenerate_debug_icons")

@export var debug_icon_count: int = 10:
	set(value):
		debug_icon_count = max(1, value)
		if Engine.is_editor_hint() and debug_enabled:
			call_deferred("_regenerate_debug_icons")

@export var random_order: bool = false:
	set(value):
		if value and Engine.is_editor_hint():
			call_deferred("_regenerate_debug_icons")
		random_order = false

var focused_index: int = 0
var is_moving_mode: bool = false
var moving_item: Control = null
var wiggle_tween: Tween = null
var is_animating: bool = false
var all_icons: Dictionary = {}
var _debug_icons: Array = []

var focus_bubble: Control = null


func _regenerate_debug_icons():
	if not Engine.is_editor_hint():
		return
	
	_clear_debug_icons()
	
	if not debug_enabled:
		queue_sort()
		return
	
	var container_width = size.x if size.x > 0 else (get_parent().size.x if get_parent() else 700.0)
	var container_height = size.y if size.y > 0 else (get_parent().size.y if get_parent() else 300.0)
	var effective_cell_size = cell_size + Vector2(item_margin, item_margin)
	var max_columns = max(1, int(floor(container_width / effective_cell_size.x)))
	var max_rows = max(1, int(floor(container_height / effective_cell_size.y)))
	
	var possible_sizes = [
		Vector2(100, 100),
		Vector2(200, 100),
		Vector2(200, 200)
	]
	
	var occupied_cells = {}
	var icons_created = 0
	
	for _i in range(debug_icon_count):
		var available_sizes = []
		
		for size_index in range(possible_sizes.size()):
			var icon_min_size = possible_sizes[size_index]
			var span_x = min(max(1, int(ceil(icon_min_size.x / cell_size.x))), max_columns)
			var span_y = min(max(1, int(ceil(icon_min_size.y / cell_size.y))), max_rows)
			
			var found_position = _find_free_space(occupied_cells, max_columns, max_rows, span_x, span_y)
			
			if found_position.y + span_y <= max_rows:
				available_sizes.append({
					"size_index": size_index,
					"span": Vector2i(span_x, span_y),
					"position": found_position
				})
		
		if available_sizes.is_empty():
			break
		
		var chosen = available_sizes[randi() % available_sizes.size()]
		
		_mark_cells(occupied_cells, chosen.position.x, chosen.position.y, chosen.span.x, chosen.span.y)
		
		var icon = HOME_ICON_SCENE.instantiate()
		icon.name = "_DbgIcon_%d" % icons_created
		icon.icon_size = chosen.size_index
		add_child(icon)
		_debug_icons.append(icon)
		icons_created += 1
	
	queue_sort()

func _clear_debug_icons():
	for icon in _debug_icons:
		if is_instance_valid(icon):
			icon.queue_free()
	_debug_icons.clear()
	
	for child in get_children():
		if child.name.begins_with("_DbgIcon") or child.name.begins_with("DbgIcon"):
			child.queue_free()

func _enter_tree():
	if Engine.is_editor_hint():
		_clear_debug_icons()

func _ready():
	if not Engine.is_editor_hint():
		_clear_debug_icons()
	
	focus_mode = Control.FOCUS_ALL
	grab_focus()
	
	focus_bubble = get_node_or_null("../focus_bubble")
	
	call_deferred("_update_focus")
	resized.connect(_sort_children)
	call_deferred("_setup_existing_children")


func _setup_existing_children():
	if FileAccess.file_exists(LAYOUT_FILE):
		_load_layout()
		return

	for child in get_children():
		if child is Control:
			add_game_icon(child)

func _notification(what):
	if what == NOTIFICATION_SORT_CHILDREN:
		_sort_children()

func _sort_children():
	var container_width = size.x if size.x > 0 else (get_parent().size.x if get_parent() else 100.0)
	var container_height = size.y if size.y > 0 else (get_parent().size.y if get_parent() else 300.0)
	var effective_cell_size = cell_size + Vector2(item_margin, item_margin)
	var max_columns = max(1, int(floor(container_width / effective_cell_size.x)))
	var max_rows = max(1, int(floor(container_height / effective_cell_size.y)))
	
	var grid = {}
	var max_used_y = 0
	
	for child in get_children():
		if not (child is Control) or not child.visible or child.is_set_as_top_level():
			continue
		
		var child_min_size = child.get_combined_minimum_size()
		var span_x = min(max(1, int(ceil(child_min_size.x / cell_size.x))), max_columns)
		var span_y = min(max(1, int(ceil(child_min_size.y / cell_size.y))), max_rows)
		
		var pos = _find_free_space(grid, max_columns, max_rows, span_x, span_y)
		
		if pos.y + span_y > max_rows:
			child.visible = false
			continue
		
		var child_pos = Vector2(pos.x * effective_cell_size.x + item_margin / 2.0, pos.y * effective_cell_size.y + item_margin / 2.0)
		var child_size = Vector2(span_x * cell_size.x + (span_x - 1) * item_margin, span_y * cell_size.y + (span_y - 1) * item_margin)
		
		fit_child_in_rect(child, Rect2(child_pos, child_size))
		
		_mark_cells(grid, pos.x, pos.y, span_x, span_y)
		max_used_y = max(max_used_y, pos.y + span_y)
	
	custom_minimum_size.y = max_used_y * effective_cell_size.y

func _find_free_space(grid: Dictionary, max_cols: int, max_rows: int, span_x: int, span_y: int) -> Vector2i:
	for y in range(max_rows - span_y + 1):
		for x in range(max_cols - span_x + 1):
			if _can_fit_at(grid, x, y, span_x, span_y):
				return Vector2i(x, y)
	return Vector2i(0, max_rows)

func _can_fit_at(grid: Dictionary, start_x: int, start_y: int, span_x: int, span_y: int) -> bool:
	for dx in range(span_x):
		for dy in range(span_y):
			var key = str(start_x + dx) + "," + str(start_y + dy)
			if grid.has(key):
				return false
	return true

func _mark_cells(grid: Dictionary, start_x: int, start_y: int, span_x: int, span_y: int):
	for dx in range(span_x):
		for dy in range(span_y):
			var key = str(start_x + dx) + "," + str(start_y + dy)
			grid[key] = true

func _input(event: InputEvent):
	if not (GlobalAutoload.is_context(GlobalAutoload.Context.GAMEPLAY) or GlobalAutoload.is_context(GlobalAutoload.Context.DASHBOARD)):
		return
	
	if not visible:
		return
	
	var game_screen_node = get_node_or_null("/root/main/Menus/game_screen")
	if game_screen_node and game_screen_node.visible:
		return
	
	var children = _get_valid_children()
	
	if event.is_action_pressed("UI_MENU"):
		if not children.is_empty():
			edit_requested.emit()
		accept_event()
		return
	
	if children.is_empty():
		return
	
	if is_moving_mode and moving_item:
		_handle_move_mode_input(event)
		return
	
	_handle_navigation_input(event, children)

func _handle_move_mode_input(event: InputEvent):
	if event.is_action_pressed("UI_RIGHT"):
		_move_item_in_direction(1)
		accept_event()
	elif event.is_action_pressed("UI_LEFT"):
		_move_item_in_direction(-1)
		accept_event()
	elif event.is_action_pressed("UI_DOWN"):
		var cols = max(1, int(floor(size.x / cell_size.x)))
		_move_item_in_direction(cols)
		accept_event()
	elif event.is_action_pressed("UI_UP"):
		var cols = max(1, int(floor(size.x / cell_size.x)))
		_move_item_in_direction(-cols)
		accept_event()
	elif event.is_action_pressed("UI_SELECT") or event.is_action_pressed("UI_EDIT"):
		stop_move_mode()
		accept_event()

func _handle_navigation_input(event: InputEvent, _children: Array):
	if event.is_action_pressed("UI_RIGHT"):
		if SFX:
			SFX.play_nav()
		_navigate_geometric(Vector2.RIGHT)
		accept_event()
	elif event.is_action_pressed("UI_LEFT"):
		if SFX:
			SFX.play_nav()
		_navigate_geometric(Vector2.LEFT)
		accept_event()
	elif event.is_action_pressed("UI_DOWN"):
		if SFX:
			SFX.play_nav()
		_navigate_geometric(Vector2.DOWN)
		accept_event()
	elif event.is_action_pressed("UI_UP"):
		if SFX:
			SFX.play_nav()
		_navigate_geometric(Vector2.UP)
		accept_event()
	elif event.is_action_pressed("UI_SELECT"):
		var children = _get_valid_children()
		if focused_index >= 0 and focused_index < children.size():
			var selected = children[focused_index]
			if "game_url" in selected and selected.game_url != "":
				if "custom_texture" in selected:
					GlobalAutoload.current_game_texture = selected.custom_texture
				
				game_selected.emit(selected.game_url)
		accept_event()

func _get_valid_children() -> Array:
	var valid = []
	for child in get_children():
		if child is Control and child.visible and not child.is_set_as_top_level():
			valid.append(child)
	return valid

func _update_focus():
	var children = _get_valid_children()
	
	for i in range(children.size()):
		var child = children[i]
		var is_focused = (i == focused_index)
		
		if child.has_method("set_focused"):
			child.set_focused(is_focused)
		
		var focus_panel = child.get_node_or_null("style/focus")
		if focus_panel:
			focus_panel.visible = is_focused
	
	if Engine.is_editor_hint():
		return
	
	if focus_bubble and focused_index >= 0 and focused_index < children.size():
		var focused_child = children[focused_index]
		var item_name = ""
		
		if "game_name" in focused_child:
			item_name = focused_child.game_name
		elif "file_name" in focused_child:
			item_name = focused_child.file_name
		else:
			item_name = focused_child.name
		
		var global_rect = focused_child.get_global_rect()
		var center_x = global_rect.position.x + global_rect.size.x / 2.0
		var top_y = global_rect.position.y
		
		var bubble_position = Vector2(
			center_x - focus_bubble.size.x / 2.0,
			top_y - focus_bubble.size.y - 10
		)
		
		focus_bubble.show_at_position(bubble_position, item_name)
	elif focus_bubble:
		focus_bubble.hide_bubble()

func add_game_icon(icon: Control):
	if not icon:
		return
	
	var file_name = ""
	if "file_name" in icon:
		file_name = icon.file_name
	elif "game_name" in icon and icon.game_name != "":
		file_name = icon.game_name
	else:
		file_name = icon.name
	
	if all_icons.has(file_name):
		return
	
	if icon.get_parent() != self:
		add_child(icon)
	
	all_icons[file_name] = icon
	icon.visible = true
	queue_sort()
	call_deferred("_update_focus")
	_save_layout()

func start_move_mode(item: Control):
	if not item:
		return
	
	if item.get_parent() != self:
		return
	
	is_moving_mode = true
	moving_item = item
	
	if moving_item.has_method("set_move_mode"):
		moving_item.set_move_mode(true)
	
	_update_focused_index_from_item(item)
	_update_focus()
	_start_wiggle_animation(item)

func stop_move_mode():
	if moving_item:
		if moving_item.has_method("set_move_mode"):
			moving_item.set_move_mode(false)
		_stop_wiggle_animation(moving_item)
	
	is_moving_mode = false
	moving_item = null
	_update_focus()
	move_mode_ended.emit()
	_save_layout()

func _check_layout_fits() -> bool:
	var container_width = size.x if size.x > 0 else (get_parent().size.x if get_parent() else 100.0)
	var container_height = size.y if size.y > 0 else (get_parent().size.y if get_parent() else 300.0)
	var effective_cell_size = cell_size + Vector2(item_margin, item_margin)
	var max_columns = max(1, int(floor(container_width / effective_cell_size.x)))
	var max_rows = max(1, int(floor(container_height / effective_cell_size.y)))
	
	var grid = {}
	
	for child in get_children():
		if not (child is Control) or not child.visible or child.is_set_as_top_level():
			continue
		
		var child_min_size = child.get_combined_minimum_size()
		var span_x = min(max(1, int(ceil(child_min_size.x / cell_size.x))), max_columns)
		var span_y = min(max(1, int(ceil(child_min_size.y / cell_size.y))), max_rows)
		
		var pos = _find_free_space(grid, max_columns, max_rows, span_x, span_y)
		
		if pos.y + span_y > max_rows:
			return false
		
		_mark_cells(grid, pos.x, pos.y, span_x, span_y)
	
	return true

func _check_layout_fits_with_new_size(target_icon: Control, new_size_index: int) -> bool:
	var container_width = size.x if size.x > 0 else (get_parent().size.x if get_parent() else 100.0)
	var container_height = size.y if size.y > 0 else (get_parent().size.y if get_parent() else 300.0)
	var effective_cell_size = cell_size + Vector2(item_margin, item_margin)
	var max_columns = max(1, int(floor(container_width / effective_cell_size.x)))
	var max_rows = max(1, int(floor(container_height / effective_cell_size.y)))
	
	var size_map = {
		0: Vector2(100, 100),
		1: Vector2(200, 100),
		2: Vector2(200, 200)
	}
	
	var grid = {}
	
	for child in get_children():
		if not (child is Control) or not child.visible or child.is_set_as_top_level():
			continue
		
		var child_min_size
		if child == target_icon:
			child_min_size = size_map.get(new_size_index, Vector2(100, 100))
		else:
			child_min_size = child.get_combined_minimum_size()
		
		var span_x = min(max(1, int(ceil(child_min_size.x / cell_size.x))), max_columns)
		var span_y = min(max(1, int(ceil(child_min_size.y / cell_size.y))), max_rows)
		
		var pos = _find_free_space(grid, max_columns, max_rows, span_x, span_y)
		
		if pos.y + span_y > max_rows:
			return false
		
		_mark_cells(grid, pos.x, pos.y, span_x, span_y)
	
	return true

func _move_item_in_direction(offset: int):
	if not moving_item:
		return
	
	if is_animating:
		return
	
	var children = get_children()
	var current_index = moving_item.get_index()
	var target_index = clamp(current_index + offset, 0, children.size() - 1)
	
	if current_index == target_index:
		return
	
	move_child(moving_item, target_index)
	
	if not _check_layout_fits():
		move_child(moving_item, current_index)
		return
	
	queue_sort()
	_update_focused_index_from_item(moving_item)

func _update_focused_index_from_item(item: Control):
	var children = _get_valid_children()
	for i in range(children.size()):
		if children[i] == item:
			focused_index = i
			return

func _navigate_geometric(direction: Vector2):
	var children = _get_valid_children()
	
	if children.is_empty():
		return
	
	if focused_index < 0 or focused_index >= children.size():
		focused_index = 0
		_update_focus()
		return
	
	var current_item = children[focused_index]
	var current_rect = current_item.get_rect()
	var start_point = _get_edge_point(current_rect, direction)
	
	var best_index = -1
	var best_score = INF
	
	for i in range(children.size()):
		if i == focused_index:
			continue
		
		var target_rect = children[i].get_rect()
		var score = _calculate_navigation_score(current_rect, target_rect, direction, start_point)
		
		if score < best_score:
			best_score = score
			best_index = i
	
	if best_index != -1:
		focused_index = best_index
		_update_focus()

func _calculate_navigation_score(current_rect: Rect2, target_rect: Rect2, direction: Vector2, _start_point: Vector2) -> float:
	var target_center = target_rect.get_center()
	var current_center = current_rect.get_center()
	
	if not _is_in_direction(current_center, target_center, direction):
		return INF
	
	var horizontal_distance = abs(target_center.x - current_center.x)
	var vertical_distance = abs(target_center.y - current_center.y)
	
	var overlap = 0.0
	
	if direction == Vector2.RIGHT or direction == Vector2.LEFT:
		var overlap_start = max(current_rect.position.y, target_rect.position.y)
		var overlap_end = min(current_rect.end.y, target_rect.end.y)
		if overlap_end > overlap_start:
			overlap = overlap_end - overlap_start
		
		return horizontal_distance - overlap * 10.0 + vertical_distance * 0.5
	else:
		var overlap_start = max(current_rect.position.x, target_rect.position.x)
		var overlap_end = min(current_rect.end.x, target_rect.end.x)
		if overlap_end > overlap_start:
			overlap = overlap_end - overlap_start
		
		return vertical_distance - overlap * 10.0 + horizontal_distance * 0.5

func _get_edge_point(rect: Rect2, direction: Vector2) -> Vector2:
	var center = rect.get_center()
	
	if direction == Vector2.RIGHT:
		return Vector2(rect.end.x, center.y)
	elif direction == Vector2.LEFT:
		return Vector2(rect.position.x, center.y)
	elif direction == Vector2.DOWN:
		return Vector2(center.x, rect.end.y)
	elif direction == Vector2.UP:
		return Vector2(center.x, rect.position.y)
	
	return center

func _is_in_direction(current_pos: Vector2, target_pos: Vector2, direction: Vector2) -> bool:
	var to_target = target_pos - current_pos
	
	if direction == Vector2.RIGHT:
		return to_target.x > 0
	elif direction == Vector2.LEFT:
		return to_target.x < 0
	elif direction == Vector2.DOWN:
		return to_target.y > 0
	elif direction == Vector2.UP:
		return to_target.y < 0
	
	return false

func _start_wiggle_animation(item: Control):
	if not is_instance_valid(item):
		return
	
	_stop_wiggle_animation(item)
	
	if not item.has_method("_update_pivot"):
		if item.size.x > 0 and item.size.y > 0:
			item.pivot_offset = item.size / 2.0
	
	wiggle_tween = create_tween()
	wiggle_tween.set_loops()
	wiggle_tween.tween_property(item, "rotation", deg_to_rad(2), 0.2).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	wiggle_tween.tween_property(item, "rotation", deg_to_rad(-2), 0.4).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	wiggle_tween.tween_property(item, "rotation", 0.0, 0.2).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

func _stop_wiggle_animation(item: Control):
	if wiggle_tween:
		wiggle_tween.kill()
		wiggle_tween = null
	
	if is_instance_valid(item):
		var reset_tween = create_tween()
		reset_tween.tween_property(item, "rotation", 0.0, 0.2).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

func delete_focused_icon():
	var children = _get_valid_children()
	
	if children.is_empty():
		return
	
	if focused_index < 0 or focused_index >= children.size():
		return
	
	var icon = children[focused_index]
	
	var icon_name = ""
	if "game_name" in icon and icon.game_name != "":
		icon_name = icon.game_name
	else:
		icon_name = icon.name
	
	if all_icons.has(icon_name):
		all_icons.erase(icon_name)
	
	icon.queue_free()
	
	queue_sort()
	
	var new_children = _get_valid_children()
	if focused_index >= new_children.size():
		focused_index = max(0, new_children.size() - 1)
	
	_update_focus()
	icon_deleted.emit()
	_save_layout()

func change_focused_icon_size(direction: int):
	var children = _get_valid_children()
	
	if children.is_empty():
		return
	
	if focused_index < 0 or focused_index >= children.size():
		return
	
	var icon = children[focused_index]
	
	if not icon:
		return
	
	if not "icon_size" in icon:
		return
	
	var HomeIcon = preload("res://src/components/home_icon.gd")
	var current_size = icon.icon_size
	var new_size = clamp(current_size + direction, HomeIcon.IconSize.SMALL, HomeIcon.IconSize.LARGE)
	
	if new_size == current_size:
		return
	
	if not _check_layout_fits_with_new_size(icon, new_size):
		alert_requested.emit("Not Enough Space")
		return
	
	icon.icon_size = new_size
	
	if is_inside_tree():
		await get_tree().process_frame
	queue_sort()
	_update_focus()
	
	var size_names = ["small", "medium", "large"]
	icon_size_changed.emit(size_names[new_size] if new_size < 3 else "?")
	_save_layout()

func request_start_move_mode():
	var children = _get_valid_children()
	
	if children.is_empty():
		return
	
	if focused_index < 0 or focused_index >= children.size():
		return
	
	var icon = children[focused_index]
	if icon:
		start_move_mode(icon)

const LAYOUT_FILE = "user://home_layout.json"

func _save_layout():
	var data = []
	for child in get_children():
		if not child is Control or child.is_set_as_top_level() or not child.visible:
			continue
		
		# ignore debug icons, we don't want to save those
		if child.name.begins_with("_DbgIcon") or child.name.begins_with("DbgIcon"):
			continue
			
		var item_data = {
			"name": child.name,
			"size": child.icon_size if "icon_size" in child else 0
		}
		
		if "game_name" in child: item_data["game_name"] = child.game_name
		if "game_url" in child: item_data["game_url"] = child.game_url
		if "custom_image_path" in child: item_data["custom_image_path"] = child.custom_image_path
		if "emulator_id" in child: item_data["emulator_id"] = child.emulator_id
		
		data.append(item_data)
	
	var file = FileAccess.open(LAYOUT_FILE, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(data, "\t"))

func _load_layout():
	if not FileAccess.file_exists(LAYOUT_FILE):
		return
		
	var file = FileAccess.open(LAYOUT_FILE, FileAccess.READ)
	if not file: return
	
	var json = JSON.new()
	if json.parse(file.get_as_text()) != OK:
		return
		
	var data = json.data
	if not data is Array:
		return
		
	for child in get_children():
		if not child.name.begins_with("_DbgIcon") and not child.is_set_as_top_level():
			child.queue_free()
			
	var pending_images: Array = []
	
	for item in data:
		var icon = HOME_ICON_SCENE.instantiate()
		
		if "name" in item: icon.name = item["name"]
		if "size" in item: icon.icon_size = int(item["size"])
		
		if "game_name" in item: icon.game_name = item["game_name"]
		if "game_url" in item: icon.game_url = item["game_url"]
		if "emulator_id" in item: icon.emulator_id = item["emulator_id"]
		
		if "custom_image_path" in item:
			icon.custom_image_path = item["custom_image_path"]
			pending_images.append({"icon": icon, "path": item["custom_image_path"]})
		
		add_child(icon)
		if "game_name" in item: all_icons[item["game_name"]] = icon
		else: all_icons[icon.name] = icon
		
	
	if is_inside_tree():
		await get_tree().process_frame
	queue_sort()
	call_deferred("_update_focus")
	
	if not pending_images.is_empty():
		call_deferred("_load_images_async", pending_images)

func _load_images_async(pending: Array):
	for item in pending:
		var icon = item["icon"]
		var path = item["path"]
		
		if not is_instance_valid(icon):
			continue
			
		if not FileAccess.file_exists(path):
			continue
		
		var image = Image.load_from_file(path)
		if image:
			var texture = ImageTexture.create_from_image(image)
			icon.custom_texture = texture
		
		await get_tree().process_frame
