@tool
extends EditorPlugin

var export_plugin : AndroidExportPlugin

func _enter_tree():
	export_plugin = AndroidExportPlugin.new()
	add_export_plugin(export_plugin)

func _exit_tree():
	remove_export_plugin(export_plugin)
	export_plugin = null

class AndroidExportPlugin extends EditorExportPlugin:
	var _plugin_name = "CatUILauncher"

	func _supports_platform(platform):
		if platform is EditorExportPlatformAndroid:
			return true
		return false

	func _get_android_libraries(platform, debug):
		if debug:
			return PackedStringArray(["res://addons/CatUILauncher_Plugin/android_plugin/CatUILauncher.aar"])
		else:
			return PackedStringArray(["res://addons/CatUILauncher_Plugin/android_plugin/CatUILauncher.aar"])

	func _get_name():
		return _plugin_name
