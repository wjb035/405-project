extends Control

@onready var label = $Panel/ScrollContainer/RichTextLabel

var max_lines = 100
var log_lines = []
var update_timer = 0.0
var update_interval = 3.0
var log_file_path = ""

func _ready():
	if label:
		label.bbcode_enabled = true
		label.text = "[color=green]debug console ready[/color]\n"
		add_log("debug console initialized")
		add_log("Platform: " + OS.get_name())
	
	if has_node("/root/DebugCapture"):
		get_node("/root/DebugCapture").set_console(self)
	
	log_file_path = ProjectSettings.globalize_path("user://debug_log.txt")
	add_log("Log file: " + log_file_path)

func _process(delta):
	update_timer += delta
	if update_timer >= update_interval:
		update_timer = 0.0
		_read_log_file()

func _read_log_file():
	if not FileAccess.file_exists(log_file_path):
		return
	
	var file = FileAccess.open(log_file_path, FileAccess.READ)
	if file:
		var content = file.get_as_text()
		file.close()
		
		var lines = content.split("\n")
		log_lines.clear()
		
		for line in lines:
			if line.strip_edges() != "":
				_format_and_add_line(line)
		
		_update_display()

func _format_and_add_line(text: String):
	var formatted_line = text
	
	if "ERROR" in text or "error" in text or "Failed" in text or "failed" in text:
		formatted_line = "[color=red]%s[/color]" % text
	elif "success" in text or "loaded" in text:
		formatted_line = "[color=green]%s[/color]" % text
	elif "LibretroPlayer" in text or "Android" in text or "LibretroNative" in text:
		formatted_line = "[color=yellow]%s[/color]" % text
	else:
		formatted_line = "[color=gray]%s[/color]" % text
	
	log_lines.append(formatted_line)

func _update_display():
	if not label:
		return
	
	if log_lines.size() > max_lines:
		var start_index = log_lines.size() - max_lines
		var visible_lines = []
		for i in range(start_index, log_lines.size()):
			visible_lines.append(log_lines[i])
		label.text = "\n".join(visible_lines)
	else:
		label.text = "\n".join(log_lines)
	
	await get_tree().process_frame
	if $Panel/ScrollContainer is ScrollContainer:
		$Panel/ScrollContainer.scroll_vertical = 999999

func add_log(text: String):
	if not label:
		return
	
	var timestamp = Time.get_time_string_from_system()
	var formatted_line = "[color=gray]%s[/color] %s" % [timestamp, text]
	
	if "ERROR" in text or "error" in text or "Failed" in text or "failed" in text:
		formatted_line = "[color=red]%s %s[/color]" % [timestamp, text]
	elif "success" in text or "loaded" in text:
		formatted_line = "[color=green]%s %s[/color]" % [timestamp, text]
	elif "LibretroPlayer" in text or "Android" in text or "LibretroNative" in text:
		formatted_line = "[color=yellow]%s %s[/color]" % [timestamp, text]
	
	log_lines.append(formatted_line)
	
	if log_lines.size() > max_lines:
		log_lines.pop_front()
	
	label.text = "\n".join(log_lines)
	
	await get_tree().process_frame
	if $Panel/ScrollContainer is ScrollContainer:
		$Panel/ScrollContainer.scroll_vertical = 999999

func clear_log():
	log_lines.clear()
	if label:
		label.text = "[color=green]log cleared[/color]\n"
