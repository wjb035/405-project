extends Node

var debug_console: Control = null
var is_console_visible: bool = false

func _ready():
	name = "DebugCapture"

func set_console(console: Control):
	debug_console = console
	debug_console.visible = is_console_visible
	add_log("Debug console connected!")

func set_visibility(visible: bool):
	is_console_visible = visible
	if debug_console:
		debug_console.visible = visible

func add_log(text: String):
	if debug_console and debug_console.has_method("add_log"):
		debug_console.add_log(text)
	print(text)
