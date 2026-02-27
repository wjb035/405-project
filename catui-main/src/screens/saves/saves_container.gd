extends FlowContainer

signal save_selected(save_data)
signal selection_changed(index)

var focused_index: int = 0
@export var focus_tittle: Label

func update_selection():
	var count = get_child_count()
	if count == 0:
		return
	
	focused_index = clampi(focused_index, 0, count - 1)
	selection_changed.emit(focused_index)
	
	for i in range(count):
		var child = get_child(i)
		if child.has_method("set_focused"):
			child.set_focused(i == focused_index)
		
		if i == focused_index and focus_tittle:
			if "save_data" in child and child.save_data.has("game_name"):
				focus_tittle.text = child.save_data["game_name"]
			else:
				focus_tittle.text = ""

func _get_columns_count() -> int:
	var count = get_child_count()
	if count < 2:
		return 1
	
	if get_child(0).position == Vector2.ZERO and get_child(count-1).position == Vector2.ZERO:
		return 1
		
	var first_y = get_child(0).position.y
	for i in range(1, count):
		if get_child(i).position.y > first_y + 10:
			return i
	return count

var input_enabled: bool = true

func _unhandled_input(event):
	if not is_visible_in_tree() or not input_enabled:
		return
		
	
	var count = get_child_count()
	if count == 0:
		return
	
	var columns = _get_columns_count()
	var prev_index = focused_index
	
	if event.is_action_pressed("UI_RIGHT"):
		focused_index = min(focused_index + 1, count - 1)
	elif event.is_action_pressed("UI_LEFT"):
		focused_index = max(focused_index - 1, 0)
	elif event.is_action_pressed("UI_DOWN"):
		focused_index = min(focused_index + columns, count - 1)
	elif event.is_action_pressed("UI_UP"):
		focused_index = max(focused_index - columns, 0)
	elif event.is_action_pressed("UI_SELECT"):
		if SFX:
			SFX.play_select()
		var child = get_child(focused_index)
		save_selected.emit(child.save_data)
		get_viewport().set_input_as_handled()
		return
	
	if prev_index != focused_index:
		if SFX:
			SFX.play_nav()
		update_selection()
		get_viewport().set_input_as_handled()
