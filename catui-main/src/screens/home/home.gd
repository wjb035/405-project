extends Control

signal game_selected(rom_path: String)

@export var home_icons_grid: Control
@export var home_options_menu: Control

func _ready():
	_setup_connections()

func _setup_connections():
	if home_icons_grid:
		home_icons_grid.game_selected.connect(_on_grid_game_selected)
		home_icons_grid.edit_requested.connect(_on_grid_edit_requested)
		home_icons_grid.icon_size_changed.connect(_on_grid_icon_size_changed)
	
	if home_options_menu:
		home_options_menu.set_grid_reference(home_icons_grid)
		home_options_menu.menu_closed.connect(_on_options_menu_closed)
		home_options_menu.delete_requested.connect(_on_options_delete_requested)
		home_options_menu.move_mode_requested.connect(_on_options_move_requested)
		home_options_menu.size_changed.connect(_on_options_size_changed)

func _on_grid_game_selected(rom_path: String):
	game_selected.emit(rom_path)

func _on_grid_edit_requested():
	if home_options_menu:
		home_options_menu.show_menu()
		GlobalAutoload.set_context(GlobalAutoload.Context.HOME_OPTIONS_MENU)

func _on_grid_icon_size_changed(_size_name: String):
	pass

func _on_options_menu_closed():
	GlobalAutoload.set_context(GlobalAutoload.Context.DASHBOARD)

func _on_options_delete_requested():
	if home_icons_grid:
		home_icons_grid.delete_focused_icon()
	if home_options_menu:
		home_options_menu.hide_menu()

func _on_options_move_requested(_icon):
	if home_icons_grid:
		home_icons_grid.request_start_move_mode()
	if home_options_menu:
		home_options_menu.hide_menu()

func _on_options_size_changed(direction: int):
	if home_icons_grid:
		home_icons_grid.change_focused_icon_size(direction)
