@tool
extends Control

enum IconSize {
	SMALL,
	MEDIUM,
	LARGE
}

enum IconType {
	GAME,
	STICK
}

@export_group("Icon Settings")
@export var icon_size: IconSize = IconSize.SMALL:
	set(value):
		var changed = icon_size != value
		icon_size = value
		if changed or Engine.is_editor_hint():
			_update_size()
			notify_property_list_changed()

@export var icon_type: IconType = IconType.GAME

@export_group("Game Data")
@export var game_name: String = ""
@export var game_url: String = ""
@export var custom_image_path: String = ""
@export var emulator_id: String = ""
@export var custom_texture: Texture2D = null:
	set(value):
		custom_texture = value
		_update_texture()

@onready var texture_rect = $IconImg/TextureRect

func _ready() -> void:
	_update_size()
	_update_texture()

func _update_size() -> void:
	match icon_size:
		IconSize.SMALL:
			custom_minimum_size = Vector2(100, 100)
		IconSize.MEDIUM:
			custom_minimum_size = Vector2(200, 100)
		IconSize.LARGE:
			custom_minimum_size = Vector2(200, 200)
	
	size = custom_minimum_size
	update_minimum_size()
	
	if Engine.is_editor_hint() and get_parent() is Container:
		get_parent().queue_sort()

func _update_texture() -> void:
	if not is_inside_tree():
		return
	
	if texture_rect and custom_texture:
		texture_rect.texture = custom_texture
