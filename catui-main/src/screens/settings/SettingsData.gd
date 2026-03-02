extends Node

const BUTTON_TYPES = {
	"CHECKBOX": 0,
	"BUTTON": 1,
	"SLIDER": 2,
	"SUBMENU": 3,
	"KEYBIND": 4,
	"SELECTOR": 5,
	"SEPARATOR": 6
}

static func get_main_menu_items() -> Array:
	return [
		{"type": "SUBMENU", "label": "Controls", "submenu": "controls_main"},
		{"type": "SUBMENU", "label": "Video", "submenu": "video_main"},
		{"type": "SUBMENU", "label": "Audio", "submenu": "audio_main"},
		{"type": "SUBMENU", "label": "Interface", "submenu": "interface_main"},
		{"type": "SUBMENU", "label": "Emulation", "submenu": "emulation_main"},
		{"type": "SUBMENU", "label": "System", "submenu": "system_main", "disabled": true},
		{"type": "SUBMENU", "label": "Network", "submenu": "network_main", "disabled": true},
		{"type": "SUBMENU", "label": "About", "submenu": "about_main", "disabled": true}
	]

static func get_menu_data(menu_id: String) -> Dictionary:
	match menu_id:
		"controls_main":
			return _get_controls_menu()
		"video_main":
			return _get_video_menu()
		"audio_main":
			return _get_audio_menu()
		"interface_main":
			return _get_interface_menu()
		"emulation_main":
			return _get_emulation_menu()
		"system_main":
			return _get_system_menu()
		"network_main":
			return _get_network_menu()
		"about_main":
			return _get_about_menu()
		"home_menu_settings":
			return _get_home_menu_settings()
		"focus_bubble_settings":
			return _get_focus_bubble_settings()
		_:
			if menu_id.begins_with("controls_"):
				return _get_controls_submenu(menu_id.replace("controls_", ""))
			elif menu_id.begins_with("emulator_"):
				return _get_emulator_settings(menu_id.replace("emulator_", ""))
			elif menu_id.begins_with("manage_core_"):
				return _get_manage_core_menu(menu_id.replace("manage_core_", ""))
			return {}

static func _get_controls_menu() -> Dictionary:
	var cores = EmulatorConfig.get_all_cores().keys() if EmulatorConfig else []
	var items = []
	
	for core_id in cores:
		items.append({
			"type": "SUBMENU",
			"label": core_id.capitalize() + " Controls",
			"submenu": "controls_" + core_id
		})
	
	return {"label": "Controls", "items": items}

static func _get_controls_submenu(core_id: String) -> Dictionary:
	var normalized_id = _normalize_core_id(core_id)
	var actions = []
	
	for action_name in InputMap.get_actions():
		var action_str = str(action_name)
		if action_str.begins_with(normalized_id + "_"):
			var button_key = action_str.replace(normalized_id + "_", "")
			var label = _get_button_label(normalized_id, button_key)
			actions.append({
				"type": "KEYBIND",
				"label": label,
				"action": action_str
			})
	
	actions.sort_custom(func(a, b): return _get_button_priority(a["action"]) < _get_button_priority(b["action"]))
	
	return {"label": core_id.capitalize() + " Controls", "items": actions}

static func _get_video_menu() -> Dictionary:
	var items = []
	
	if OS.get_name() != "Android":
		items.append({"type": "CHECKBOX", "label": "Fullscreen", "key": "fullscreen", "default": false})
		items.append({"type": "CHECKBOX", "label": "VSync", "key": "vsync", "default": true})
	
	items.append({
		"type": "SELECTOR",
		"label": "FPS Limit",
		"key": "fps_limit",
		"options": [30, 60, 90, 120],
		"default": 60
	})
	
	items.append({"type": "SEPARATOR"})
	
	var cores = EmulatorConfig.get_all_cores().keys() if EmulatorConfig else []
	for core_id in cores:
		items.append({
			"type": "SUBMENU",
			"label": core_id.capitalize() + " Settings",
			"submenu": "emulator_" + core_id
		})
	
	return {"label": "Video", "items": items}

static func _get_audio_menu() -> Dictionary:
	return {
		"label": "Audio",
		"items": [
			{"type": "SLIDER", "label": "Emulator Volume", "key": "emu_volume", "min": 0.0, "max": 100.0, "step": 10.0, "default": 100.0},
			{"type": "SLIDER", "label": "SFX Volume", "key": "sfx_volume", "min": 0.0, "max": 100.0, "step": 10.0, "default": 100.0}
		]
	}

static func _get_interface_menu() -> Dictionary:
	var current_layout = "Xbox"
	if ButtonLayoutManager:
		current_layout = "Xbox" if ButtonLayoutManager.current_layout == 0 else "PlayStation"
	
	return {
		"label": "Interface",
		"items": [
			{"type": "SELECTOR", "label": "Controller Layout", "key": "controller_layout", 
			 "options": ["Xbox", "PlayStation"], "default": current_layout},
			{"type": "SUBMENU", "label": "Home Menu", "submenu": "home_menu_settings"},
			{"type": "SUBMENU", "label": "Focus Bubble", "submenu": "focus_bubble_settings"},
			{"type": "CHECKBOX", "label": "Show Debug Console", "key": "debug_console", "default": false}
		]
	}

static func _get_emulation_menu() -> Dictionary:
	var items = [
		{"type": "BUTTON", "label": "Import Core Manually", "action": "import_core"},
		{"type": "SEPARATOR"},
		{"type": "SLIDER", "label": "Max Auto-Saves", "key": "max_save_states", "min": 1.0, "max": 20.0, "step": 1.0, "default": 5.0},
		{"type": "SELECTOR", "label": "Auto-Save Interval", "key": "auto_save_interval", 
		 "options": ["Off", "1 min", "5 mins", "10 mins", "30 mins"], "default": "5 mins"},
		{"type": "SEPARATOR"}
	]
	
	var cores = EmulatorConfig.get_all_cores() if EmulatorConfig else {}
	for core_id in cores.keys():
		items.append({
			"type": "SUBMENU",
			"label": "Manage " + core_id.capitalize(),
			"submenu": "manage_core_" + core_id
		})
	
	return {
		"label": "Emulation",
		"items": items
	}

static func _get_manage_core_menu(core_id: String) -> Dictionary:
	var core_path = EmulatorConfig.get_libretro_core(core_id) if EmulatorConfig else "Unknown"
	var roms_path = EmulatorConfig.get_core_rooms_folder(core_id) if EmulatorConfig else "Unknown"
	
	return {
		"label": core_id.capitalize(),
		"items": [
			{"type": "SELECTOR", "label": "ROMs Folder", "key": "info_roms", "options": [(roms_path if roms_path != "" else "Select Folder...")], "default": (roms_path if roms_path != "" else "Select Folder..."), "action": "change_rom_folder", "emulator_id": core_id},
			{"type": "SEPARATOR"},
			{"type": "BUTTON", "label": "Delete Core", "action": "delete_core", "emulator_id": core_id}
		]
	}

static func _get_system_menu() -> Dictionary:
	return {
		"label": "System",
		"items": []
	}

static func _get_network_menu() -> Dictionary:
	return {
		"label": "Network",
		"items": []
	}

static func _get_about_menu() -> Dictionary:
	return {
		"label": "About",
		"items": []
	}



static func _get_home_menu_settings() -> Dictionary:
	return {
		"label": "Home Menu",
		"items": [
			{"type": "CHECKBOX", "label": "Show Clock", "key": "home_show_clock", "default": true},
			{"type": "CHECKBOX", "label": "Show Battery", "key": "home_show_battery", "default": true}
		]
	}

static func _get_focus_bubble_settings() -> Dictionary:
	return {
		"label": "Focus Bubble",
		"items": [
			{"type": "CHECKBOX", "label": "Enabled", "key": "focus_bubble_enabled", "default": true},
			{"type": "SLIDER", "label": "Opacity", "key": "focus_bubble_opacity", "min": 0.0, "max": 1.0, "step": 0.1, "default": 0.5},
			{"type": "SELECTOR", "label": "Animation Speed", "key": "focus_anim_speed", "options": ["Slow", "Normal", "Fast"], "default": "Normal"}
		]
	}

static func _get_emulator_settings(core_id: String) -> Dictionary:
	var items = [
		{"type": "SELECTOR", "label": "Stretch Mode", "key": "stretch_mode", "category": "video",
		 "options": ["Small", "Large", "Fill", "Proportional"], "default": "Fill", "emulator_id": core_id},
		{"type": "SELECTOR", "label": "Texture Filter", "key": "texture_filter", "category": "video",
		 "options": ["Inherit", "Nearest Mipmap", "Linear Mipmap"], "default": "Inherit", "emulator_id": core_id}
	]
	
	var specific = _get_core_specific_settings(core_id)
	if specific.size() > 0:
		items.append({"type": "SEPARATOR"})
		items.append_array(specific)
	
	return {"label": core_id.capitalize() + " Settings", "items": items}

static func _get_core_specific_settings(core_id: String) -> Array:
	match core_id:
		"pcsx_rearmed":
			return [
				{"type": "SELECTOR", "label": "Internal Resolution", "key": "internal_resolution", "category": "core",
				 "options": ["1x (Native)", "2x", "4x", "8x"], "default": "1x (Native)", "emulator_id": core_id},
				{"type": "CHECKBOX", "label": "Dithering", "key": "dithering", "category": "core", "default": true, "emulator_id": core_id},
				{"type": "CHECKBOX", "label": "Enhanced Resolution", "key": "enhanced_resolution", "category": "core", "default": false, "emulator_id": core_id}
			]
		"snes9x":
			return [
				{"type": "SELECTOR", "label": "Audio Interpolation", "key": "audio_interpolation", "category": "core",
				 "options": ["Gaussian", "Cubic", "Sinc", "None"], "default": "Gaussian", "emulator_id": core_id},
				{"type": "SLIDER", "label": "SuperFX Overclock", "key": "superfx_overclock", "category": "core",
				 "min": 50.0, "max": 400.0, "step": 10.0, "default": 100.0, "emulator_id": core_id},
				{"type": "CHECKBOX", "label": "Reduce Slowdown", "key": "reduce_slowdown", "category": "core", "default": false, "emulator_id": core_id}
			]
		"picodrive":
			return [
				{"type": "SELECTOR", "label": "Region", "key": "region", "category": "core",
				 "options": ["Auto", "Japan (NTSC)", "USA (NTSC)", "Europe (PAL)"], "default": "Auto", "emulator_id": core_id},
				{"type": "SELECTOR", "label": "Audio Quality", "key": "audio_quality", "category": "core",
				 "options": ["Low", "Medium", "High"], "default": "Medium", "emulator_id": core_id},
				{"type": "SLIDER", "label": "68k Overclock", "key": "m68k_overclock", "category": "core",
				 "min": 100.0, "max": 400.0, "step": 10.0, "default": 100.0, "emulator_id": core_id}
			]
		_:
			return []

static func _normalize_core_id(core_id: String) -> String:
	match core_id:
		"pcsx_rearmed", "swanstation", "mednafen_psx", "mednafen_psx_hw":
			return "ps1"
		"mgba", "vbam":
			return "gba"
		"snes9x":
			return "snes"
		"fceumm":
			return "nes"
		"gambatte":
			return "gb"
		"picodrive", "genesis_plus_gx":
			return "megadrive"
		_:
			return core_id

static func _get_button_label(core_id: String, button_key: String) -> String:
	var labels = {
		"ps1": {
			"cross": "✕ Cross", "circle": "○ Circle", "square": "□ Square", "triangle": "△ Triangle",
			"l1": "L1", "r1": "R1", "l2": "L2", "r2": "R2", "l3": "L3 (Stick)", "r3": "R3 (Stick)",
			"up": "D-Pad Up", "down": "D-Pad Down", "left": "D-Pad Left", "right": "D-Pad Right",
			"start": "Start", "select": "Select"
		},
		"snes": {
			"a": "A", "b": "B", "x": "X", "y": "Y", "l": "L", "r": "R",
			"up": "D-Pad Up", "down": "D-Pad Down", "left": "D-Pad Left", "right": "D-Pad Right",
			"start": "Start", "select": "Select"
		},
		"gba": {
			"a": "A", "b": "B", "l": "L", "r": "R",
			"up": "D-Pad Up", "down": "D-Pad Down", "left": "D-Pad Left", "right": "D-Pad Right",
			"start": "Start", "select": "Select"
		},
		"nes": {
			"a": "A", "b": "B",
			"up": "D-Pad Up", "down": "D-Pad Down", "left": "D-Pad Left", "right": "D-Pad Right",
			"start": "Start", "select": "Select"
		},
		"megadrive": {
			"a": "A", "b": "B", "c": "C",
			"x": "X", "y": "Y", "z": "Z",
			"up": "D-Pad Up", "down": "D-Pad Down", "left": "D-Pad Left", "right": "D-Pad Right",
			"start": "Start"
		}
	}
	
	if labels.has(core_id) and labels[core_id].has(button_key):
		return labels[core_id][button_key]
	
	return button_key.capitalize()

static func _get_button_priority(action: String) -> int:
	var button_order = [
		"up", "down", "left", "right",
		"cross", "circle", "square", "triangle",
		"a", "b", "c", "x", "y", "z",
		"l", "r", "l1", "r1", "l2", "r2", "l3", "r3",
		"start", "select"
	]
	
	for i in range(button_order.size()):
		if action.ends_with("_" + button_order[i]):
			return i
	
	return 999
