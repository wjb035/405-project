extends Control

signal boot_completed

@export var progress: ProgressBar
@export var status_label: Label

var is_loading: bool = false
var load_steps_completed: int = 0
var total_load_steps: int = 3

func _ready():
	visible = true
	modulate.a = 1.0
	
	if progress:
		progress.value = 0
		progress.max_value = 100
	
	_update_status("Initializing...")
	
	call_deferred("_start_loading")

func _start_loading():
	if is_loading:
		return
	
	is_loading = true
	
	await get_tree().process_frame
	await get_tree().process_frame
	
	_update_status("Loading settings...")
	_update_progress(10)
	await get_tree().create_timer(0.1).timeout
	
	_update_status("Loading cores...")
	_update_progress(30)
	await get_tree().create_timer(0.1).timeout
	
	if OS.get_name() == "Android" and AndroidManager.is_available:
		_update_status("Loading Android apps...")
		_update_progress(50)
		
		var apps = AndroidManager.get_installed_apps()
		
		_update_status("Loading app icons...")
		var icon_count = mini(apps.size(), 20)
		for i in range(icon_count):
			var app = apps[i]
			AndroidManager.get_app_icon_async(app["package"], 128)
			
			var icon_progress = 50 + int((float(i) / float(icon_count)) * 40)
			_update_progress(icon_progress)
			
			if i % 5 == 0:
				await get_tree().process_frame
	else:
		_update_progress(90)
		await get_tree().create_timer(0.2).timeout
	
	_update_status("Ready!")
	_update_progress(100)
	
	await get_tree().create_timer(0.3).timeout
	
	is_loading = false
	boot_completed.emit()

func _update_progress(value: int):
	if progress:
		var tween = create_tween()
		tween.tween_property(progress, "value", float(value), 0.15)

func _update_status(text: String):
	if status_label:
		status_label.text = text
