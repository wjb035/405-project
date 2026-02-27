extends Control

@onready var label = $HBoxContainer/Label
@onready var icon_rect = $HBoxContainer/Control/Panel/TextureRect
@onready var focus_panel = $style/focus

var action_name: String = ""

func setup(text: String, icon_path: String, action: String):
	if label:
		label.text = text
	
	if icon_rect and not icon_path.is_empty():
		if FileAccess.file_exists(icon_path) or icon_path.begins_with("user://"):
			var image = Image.load_from_file(icon_path)
			if image:
				var texture = ImageTexture.create_from_image(image)
				icon_rect.texture = texture
				icon_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
				icon_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
		elif ResourceLoader.exists(icon_path):
			icon_rect.texture = load(icon_path)
	
	action_name = action
	set_focused(false)

func set_focused(focused: bool):
	if focus_panel:
		focus_panel.visible = focused
	
	if focused:
		modulate = Color(1.1, 1.1, 1.1, 1.0)
	else:
		modulate = Color(1.0, 1.0, 1.0, 1.0)
