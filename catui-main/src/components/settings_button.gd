@tool
extends Control

enum ButtonType {
	CHECKBOX,
	BUTTON,
	SLIDER,
	SUBMENU,
	KEYBIND,
	SELECTOR,
	SEPARATOR
}

signal value_changed(value)
signal button_pressed()

@export var button_type: ButtonType = ButtonType.CHECKBOX:
	set(value):
		button_type = value
		_update_type()

@export var label_text: String = "Option":
	set(value):
		label_text = value
		_update_label()

@export var checkbox_value: bool = false:
	set(value):
		checkbox_value = value
		_update_checkbox()

@export_range(0.0, 1.0) var slider_value: float = 0.5:
	set(value):
		slider_value = value
		_update_slider()

@export var slider_min: float = 0.0:
	set(value):
		slider_min = value
		_update_slider_range()

@export var slider_max: float = 100.0:
	set(value):
		slider_max = value
		_update_slider_range()

@export var slider_step: float = 1.0:
	set(value):
		slider_step = value
		_update_slider_range()

@export var keybind_text: String = "":
	set(value):
		keybind_text = value
		_update_keybind()

@export var action_name: String = ""
@export var submenu_id: String = ""

@export var selector_options: Array = []
@export var selector_index: int = 0:
	set(value):
		if selector_options.size() > 0:
			selector_index = clampi(value, 0, selector_options.size() - 1)
			_update_selector()

signal submenu_selected(submenu_id)
signal keybind_pressed(action_name)

@onready var label = $Label
@onready var checkbox = $CheckBox
@onready var slider = $HSlider
@onready var value_label = $Value

func _ready():
	_setup_connections()
	_update_type()
	_update_label()
	_update_checkbox()
	_update_slider_range()
	_update_slider()

func _setup_connections():
	if not Engine.is_editor_hint():
		if checkbox:
			checkbox.toggled.connect(_on_checkbox_toggled)
		if slider:
			slider.value_changed.connect(_on_slider_value_changed)

func _gui_input(event):
	if not Engine.is_editor_hint() and button_type == ButtonType.BUTTON:
		if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
			_on_button_pressed()

func _update_type():
	if not is_inside_tree():
		return
	
	var label_node = get_node_or_null("Label")
	var checkbox_node = get_node_or_null("CheckBox")
	var slider_node = get_node_or_null("HSlider")
	var value_node = get_node_or_null("Value")
	
	if button_type == ButtonType.SEPARATOR:
		custom_minimum_size.y = 40
		if label_node:
			label_node.visible = false
		if checkbox_node:
			checkbox_node.visible = false
		if slider_node:
			slider_node.visible = false
		if value_node:
			value_node.visible = false
		return
	
	custom_minimum_size.y = 60
	
	if label_node:
		label_node.visible = true
		if button_type == ButtonType.BUTTON:
			# center text because reasons
			label_node.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
			label_node.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
			label_node.anchors_preset = Control.PRESET_FULL_RECT
			label_node.offset_left = 0
			label_node.offset_right = 0
			label_node.offset_top = 0
			label_node.offset_bottom = 0
		else:
			label_node.anchors_preset = Control.PRESET_CENTER_LEFT
			label_node.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
			label_node.set_anchors_preset(Control.PRESET_CENTER_LEFT, true) # make sure layout knows whats up
			label_node.offset_left = 15
			
		label_node.custom_minimum_size.x = 150
	
	if checkbox_node:
		checkbox_node.visible = (button_type == ButtonType.CHECKBOX)
	if slider_node:
		slider_node.visible = (button_type == ButtonType.SLIDER)
		if button_type == ButtonType.SLIDER:
			slider_node.focus_mode = Control.FOCUS_NONE
			slider_node.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if value_node:
		if button_type == ButtonType.SLIDER:
			value_node.visible = true
		elif button_type == ButtonType.KEYBIND:
			value_node.visible = true
			value_node.text = keybind_text if keybind_text else "---"
		elif button_type == ButtonType.SUBMENU:
			value_node.visible = true
			value_node.text = ">"
		elif button_type == ButtonType.SELECTOR:
			value_node.visible = true
			_update_selector()
		elif button_type == ButtonType.BUTTON:
			value_node.visible = true
			value_node.text = ""
		else:
			value_node.visible = false

func _update_label():
	if not is_inside_tree():
		return
	
	var label_node = get_node_or_null("Label")
	if label_node:
		label_node.text = label_text

func _update_checkbox():
	if not is_inside_tree():
		return
	
	var checkbox_node = get_node_or_null("CheckBox")
	if checkbox_node:
		checkbox_node.button_pressed = checkbox_value

func _update_slider():
	if not is_inside_tree():
		return
	
	var slider_node = get_node_or_null("HSlider")
	var value_node = get_node_or_null("Value")
	
	if slider_node:
		slider_node.value = slider_value * (slider_max - slider_min) + slider_min
	
	if value_node and button_type == ButtonType.SLIDER:
		var display_value = slider_value * (slider_max - slider_min) + slider_min
		value_node.text = str(int(display_value)) if slider_step >= 1.0 else "%.1f" % display_value

func _update_slider_range():
	if not is_inside_tree():
		return
	
	var slider_node = get_node_or_null("HSlider")
	if slider_node:
		slider_node.min_value = slider_min
		slider_node.max_value = slider_max
		slider_node.step = slider_step

func _update_keybind():
	if not is_inside_tree():
		return
	
	var value_node = get_node_or_null("Value")
	if value_node and button_type == ButtonType.KEYBIND:
		value_node.text = keybind_text if keybind_text else "---"

func _update_selector():
	if not is_inside_tree():
		return
	
	var value_node = get_node_or_null("Value")
	if value_node and button_type == ButtonType.SELECTOR:
		if selector_options.size() > 0 and selector_index < selector_options.size():
			value_node.text = "< " + str(selector_options[selector_index]) + " >"
		else:
			value_node.text = "---"

func selector_next():
	if selector_options.size() > 0:
		selector_index = (selector_index + 1) % selector_options.size()
		_update_selector()
		value_changed.emit(get_selector_value())

func selector_prev():
	if selector_options.size() > 0:
		selector_index = (selector_index - 1 + selector_options.size()) % selector_options.size()
		_update_selector()
		value_changed.emit(get_selector_value())

func get_selector_value():
	if selector_options.size() > 0 and selector_index < selector_options.size():
		return selector_options[selector_index]
	return null

func _on_checkbox_toggled(toggled: bool):
	checkbox_value = toggled
	value_changed.emit(toggled)

func _on_button_pressed():
	button_pressed.emit()

func _on_slider_value_changed(value: float):
	slider_value = (value - slider_min) / (slider_max - slider_min)
	_update_slider()
	value_changed.emit(value)

func get_value():
	match button_type:
		ButtonType.CHECKBOX:
			return checkbox_value
		ButtonType.SLIDER:
			return slider_value * (slider_max - slider_min) + slider_min
		ButtonType.SELECTOR:
			return get_selector_value()
		ButtonType.BUTTON:
			return null
	return null

func set_value(value):
	match button_type:
		ButtonType.CHECKBOX:
			checkbox_value = value
		ButtonType.SLIDER:
			slider_value = (value - slider_min) / (slider_max - slider_min)
		ButtonType.SELECTOR:
			var idx = selector_options.find(value)
			if idx >= 0:
				selector_index = idx
				_update_selector()

func trigger_submenu():
	if button_type == ButtonType.SUBMENU:
		submenu_selected.emit(submenu_id)

func trigger_keybind():
	if button_type == ButtonType.KEYBIND:
		keybind_pressed.emit(action_name)

func set_value_text(text: String):
	if not is_inside_tree():
		return
	
	var value_node = get_node_or_null("Value")
	if value_node:
		value_node.text = text
