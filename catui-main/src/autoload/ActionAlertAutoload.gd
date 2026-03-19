extends Node

var alert_manager = null

func _ready():
	var ActionAlertScript = load("res://src/autoload/ActionAlert.gd")
	if ActionAlertScript:
		alert_manager = ActionAlertScript.new()
		alert_manager.name = "AlertManager"
		add_child(alert_manager)

func show_alert(message: String):
	if alert_manager and alert_manager.has_method("show_alert"):
		alert_manager.show_alert(message)

func show_alert_sticky(message: String):
	if alert_manager and alert_manager.has_method("show_alert_sticky"):
		alert_manager.show_alert_sticky(message)

func hide_alert():
	if alert_manager and alert_manager.has_method("hide_alert"):
		alert_manager.hide_alert()
