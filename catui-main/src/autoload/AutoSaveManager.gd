extends Node

var auto_save_timer: Timer
var current_game_path: String = ""
var is_game_running: bool = false
var save_count: int = 0

var max_saves: int = 6
var save_interval: float = 60.0

func _ready():
	process_mode = Node.PROCESS_MODE_ALWAYS
	
	auto_save_timer = Timer.new()
	auto_save_timer.wait_time = save_interval
	auto_save_timer.one_shot = false
	auto_save_timer.timeout.connect(_on_auto_save_timer)
	add_child(auto_save_timer)
	
	
func start_monitoring(rom_path: String):
	current_game_path = rom_path
	is_game_running = true
	_update_settings()
	save_count = _count_existing_saves()
	
	if save_interval > 0:
		auto_save_timer.start()
		print("autosave: started. interval: ", save_interval, "s. max saves: ", max_saves)
	else:
		auto_save_timer.stop()
		print("autosave: disabled")

func stop_monitoring():
	is_game_running = false
	auto_save_timer.stop()
	current_game_path = ""

func _update_settings():
	var settings = get_node_or_null("/root/main/Menus/settings")
	if settings:
		var interval_idx = settings.settings_cache.get("auto_save_interval", 1)
		
		match int(interval_idx):
			0: save_interval = 0
			1: save_interval = 60.0
			2: save_interval = 300.0
			3: save_interval = 600.0
			4: save_interval = 1800.0
			
		var val = int(settings.settings_cache.get("max_save_states", 6))
		if val < 1:
			val = 6
		max_saves = val
		
		if auto_save_timer:
			auto_save_timer.wait_time = max(save_interval, 1.0)

func _on_auto_save_timer():
	if not is_game_running or current_game_path == "":
		return
		
	var libretro = get_node_or_null("/root/LibretroPlayer")
	if libretro and libretro.has_method("IsRunning") and libretro.IsRunning():
		perform_auto_save()

func perform_auto_save():
	var libretro = get_node_or_null("/root/LibretroPlayer")
	if not libretro: return

	save_count += 1
	var timestamp = Time.get_datetime_string_from_system()
	var clean_name = current_game_path.get_file().get_basename()
	var save_dir = "user://saves/" + clean_name + "/"
	
	if not DirAccess.dir_exists_absolute(save_dir):
		DirAccess.make_dir_recursive_absolute(save_dir)
	
	var filename_base = "auto_" + timestamp.replace(":", "-")
	var state_path = save_dir + filename_base + ".state"
	var img_path = save_dir + filename_base + ".png"
	var json_path = save_dir + filename_base + ".json"
	
	libretro.SaveState(ProjectSettings.globalize_path(state_path))
	
	libretro.CaptureScreenshot(ProjectSettings.globalize_path(img_path))
	
	var meta = {
		"timestamp": timestamp,
		"type": "auto",
		"rom_path": current_game_path
	}
	var file = FileAccess.open(json_path, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(meta, "\t"))
		
	print("autosave: saved ", filename_base)
	
	_cleanup_old_saves(save_dir)

func _count_existing_saves() -> int:
	return 0

func _cleanup_old_saves(save_dir: String):
	var dir = DirAccess.open(save_dir)
	if not dir: return
	
	var saves = []
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if not dir.current_is_dir() and file_name.ends_with(".json") and file_name.begins_with("auto_"):
			saves.append(file_name)
		file_name = dir.get_next()
	
	saves.sort()
	
	while saves.size() > max_saves:
		var old_json = saves.pop_front()
		var base = old_json.replace(".json", "")
		
		DirAccess.remove_absolute(save_dir + old_json)
		DirAccess.remove_absolute(save_dir + base + ".state")
		DirAccess.remove_absolute(save_dir + base + ".png")
		print("autosave: deleted old save ", base)
