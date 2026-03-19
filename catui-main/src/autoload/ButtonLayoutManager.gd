extends Node

enum ControllerLayout {
	XBOX,
	PLAYSTATION,
}

signal layout_changed(new_layout: ControllerLayout)

var current_layout: ControllerLayout = ControllerLayout.XBOX:
	set(value):
		if current_layout != value:
			current_layout = value
			layout_changed.emit(current_layout)
			print("layout_manager: layout changed to ", _get_layout_name(value))

func _ready():
	_load_saved_layout()
	print("layout_manager: initialized with layout ", _get_layout_name(current_layout))

func _load_saved_layout():
	const CONFIG_FILE = "user://settings.json"
	if not FileAccess.file_exists(CONFIG_FILE):
		return
	
	var file = FileAccess.open(CONFIG_FILE, FileAccess.READ)
	if not file:
		return
	
	var json = JSON.new()
	if json.parse(file.get_as_text()) == OK:
		var data = json.data
		if data.has("general") and data["general"].has("controller_layout"):
			var saved_layout = data["general"]["controller_layout"]
			if typeof(saved_layout) == TYPE_INT or typeof(saved_layout) == TYPE_FLOAT:
				current_layout = int(saved_layout) as ControllerLayout
				print("layout_manager: loaded layout from config: ", _get_layout_name(int(saved_layout) as ControllerLayout))


func set_layout(new_layout: ControllerLayout):
	current_layout = new_layout
	_save_layout()

func _save_layout():
	const CONFIG_FILE = "user://settings.json"
	var data = {}
	
	if FileAccess.file_exists(CONFIG_FILE):
		var file = FileAccess.open(CONFIG_FILE, FileAccess.READ)
		if file:
			var json = JSON.new()
			if json.parse(file.get_as_text()) == OK:
				data = json.data
			file.close()
	
	if not data.has("general"):
		data["general"] = {}
	data["general"]["controller_layout"] = int(current_layout)
	
	var file = FileAccess.open(CONFIG_FILE, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(data, "\t"))
		file.close()

func get_layout() -> ControllerLayout:
	return current_layout

func _get_layout_name(layout: ControllerLayout) -> String:
	match layout:
		ControllerLayout.XBOX:
			return "XBOX"
		ControllerLayout.PLAYSTATION:
			return "PLAYSTATION"
		_:
			return "UNKNOWN"
