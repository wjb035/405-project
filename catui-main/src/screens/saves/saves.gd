extends MenuBase

@export var saves_container: FlowContainer
@export var saves_scrollcontainer: ScrollContainer

@export var save_inspect: MenuBase

const SAVE_DIR = "user://saves/"
const SAVE_INSTANCE_SCENE = preload("res://src/screens/saves/save_instance.tscn")



func get_menu_id() -> String:
	return "saves"

func get_menu_context() -> GlobalAutoload.Context:
	return GlobalAutoload.Context.SAVES

func on_open():
	_refresh_saves_list()
	
	if save_inspect and save_inspect.visible:
		save_inspect.close()
	
	if save_inspect and not save_inspect.is_connected("action_selected", _on_inspect_action):
		save_inspect.connect("action_selected", _on_inspect_action)
		
	if saves_container:
		saves_container.input_enabled = true

func on_close():
	if saves_container:
		saves_container.input_enabled = false
	
	if save_inspect and save_inspect.visible:
		save_inspect.close()

func handle_back():
	pass

func on_submenu_closed():
	if saves_container:
		saves_container.input_enabled = true
		saves_container.grab_focus()

func on_input(_event):
	pass

func _ready():
	super._ready()
	
	if not save_inspect:
		save_inspect = get_node_or_null("save_inspect")
		if not save_inspect:
			save_inspect = get_node_or_null("/root/main/Menus/save_inspect")
	
	visibility_changed.connect(func(): 
		if visible and not is_active: 
			open()
		elif not visible and is_active:
			close()
	)
	
	if saves_container:
		saves_container.save_selected.connect(_on_save_selected_from_list)
		if saves_container.has_signal("selection_changed"):
			saves_container.selection_changed.connect(_on_selection_changed)

func _refresh_saves_list():
	if not saves_container: return
		
	for child in saves_container.get_children():
		child.queue_free()
		
	if not DirAccess.dir_exists_absolute(SAVE_DIR):
		DirAccess.make_dir_recursive_absolute(SAVE_DIR)
		return
		
	var dir = DirAccess.open(SAVE_DIR)
	if not dir: return

	dir.list_dir_begin()
	var file_name = dir.get_next()
	var game_folders = []

	while file_name != "":
		if dir.current_is_dir() and not file_name.begins_with("."):
			game_folders.append(file_name)
		file_name = dir.get_next()
	
	game_folders.sort()
	
	for game_name in game_folders:
		_load_game_item(game_name)
		
	if saves_container.has_method("update_selection"):
		saves_container.focused_index = 0
		await get_tree().process_frame
		saves_container.update_selection()
		
		if saves_container.get_child_count() > 0:
			var child = saves_container.get_child(0)
			if child.has_method("set_focused"):
				child.set_focused(true)

func _load_game_item(game_name: String):
	var folder_path = SAVE_DIR.path_join(game_name)
	var latest_save_data = _get_latest_save_in_folder(folder_path)
	
	if latest_save_data.is_empty():
		return 
		
	var instance = SAVE_INSTANCE_SCENE.instantiate()
	saves_container.add_child(instance)
	
	instance.setup(latest_save_data, folder_path)

func _get_latest_save_in_folder(folder_path: String) -> Dictionary:
	var dir = DirAccess.open(folder_path)
	if not dir: return {}
	
	dir.list_dir_begin()
	var file = dir.get_next()

	
	var files = []
	while file != "":
		if not dir.current_is_dir() and file.ends_with(".json"):
			files.append(file)
		file = dir.get_next()
		
	if files.is_empty(): return {}
	files.sort()
	
	var latest_json = files[-1]
	
	var f = FileAccess.open(folder_path.path_join(latest_json), FileAccess.READ)
	if f:
		var json = JSON.new()
		if json.parse(f.get_as_text()) == OK:
			return json.data
	return {}

func _on_save_selected_from_list(save_data):
	if save_inspect:
		var path = ""
		if saves_container:
			var child = saves_container.get_child(saves_container.focused_index)
			if "save_path" in child: path = child.save_path
		
		save_inspect.setup_context(save_data, path)
		
		if saves_container: saves_container.input_enabled = false
		navigate_to(save_inspect)
		
	else:
		push_error("save inspector is missing, can't check the save.")

func _on_inspect_action(action: String, _save_data: Dictionary):
	match action:
		"load_auto", "load_slot_1", "load_slot_2":
			pass
		"delete_file":
			if save_inspect:
				await get_tree().create_timer(1.0).timeout
				
				var path = save_inspect.current_save_path
				if path != "" and FileAccess.file_exists(path):
					DirAccess.remove_absolute(path)
				
				save_inspect.close()
				_refresh_saves_list()

func _on_selection_changed(index):
	if not saves_scrollcontainer or not saves_container: return
	
	if index >= 0 and index < saves_container.get_child_count():
		await get_tree().process_frame
		
		if saves_container and index < saves_container.get_child_count():
			var child = saves_container.get_child(index)
			
			var child_rect = child.get_global_rect()
			var scroll_rect = saves_scrollcontainer.get_global_rect()
			var padding = 20
			
			if child_rect.position.y < scroll_rect.position.y:
				var diff = scroll_rect.position.y - child_rect.position.y
				saves_scrollcontainer.scroll_vertical -= (diff + padding)
				
			elif child_rect.end.y > scroll_rect.end.y:
				var diff = child_rect.end.y - scroll_rect.end.y
				saves_scrollcontainer.scroll_vertical += (diff + padding)
