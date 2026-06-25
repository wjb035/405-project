extends Control

@onready var container = $"../options/HBoxContainer"

func _ready():
	if container:
		container.resized.connect(_update_size)
		await get_tree().process_frame
		_update_size()

func _update_size():
	if container:
		size.x = container.size.x + 25
		
		pivot_offset.x = size.x / 2
		
		var container_center_x = container.global_position.x + (container.size.x / 2.0)
		global_position.x = container_center_x - (size.x / 2.0)
