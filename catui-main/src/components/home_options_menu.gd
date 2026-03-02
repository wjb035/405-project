extends Control

signal item_selected(action: String)
signal menu_closed
signal move_mode_requested(icon: Control)
signal delete_requested
signal size_changed(direction: int)

var items: Array[Control] = []
var focus_index: int = 0
var is_active: bool = false

@onready var vbox = $_/MarginContainer/VBoxContainer
@onready var size_label = $_/MarginContainer/VBoxContainer/size/Label

var home_icons_grid: Control = null

func _ready() -> void:
	visible = false
	_setup_items()

func _setup_items():
	if not vbox:
		return
	
	items.clear()
	for child in vbox.get_children():
		if child is Control:
			items.append(child)
	
	_update_focus()

func _input(event: InputEvent) -> void:
	if not is_active:
		return
	
	if event.is_action_pressed("UI_UP"):
		_navigate(-1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_DOWN"):
		_navigate(1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_SELECT"):
		_select_item()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_BACK") or event.is_action_pressed("UI_HOME") or event.is_action_pressed("UI_MENU"):
		if SFX:
			SFX.play_back()
		hide_menu()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_NEXT"):
		_handle_size_input(1)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("UI_PREV"):
		_handle_size_input(-1)
		get_viewport().set_input_as_handled()

func _navigate(direction: int):
	if items.is_empty():
		return
	
	var new_index = focus_index + direction
	new_index = clampi(new_index, 0, items.size() - 1)
	
	if new_index != focus_index:
		if SFX:
			SFX.play_nav()
		focus_index = new_index
		_update_focus()

func _update_focus():
	for i in range(items.size()):
		var item = items[i]
		if not item:
			continue
		
		var focus_panel = item.get_node_or_null("style/focus")
		if focus_panel:
			focus_panel.visible = (i == focus_index)

func _select_item():
	if focus_index >= items.size():
		return
	
	var item = items[focus_index]
	if not item:
		return
	
	if SFX:
		SFX.play_select()
	
	var item_name = item.name.to_lower()
	
	match item_name:
		"delete":
			delete_requested.emit()
		"size":
			pass
		"move":
			move_mode_requested.emit(null)
		_:
			item_selected.emit(item_name)

func _handle_size_input(direction: int):
	if focus_index >= items.size():
		return
	
	var item = items[focus_index]
	if not item:
		return
	
	if item.name.to_lower() != "size":
		return
	
	size_changed.emit(direction)
	call_deferred("_update_size_label")

func show_menu():
	is_active = true
	visible = true
	focus_index = 0
	_update_focus()
	_update_size_label()
	GlobalAutoload.set_context(GlobalAutoload.Context.HOME_OPTIONS_MENU)

func hide_menu():
	is_active = false
	visible = false
	GlobalAutoload.set_context(GlobalAutoload.Context.GAMEPLAY)
	menu_closed.emit()

func get_focused_item_name() -> String:
	if focus_index >= items.size():
		return ""
	
	var item = items[focus_index]
	if not item:
		return ""
	
	return item.name.to_lower()

func _update_size_label():
	if not size_label or not home_icons_grid:
		return
	
	var children = home_icons_grid._get_valid_children()
	if children.is_empty():
		return
	
	var focused_index = home_icons_grid.focused_index
	if focused_index < 0 or focused_index >= children.size():
		return
	
	var icon = children[focused_index]
	if not icon or not "icon_size" in icon:
		return
	
	var size_letter = ""
	match icon.icon_size:
		0:
			size_letter = "S"
		1:
			size_letter = "M"
		2:
			size_letter = "L"
	
	size_label.text = "Size: " + size_letter

func set_grid_reference(grid: Control):
	home_icons_grid = grid
