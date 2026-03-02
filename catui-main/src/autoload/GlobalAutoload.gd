extends Node

enum Context {
	DASHBOARD,
	GAMEPLAY,
	HOME_MENU,
	SAVES,
	SETTINGS,
	SUBMENU,
	LIBRARY,
	OPTIONS_MENU,
	HOME_OPTIONS_MENU,
}

var current_context: Context = Context.GAMEPLAY
var context_changed_frame: int = -1



var current_game_texture: Texture2D = null
var current_system_name: String = ""
var current_rom_path: String = ""

# systems that can handle save states
var supported_systems_quick_menu: Array[String] = ["snes", "snes9x", "gba", "mgba", "vbam", "nes", "fceumm", "gb", "gbc", "gambatte", "gen", "md", "genesis_plus_gx"]

func is_quick_menu_compatible(system: String) -> bool:
	# checks if this system is cool enough for quick menu
	return system.to_lower() in supported_systems_quick_menu

signal context_changed(new_context: Context)

func set_context(new_context: Context):
	if current_context != new_context:
		current_context = new_context
		context_changed_frame = Engine.get_process_frames()
		context_changed.emit(new_context)

func is_context(check_context: Context) -> bool:
	return current_context == check_context

func is_context_fresh() -> bool:
	return context_changed_frame == Engine.get_process_frames()
