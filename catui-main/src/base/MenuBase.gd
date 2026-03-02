extends Control
class_name MenuBase

signal closed
signal back_requested

var is_active: bool = false

static var _focus_stack: Array[MenuBase] = []

func _ready():
	if not visible:
		is_active = false
		set_process_unhandled_input(false)
		
	visibility_changed.connect(func():
		if visible and not is_active:
			open()
		elif not visible:
			close()
	)
	
	tree_exiting.connect(func(): 
		if self in _focus_stack:
			_focus_stack.erase(self)
	)

func open():
	visible = true
	is_active = true
	set_process_unhandled_input(true)
	
	if self in _focus_stack:
		_focus_stack.erase(self)
	_focus_stack.append(self)
	
	modulate.a = 1.0
	
	on_open()

func close():
	is_active = false
	set_process_unhandled_input(false)
	
	if self in _focus_stack:
		_focus_stack.erase(self)
	
	
	if not is_active:
		visible = false
		emit_signal("closed")
		on_close()

func navigate_to(submenu: MenuBase):
	if not submenu:
		push_error("tried to navigate to a null menu!")
		return

	
	is_active = false
	
	submenu.open()
	
	await submenu.closed
	
	is_active = true
	if self in _focus_stack:
		_focus_stack.erase(self)
	_focus_stack.append(self)
	
	on_submenu_closed()

func on_open():
	pass

func on_close():
	pass

func on_submenu_closed():
	pass

func on_input(_event: InputEvent):
	pass

func get_menu_id() -> String:
	return name.to_lower()

func get_menu_context() -> GlobalAutoload.Context:
	return GlobalAutoload.Context.SUBMENU

func on_menu_registered():
	pass

func on_menu_shown():
	pass

func on_menu_hidden():
	pass

func _unhandled_input(event):
	if not _has_focus_authority():
		return
		
	if event.is_action_pressed("UI_BACK"):
		get_viewport().set_input_as_handled()
		handle_back()
		return
		
	on_input(event)

func _has_focus_authority() -> bool:
	if not visible or not is_active:
		return false
	
	if not _focus_stack.is_empty():
		return _focus_stack.back() == self
		
	return true

func handle_back():
	emit_signal("back_requested")
	close()
