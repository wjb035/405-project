@tool
extends Control

func update_title(title: String, visible_label: bool):
	var label = get_node_or_null("Label")
	if label:
		label.text = title
		label.visible = visible_label

func update_icon(icon: Texture2D):
	var texture_rect = get_node_or_null("option_image/TextureRect")
	if texture_rect:
		texture_rect.texture = icon

func set_active(active: bool):
	# set transparency and focus panel so we know whos the chosen one
	if active:
		modulate.a = 1.0
	else:
		modulate.a = 0.7
		
	var focus_node = get_node_or_null("Focus")
	if not focus_node:
		focus_node = get_node_or_null("style/focus")
	
	if focus_node:
		focus_node.visible = active
