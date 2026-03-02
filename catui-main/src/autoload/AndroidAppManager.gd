extends Node
class_name AndroidAppManager

signal apps_loaded(apps: Array)
signal app_icon_loaded(package_name: String, texture: ImageTexture)
signal app_launched(package_name: String, success: bool)

var plugin = null
var is_available: bool = false
var cached_apps: Array = []
var cached_icons: Dictionary = {}

func _ready():
	_init_plugin()

func _init_plugin():
	DebugCapture.add_log("catui: starting android plugin...")
	if Engine.has_singleton("CatUILauncher"):
		plugin = Engine.get_singleton("CatUILauncher")
		is_available = true
		DebugCapture.add_log("catui: plugin loaded successfully!")
	else:
		is_available = false
		DebugCapture.add_log("catui: failure - plugin not found!")
		DebugCapture.add_log("catui: singletons: " + str(Engine.get_singleton_list()))

func get_installed_apps() -> Array:
	if not is_available:
		return _get_mock_apps()
	
	var raw_apps = plugin.getInstalledApps()
	var apps = []
	
	for entry in raw_apps:
		var parts = entry.split("|")
		if parts.size() >= 2:
			apps.append({
				"package": parts[0],
				"name": parts[1]
			})
	
	cached_apps = apps
	apps_loaded.emit(apps)
	return apps

func get_app_icon_async(package_name: String, size: int = 128):
	if cached_icons.has(package_name):
		app_icon_loaded.emit(package_name, cached_icons[package_name])
		return
	
	if not is_available:
		app_icon_loaded.emit(package_name, null)
		return
	
	var base64_icon = plugin.getAppIcon(package_name, size)
	
	if base64_icon == "":
		app_icon_loaded.emit(package_name, null)
		return
	
	var image_data = Marshalls.base64_to_raw(base64_icon)
	var image = Image.new()
	var error = image.load_png_from_buffer(image_data)
	
	if error != OK:
		app_icon_loaded.emit(package_name, null)
		return
	
	var texture = ImageTexture.create_from_image(image)
	cached_icons[package_name] = texture
	app_icon_loaded.emit(package_name, texture)

func launch_app(package_name: String):
	if not is_available:
		print("android_manager: cannot launch - plugin not available")
		app_launched.emit(package_name, false)
		return
	
	var success = plugin.launchApp(package_name)
	app_launched.emit(package_name, success)

func launch_app_with_file(package_name: String, file_path: String):
	if not is_available:
		print("android_manager: cannot launch with file - plugin not available")
		app_launched.emit(package_name, false)
		return
	
	var success = plugin.launchAppWithFile(package_name, file_path)
	app_launched.emit(package_name, success)

func get_app_name(package_name: String) -> String:
	if not is_available:
		return package_name
	
	return plugin.getAppName(package_name)

func _get_mock_apps() -> Array:
	var mock = []
	for i in range(150):
		mock.append({
			"package": "com.test.app" + str(i),
			"name": "Test App " + str(i + 1)
		})
	cached_apps = mock
	return mock
