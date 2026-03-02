extends Control


@onready var label = $Label
@onready var focus_panel = $style/focus

var action_name: String = ""

func _ready():
	if focus_panel:
		focus_panel.visible = false

func setup(p_text: String, p_action: String):
	label.text = p_text
	action_name = p_action
	name = p_action

func set_focused(is_focused: bool):
	if focus_panel:
		focus_panel.visible = is_focused
	
	if is_focused:
		# Small pop animation
		var tween = create_tween()
		tween.set_trans(Tween.TRANS_CUBIC)
		tween.set_ease(Tween.EASE_OUT)
		scale = Vector2.ONE * 1.05
		tween.tween_property(self, "scale", Vector2.ONE, 0.2)
	else:
		scale = Vector2.ONE
