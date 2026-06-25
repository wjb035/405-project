@tool
extends TextureRect

enum ControllerLayout {
	XBOX,
	PLAYSTATION,
}

@export var use_global_layout: bool = true:
	set(value):
		use_global_layout = value
		_update_button_icon()

@export var layout: ControllerLayout = ControllerLayout.XBOX:
	set(value):
		layout = value
		if not use_global_layout:
			_update_button_icon()

const BUTTON_MAPPINGS = {
	ControllerLayout.XBOX: {
		"button_0": "a.png",
		"button_1": "b.png",
		"button_2": "x.png",
		"button_3": "y.png",
		"button_4": "select.png",
		"button_5": "home.png",
		"button_6": "start.png",
		"button_9": "lb.png",
		"button_10": "rb.png",
	},
	ControllerLayout.PLAYSTATION: {
		"button_0": "cross.png",
		"button_1": "circle.png",
		"button_2": "square.png",
		"button_3": "triangle.png",
		"button_4": "select.png",
		"button_5": "super.png",
		"button_6": "start.png",
		"button_9": "l1.png",
		"button_10": "r1.png",
	}
}

const LAYOUT_PATHS = {
	ControllerLayout.XBOX: "res://assets/icons/joystick/xbox/",
	ControllerLayout.PLAYSTATION: "res://assets/icons/joystick/ps/",
}

func _ready():
	if use_global_layout and not Engine.is_editor_hint():
		var manager = get_node_or_null("/root/ButtonLayoutManager")
		if manager:
			manager.layout_changed.connect(_on_global_layout_changed)
			_on_global_layout_changed(manager.current_layout)
		else:
			_update_button_icon()
	else:
		_update_button_icon()

func _on_global_layout_changed(new_layout: int):
	layout = new_layout as ControllerLayout
	_update_button_icon()

func _update_button_icon():
	if not is_inside_tree():
		return
	
	var active_layout = layout
	if use_global_layout and not Engine.is_editor_hint():
		var manager = get_node_or_null("/root/ButtonLayoutManager")
		if manager:
			active_layout = manager.current_layout
	
	var button_name = name.to_lower()
	
	if not BUTTON_MAPPINGS.has(active_layout):
		push_warning("ButtonLayout: Unknown layout %s" % active_layout)
		return
	
	var mapping = BUTTON_MAPPINGS[active_layout]
	if not mapping.has(button_name):
		push_warning("ButtonLayout: No mapping for button '%s' in layout %s" % [button_name, active_layout])
		return
	
	var icon_file = mapping[button_name]
	var icon_path = LAYOUT_PATHS[active_layout] + icon_file
	
	if not ResourceLoader.exists(icon_path):
		push_warning("ButtonLayout: Icon not found at '%s'" % icon_path)
		return
	
	texture = load(icon_path)
