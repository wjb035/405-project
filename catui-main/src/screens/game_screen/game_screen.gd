@tool
extends Control

@export_category("Menus")
@export var game_load: Control

enum VideoSize {
	SMALL,
	LARGE,
	FILL,
	PROPORTIONAL
}

enum VideoTextureFilter {
	INHERIT,
	NEAREST_MIPMAP,
	LINEAR_MIPMAP
}

@export_category("Emu settings")
@export var video: Panel
@export var audio: AudioStreamPlayer
var aspect_ratio: float = 4.0 / 3.0

@export var size_mode: VideoSize = VideoSize.FILL:
	set(value):
		size_mode = value
		apply_video_size(size_mode)

@export var video_texture_filter: VideoTextureFilter = VideoTextureFilter.INHERIT:
	set(value):
		video_texture_filter = value
		apply_texture_filter(video_texture_filter)

func _ready():
	apply_video_size(size_mode)
	apply_texture_filter(video_texture_filter)

func apply_texture_filter(filter_mode: VideoTextureFilter):
	if not video:
		return
	
	var texture_rect = video.get_node_or_null("TextureRect")
	if not texture_rect:
		return
	
	match filter_mode:
		VideoTextureFilter.INHERIT:
			texture_rect.texture_filter = CanvasItem.TEXTURE_FILTER_PARENT_NODE
		VideoTextureFilter.NEAREST_MIPMAP:
			texture_rect.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST_WITH_MIPMAPS
		VideoTextureFilter.LINEAR_MIPMAP:
			texture_rect.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS

func apply_video_size(video_size: VideoSize):
	if not video:
		return
	
	match video_size:
		VideoSize.SMALL:
			_set_centered_size(Vector2(400, 400))
			video.clip_children = Panel.CLIP_CHILDREN_ONLY
		VideoSize.LARGE:
			_set_centered_size(Vector2(600, 600))
			video.clip_children = Panel.CLIP_CHILDREN_ONLY
		VideoSize.FILL:
			video.custom_minimum_size = Vector2.ZERO
			video.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
			video.clip_children = Panel.CLIP_CHILDREN_DISABLED
		VideoSize.PROPORTIONAL:
			_update_proportional_size()
			video.clip_children = Panel.CLIP_CHILDREN_DISABLED

func _set_centered_size(target_size: Vector2):
	video.set_anchors_preset(Control.PRESET_CENTER)
	video.custom_minimum_size = target_size
	video.size = target_size
	video.grow_horizontal = Control.GROW_DIRECTION_BOTH
	video.grow_vertical = Control.GROW_DIRECTION_BOTH
	
	var half_size = target_size / 2.0
	video.offset_left = -half_size.x
	video.offset_top = -half_size.y
	video.offset_right = half_size.x
	video.offset_bottom = half_size.y

func _update_proportional_size():
	var available_size = size
	var target_width = available_size.x
	var target_height = target_width / aspect_ratio
	
	if target_height > available_size.y:
		target_height = available_size.y
		target_width = target_height * aspect_ratio
	
	_set_centered_size(Vector2(target_width, target_height))

func _notification(what):
	if what == NOTIFICATION_RESIZED:
		if size_mode == VideoSize.PROPORTIONAL:
			_update_proportional_size()
