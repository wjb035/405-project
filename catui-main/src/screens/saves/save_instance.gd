extends Control

@export var viewport: SubViewportContainer

@onready var focus_panel = $focus

var save_data: Dictionary = {}
var save_path: String = ""

func _ready():
	var library = get_node_or_null("/root/main/Menus/library")
	if library and library.has_signal("game_metadata_updated"):
		library.game_metadata_updated.connect(_on_metadata_updated)

func _on_metadata_updated(rom_path, _data):
	if save_data.has("rom_path") and save_data["rom_path"] == rom_path:
		_update_cover()

func setup(data: Dictionary, file_path: String):
	save_data = data
	save_path = file_path
	
	if data.has("game_name"):
		name = data["game_name"]
	
	_update_cover()

func _update_cover():
	var image_path = ""
	
	if save_data.has("cover_path") and not save_data["cover_path"].is_empty():
		image_path = save_data["cover_path"]
	elif save_data.has("rom_path"):
		var library_node = get_node_or_null("/root/main/Menus/library")
		if library_node and library_node.has_method("get_rom_metadata"):
			var metadata = library_node.get_rom_metadata(save_data["rom_path"])
			if metadata and metadata.has("cover_path"):
				image_path = metadata["cover_path"]
			else:
				library_node.lookup_rom_metadata(save_data["rom_path"])
	
	if image_path.is_empty():
		return
	
	var image = Image.load_from_file(image_path)
	if image:
		var texture = ImageTexture.create_from_image(image)
		_set_3d_cover(texture)

func _set_3d_cover(texture: Texture2D):
	if not viewport:
		return
		
	var sub_viewport = viewport.get_child(0)
	if not sub_viewport:
		return
		
	var save_icon = sub_viewport.get_node_or_null("save_icon")
	if save_icon and save_icon.game_cover:
		var mat = StandardMaterial3D.new()
		mat.albedo_texture = texture
		save_icon.game_cover.material_override = mat

func set_focused(focused: bool):
	if focus_panel:
		focus_panel.visible = focused
		
	if viewport:
		var sub_viewport = viewport.get_child(0)
		if sub_viewport:
			var save_icon = sub_viewport.get_node_or_null("save_icon")
			if save_icon and save_icon.has_method("set_focused"):
				save_icon.set_focused(focused)

func set_animation_state(state_name: String):
	if viewport:
		var sub_viewport = viewport.get_child(0)
		if sub_viewport:
			var save_icon = sub_viewport.get_node_or_null("save_icon")
			if save_icon:
				match state_name:
					"LOOP": save_icon.set_state(0)
					"HOVER": save_icon.set_state(1)
					"OPEN": save_icon.set_state(2)
					"DELETE": save_icon.set_state(3)
