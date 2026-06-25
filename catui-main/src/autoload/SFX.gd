extends Node

@export_range(-80, 24) var master_volume_db: float = 0.0

@export_group("Individual Volumes")
@export_range(-80, 24) var nav_volume_db: float = 0.0
@export_range(-80, 24) var select_volume_db: float = 0.0
@export_range(-80, 24) var notification_volume_db: float = 0.0
@export_range(-80, 24) var home_volume_db: float = 0.0
@export_range(-80, 24) var back_volume_db: float = 0.0

@export var nav_sound: AudioStreamMP3
@export var select_sound: AudioStreamMP3
@export var notification_sound: AudioStreamMP3
@export var home_sound: AudioStreamMP3
@export var back_sound: AudioStreamMP3

@export var ui_player: AudioStreamPlayer
@export var alert_player: AudioStreamPlayer



func _ready():
	
	if ui_player:
		ui_player.bus = "Master"
	if alert_player:
		alert_player.bus = "Master"

func _play_ui_sound(sound: AudioStream, individual_volume: float):
	if sound and ui_player:
		ui_player.stream = sound
		ui_player.volume_db = master_volume_db + individual_volume
		ui_player.play()

func _play_alert_sound(sound: AudioStream, individual_volume: float):
	if sound and alert_player:
		alert_player.stream = sound
		alert_player.volume_db = master_volume_db + individual_volume
		alert_player.play()

func play_nav():
	_play_ui_sound(nav_sound, nav_volume_db)

func play_select():
	_play_ui_sound(select_sound, select_volume_db)

func play_notification():
	_play_alert_sound(notification_sound, notification_volume_db)

func play_home():
	_play_ui_sound(home_sound, home_volume_db)

func play_back():
	_play_ui_sound(back_sound, back_volume_db)
