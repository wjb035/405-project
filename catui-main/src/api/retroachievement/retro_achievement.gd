extends Node

signal game_data_loaded(rom_path: String, data: Dictionary)
signal cover_downloaded(rom_path: String, local_path: String)
signal queue_progress(current: int, total: int)
signal queue_finished

const BASE_URL = "https://retroachievements.org"
const API_BASE = "https://retroachievements.org/API"
const CACHE_FILE = "user://retroachievements_cache.json"
const COVERS_FOLDER = "user://covers"
const ICONS_FOLDER = "user://icons"

@export_group("API Configuration")
@export var api_key: String = ""
@export var username: String = ""
@export var enabled: bool = true

@export_group("Download Settings")
@export_range(1, 10) var max_concurrent_downloads: int = 2
@export_range(0.5, 5.0) var request_delay: float = 1.0
@export var auto_download_covers: bool = true

@export_group("Search Settings")
@export_range(0.0, 1.0) var min_match_score: float = 0.85
@export var use_fuzzy_search: bool = true

var http_request: HTTPRequest
var image_downloaders: Array = []
var active_downloads: int = 0

var cache: Dictionary = {}
var download_queue: Array = []
var is_processing_queue: bool = false

var lookup_count: int = 0
var lookup_total: int = 0

var games_database: Dictionary = {}

var console_id_map = {
	"gba": 5,
	"gbc": 6,
	"gb": 4,
	"nes": 7,
	"snes": 3,
	"sfc": 3,
	"smc": 3,
	"md": 1,
	"gen": 1,
	"n64": 2,
	"nds": 18,
	"psx": 12,
	"ps1": 12,
	"psp": 41,
	"arcade": 27,
	"mame": 27,
	"neo": 9,
	"pce": 8,
	"sms": 11,
	# Disc formats (assuming PS1 as default for chd/iso/bin/cue/pbp if no hint)
	"chd": 12,
	"iso": 12,
	"bin": 12,
	"cue": 12,
	"pbp": 12,
	"cso": 41, # PSP
	"gg": 15,
	"a2600": 25,
	"lynx": 13,
	"ws": 53,
	"wsc": 53,
	"ngp": 14
}

var core_to_console_map = {
	"pcsx_rearmed": 12, # PS1
	"swanstation": 12,
	"mednafen_psx": 12,
	"mednafen_psx_hw": 12,
	"snes9x": 3,
	"picodrive": 1,
	"genesis_plus_gx": 1,
	"fceumm": 7,
	"nestopia": 7,
	"gambatte": 4,
	"mgba": 5,
	"vbam": 5,
	"melonds": 18,
	"desmume": 18,
	"mupen64plus_next": 2,
	"parallel_n64": 2,
	"fbneo": 27,
	"mame2003_plus": 27,
	"ppsspp": 41
}

func _ready():
	_setup_http_requests()
	_setup_directories()
	_load_cache()

func _setup_http_requests():
	http_request = HTTPRequest.new()
	add_child(http_request)
	http_request.timeout = 30.0
	
	for i in range(max_concurrent_downloads):
		var downloader = HTTPRequest.new()
		add_child(downloader)
		downloader.timeout = 60.0
		image_downloaders.append(downloader)

func _setup_directories():
	var covers_path = COVERS_FOLDER.replace("user://", OS.get_user_data_dir() + "/")
	var icons_path = ICONS_FOLDER.replace("user://", OS.get_user_data_dir() + "/")
	
	if not DirAccess.dir_exists_absolute(covers_path):
		DirAccess.make_dir_recursive_absolute(covers_path)
	
	if not DirAccess.dir_exists_absolute(icons_path):
		DirAccess.make_dir_recursive_absolute(icons_path)

func _load_cache():
	if not FileAccess.file_exists(CACHE_FILE):
		cache = {}
		return
	
	var file = FileAccess.open(CACHE_FILE, FileAccess.READ)
	if not file:
		cache = {}
		return
	
	var json_string = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var error = json.parse(json_string)
	
	if error == OK:
		cache = json.data
	else:
		cache = {}

func _save_cache():
	var file = FileAccess.open(CACHE_FILE, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(cache, "\t"))
		file.close()

func is_configured() -> bool:
	return not api_key.is_empty() and not username.is_empty()

func get_cache_key(rom_path: String) -> String:
	return rom_path.md5_text()

func has_cached_data(rom_path: String) -> bool:
	var key = get_cache_key(rom_path)
	return cache.has(key)

func get_cached_data(rom_path: String) -> Dictionary:
	var key = get_cache_key(rom_path)
	if cache.has(key):
		return cache[key]
	return {}

func queue_rom_lookup(rom_path: String, console_hint: String = ""):
	if not enabled:
		return
	
	if not is_configured():
		return
	
	if has_cached_data(rom_path):
		var cached_data = get_cached_data(rom_path)
		if cached_data.get("found", false):
			game_data_loaded.emit(rom_path, cached_data)
			return
	
	download_queue.append({
		"rom_path": rom_path,
		"console_hint": console_hint,
		"type": "lookup"
	})
	
	if not is_processing_queue:
		_process_queue()

func queue_multiple_roms(rom_paths: Array, console_hint: String = ""):
	lookup_count = 0
	lookup_total = rom_paths.size()
	
	for path in rom_paths:
		queue_rom_lookup(path, console_hint)

func _process_queue():
	if download_queue.is_empty():
		is_processing_queue = false
		queue_finished.emit()
		return
	
	is_processing_queue = true
	
	var item = download_queue.pop_front()
	
	if item["type"] == "lookup":
		lookup_count += 1
		queue_progress.emit(lookup_count, lookup_total)
		await _lookup_game(item["rom_path"], item["console_hint"])
	elif item["type"] == "cover":
		await _download_image(item["url"], item["local_path"], item["rom_path"])
	
	await get_tree().create_timer(request_delay).timeout
	_process_queue()

func identify_rom_by_hash(rom_path: String) -> Dictionary:
	if not enabled or not is_configured():
		print("    ✗ RetroAchievements not configured")
		return {}
	
	# 1. Setup Fallback Params
	var ext = rom_path.get_extension().to_lower()
	var rom_name = rom_path.get_file().get_basename()
	var console_id = _get_console_id(rom_path)
	var is_disc = ext in ["chd", "iso", "bin", "cue", "pbp", "cso"] # Hints for serial search
	
	print("    processing '", rom_name, "'")
	
	# A. Try Serial (Disc Only - Highly Accurate)
	if is_disc:
		var serial = _extract_serial_from_path(rom_path)
		if serial != "":
			# print("    found serial: ", serial)
			var serial_data = await _lookup_game_by_name(serial, rom_path)
			if serial_data.get("found", false):
				serial_data["method"] = "serial"
				_cache_result(rom_path, serial_data)
				_handle_auto_download(rom_path, serial_data)
				return serial_data

	# B. Smart Title Search (For everyone, but smarter)
	var clean_name = _clean_rom_name(rom_name)
	print("    searching '", clean_name, "'")
	
	var search_data = await _lookup_game_by_name(clean_name, rom_path, console_id)
	var score = search_data.get("score", 0.0)
	var min_score = 0.98 # Default strict
	
	# Discs are notoriously hard to hash, so we allow slightly fuzzy matches
	if is_disc: 
		min_score = 0.90 
		
	if search_data.get("found", false) and score >= min_score:
		print("    found it! (score: ", score, ")")
		search_data["method"] = "title_strict"
		_cache_result(rom_path, search_data)
		_handle_auto_download(rom_path, search_data)
		return search_data
	
	print("    nope, nothing found")
	return {}


func _extract_serial_from_path(path: String) -> String:
	# Matches patterns like SLUS-01234, SLES_50001, SCPS-45678
	var regex = RegEx.new()
	regex.compile("([A-Z]{4}[-_ ]\\d{5})")
	var result = regex.search(path.to_upper())
	if result:
		return result.get_string(1)
	return ""

func _handle_auto_download(rom_path: String, data: Dictionary):
	if not auto_download_covers:
		return

	# Download Cover
	if data.get("image_boxart", "") != "":
		var cover_url = BASE_URL + data["image_boxart"]
		var cover_path = _get_cover_path(rom_path)
		_queue_download(cover_url, cover_path, rom_path)
		
	# Download Icon
	if data.get("image_icon", "") != "":
		var icon_url = BASE_URL + data["image_icon"]
		var icon_path = _get_icon_path(rom_path)
		_queue_download(icon_url, icon_path, rom_path)

func _lookup_game_by_name(game_name: String, rom_path: String, console_id: int = -1) -> Dictionary:
	if console_id == -1:
		console_id = _get_console_id(rom_path)
	
	if not games_database.has(console_id):
		await _fetch_games_list(console_id)
	
	var match_result = _find_best_match(game_name, console_id)
	
	print("    → Best match: '", match_result.get("title", ""), "' (Score: ", match_result["score"], ")")
	
	if match_result["score"] >= min_match_score:
		# Need full details to include images
		var url = API_BASE + "/API_GetGame.php?z=" + username + "&y=" + api_key + "&i=" + str(match_result["id"])
		
		var request = HTTPRequest.new()
		add_child(request)
		request.request(url)
		var result = await request.request_completed
		request.queue_free()
		
		if result[1] == 200:
			var json = JSON.new()
			if json.parse(result[3].get_string_from_utf8()) == OK:
				var data = _process_game_data(json.data)
				data["score"] = match_result["score"]
				return data
	
	return {}

func _process_game_data(game_data: Dictionary) -> Dictionary:
	return {
		"id": game_data.get("ID", 0), # Compatibility with old code
		"game_id": game_data.get("ID", 0),
		"title": game_data.get("Title", ""),
		"console_name": game_data.get("ConsoleName", ""),
		"image_icon": game_data.get("ImageIcon", ""),
		"image_title": game_data.get("ImageTitle", ""),
		"image_ingame": game_data.get("ImageIngame", ""),
		"image_boxart": game_data.get("ImageBoxArt", ""),
		"publisher": game_data.get("Publisher", ""),
		"developer": game_data.get("Developer", ""),
		"genre": game_data.get("Genre", ""),
		"released": game_data.get("Released", ""),
		"found": true
	}

func _lookup_game(rom_path: String, console_hint: String):
	var game_name = _clean_rom_name(rom_path.get_file().get_basename())
	var console_id = _get_console_id(rom_path, console_hint)
	
	if not games_database.has(console_id):
		await _fetch_games_list(console_id)
	
	var match_result = _find_best_match(game_name, console_id)
	
	if match_result["score"] >= min_match_score:
		await _fetch_game_details(match_result["id"], rom_path)
	else:
		var empty_data = {
			"title": rom_path.get_file().get_basename(),
			"genre": "",
			"cover_path": "",
			"icon_path": "",
			"found": false
		}
		_cache_result(rom_path, empty_data)
		game_data_loaded.emit(rom_path, empty_data)

func _fetch_games_list(console_id: int):
	var url = API_BASE + "/API_GetGameList.php?z=" + username + "&y=" + api_key + "&i=" + str(console_id) + "&h=0&f=0"
	
	var request = HTTPRequest.new()
	add_child(request)
	request.request(url)
	var result = await request.request_completed
	request.queue_free()
	
	if result[1] != 200:
		return
	
	var json = JSON.new()
	var error = json.parse(result[3].get_string_from_utf8())
	
	if error != OK:
		return
	
	games_database[console_id] = []
	
	var games_data = json.data
	var games_array = []
	
	if games_data is Array:
		games_array = games_data
	elif games_data is Dictionary:
		games_array = games_data.values()
	
	for game in games_array:
		if game is Dictionary:
			games_database[console_id].append({
				"id": game.get("ID", 0),
				"title": game.get("Title", ""),
				"title_lower": game.get("Title", "").to_lower(),
				"title_normalized": _normalize_title(game.get("Title", ""))
			})

func _fetch_game_details(game_id: int, rom_path: String):
	var url = API_BASE + "/API_GetGame.php?z=" + username + "&y=" + api_key + "&i=" + str(game_id)
	
	var request = HTTPRequest.new()
	add_child(request)
	request.request(url)
	var result = await request.request_completed
	request.queue_free()
	
	if result[1] != 200:
		return
	
	var json = JSON.new()
	var error = json.parse(result[3].get_string_from_utf8())
	
	if error != OK:
		return
	
	var game_data = json.data
	
	var title = game_data.get("Title", "")
	var box_art = game_data.get("ImageBoxArt", "")
	var icon = game_data.get("ImageIcon", "")
	
	var data = {
		"title": title,
		"genre": game_data.get("Genre", ""),
		"developer": game_data.get("Developer", ""),
		"publisher": game_data.get("Publisher", ""),
		"released": game_data.get("Released", ""),
		"cover_url": BASE_URL + box_art if not box_art.is_empty() else "",
		"icon_url": BASE_URL + icon if not icon.is_empty() else "",
		"cover_path": "",
		"icon_path": "",
		"game_id": game_id,
		"found": true
	}
	
	print("retroachievements: ", lookup_count, "/", lookup_total, " - '", title, "'")
	
	if auto_download_covers and not box_art.is_empty():
		var cover_local_path = COVERS_FOLDER + "/" + str(game_id) + "_cover.png"
		if FileAccess.file_exists(cover_local_path):
			data["cover_path"] = cover_local_path
		else:
			download_queue.append({
				"type": "cover",
				"url": BASE_URL + box_art,
				"local_path": cover_local_path,
				"rom_path": rom_path,
				"field": "cover_path"
			})
	
	if auto_download_covers and not icon.is_empty():
		var icon_local_path = ICONS_FOLDER + "/" + str(game_id) + "_icon.png"
		if FileAccess.file_exists(icon_local_path):
			data["icon_path"] = icon_local_path
		else:
			download_queue.append({
				"type": "cover",
				"url": BASE_URL + icon,
				"local_path": icon_local_path,
				"rom_path": rom_path,
				"field": "icon_path"
			})
	
	_cache_result(rom_path, data)
	game_data_loaded.emit(rom_path, data)

func _download_image(url: String, local_path: String, rom_path: String):
	var downloader = _get_available_downloader()
	
	if downloader == null:
		download_queue.push_front({
			"type": "cover",
			"url": url,
			"local_path": local_path,
			"rom_path": rom_path
		})
		return
	
	active_downloads += 1
	
	downloader.request(url)
	var result = await downloader.request_completed
	
	active_downloads -= 1
	
	if result[1] != 200:
		return
	
	var absolute_path = local_path.replace("user://", OS.get_user_data_dir() + "/")
	
	var file = FileAccess.open(absolute_path, FileAccess.WRITE)
	if file:
		file.store_buffer(result[3])
		file.close()
		
		var cache_key = get_cache_key(rom_path)
		if cache.has(cache_key):
			if "cover" in local_path:
				cache[cache_key]["cover_path"] = local_path
			elif "icon" in local_path:
				cache[cache_key]["icon_path"] = local_path
			_save_cache()
		
		cover_downloaded.emit(rom_path, local_path)

func _get_available_downloader() -> HTTPRequest:
	for downloader in image_downloaders:
		if downloader.get_http_client_status() == HTTPClient.STATUS_DISCONNECTED:
			return downloader
	return null

func _cache_result(rom_path: String, data: Dictionary):
	var key = get_cache_key(rom_path)
	cache[key] = data
	cache[key]["rom_path"] = rom_path
	cache[key]["cached_at"] = Time.get_unix_time_from_system()
	_save_cache()

func _clean_rom_name(rom_name: String) -> String:
	var cleaned = rom_name
	
	if ", The" in cleaned:
		cleaned = "The " + cleaned.replace(", The", "")
	elif ", A" in cleaned:
		cleaned = "A " + cleaned.replace(", A", "")
	
	var patterns = [
		"\\([^)]*\\)",
		"\\[[^\\]]*\\]",
		"\\s+v\\d+\\.?\\d*",
		"\\s+rev\\s*\\d*",
		"\\s*\\(.*?\\)\\s*"
	]
	
	# Replace dashes with space first
	cleaned = cleaned.replace(" - ", " ")
	cleaned = cleaned.replace("-", " ")
	
	var regex = RegEx.new()
	for pattern in patterns:
		regex.compile(pattern)
		cleaned = regex.sub(cleaned, "", true)
	
	cleaned = cleaned.strip_edges()
	cleaned = cleaned.replace("_", " ")
	cleaned = cleaned.replace("  ", " ")
	
	return cleaned

func _normalize_title(title: String) -> String:
	var normalized = title.to_lower()
	
	normalized = normalized.replace(":", " ")
	normalized = normalized.replace("-", " ")
	normalized = normalized.replace("'", "")
	normalized = normalized.replace(".", " ")
	normalized = normalized.replace("!", " ")
	normalized = normalized.replace("?", " ")
	normalized = normalized.replace("&", "and")
	
	var regex = RegEx.new()
	regex.compile("\\s+")
	normalized = regex.sub(normalized, " ", true)
	
	return normalized.strip_edges()

func _get_console_id(rom_path: String, console_hint: String = "") -> int:
	if not console_hint.is_empty():
		var hint_lower = console_hint.to_lower()
		if console_id_map.has(hint_lower):
			return console_id_map[hint_lower]
		if hint_lower.is_valid_int():
			return hint_lower.to_int()
	
	var extension = rom_path.get_extension().to_lower()
	
	if console_id_map.has(extension):
		return console_id_map[extension]
	
	return 0

func _find_best_match(search_name: String, console_id: int) -> Dictionary:
	if not games_database.has(console_id):
		return {"id": 0, "score": 0.0, "title": ""}
	
	var search_normalized = _normalize_title(search_name)
	var search_lower = search_name.to_lower()
	
	var best_match = {"id": 0, "score": 0.0, "title": ""}
	
	var penalty_keywords = ["hack", "subset", "prototype", "beta", "demo", "kaizo", "bonus"]
	
	for game in games_database[console_id]:
		var score = 0.0
		var game_title_lower = game["title_lower"]
		var game_title_normalized = game["title_normalized"]
		
		if game_title_normalized == search_normalized:
			score = 1.0
		elif game_title_lower == search_lower:
			score = 0.98
		elif search_normalized in game_title_normalized:
			score = 0.85
		elif game_title_normalized in search_normalized:
			score = 0.80
		elif use_fuzzy_search:
			score = _calculate_similarity(search_normalized, game_title_normalized)
		
		for keyword in penalty_keywords:
			if keyword in game_title_lower and not keyword in search_lower:
				score *= 0.5
		
		if score > best_match["score"]:
			best_match = {
				"id": game["id"],
				"score": score,
				"title": game["title"]
			}
		
		if score >= 0.99:
			break
	
	return best_match

func _calculate_similarity(string1: String, string2: String) -> float:
	if string1.is_empty() or string2.is_empty():
		return 0.0
	
	# Tokenize and unique
	var words1 = _get_unique_words(string1)
	var words2 = _get_unique_words(string2)
	
	# Calculate Intersection
	var intersection_count = 0.0
	
	# Create a copy of words2 to track usage (avoid matching same word twice)
	var available_words2 = words2.duplicate()
	
	for w1 in words1:
		var best_match_idx = -1
		var best_match_score = 0.0
		
		for i in range(available_words2.size()):
			var w2 = available_words2[i]
			var score = 0.0
			
			if w1 == w2:
				score = 1.0
			elif w1.length() > 3 and w2.length() > 3:
				# Levenshtein distance or inclusion for minor typos
				if w1 in w2 or w2 in w1:
					score = 0.8
				# Handle plurals lightly
				elif w1 + "s" == w2 or w2 + "s" == w1:
					score = 0.95
					
			if score > best_match_score:
				best_match_score = score
				best_match_idx = i
		
		if best_match_idx != -1:
			intersection_count += best_match_score
			available_words2.remove_at(best_match_idx)
			
	# Jaccard Index = Intersection / Union
	# Union = Size1 + Size2 - Intersection
	var union_count = words1.size() + words2.size() - intersection_count
	
	if union_count == 0: return 0.0
	
	return intersection_count / union_count

func _get_unique_words(text: String) -> Array:
	var words = text.to_lower().replace("&", "and").replace("+", " ").split(" ", false)
	var unique = []
	for w in words:
		if w.length() < 2 and w != "3" and w != "2" and w != "i" and w != "v": continue # Skip small junk unless numbers
		if not w in unique:
			unique.append(w)
	return unique

func scan_emulator_rooms(emulator_node: Control):
	if not emulator_node:
		return
	
	if not "rooms_folder" in emulator_node:
		return
	
	var folder = emulator_node.rooms_folder
	var extensions = emulator_node.roms_extensions if "roms_extensions" in emulator_node else []
	var console_hint = extensions[0] if not extensions.is_empty() else ""
	
	if emulator_node.has_meta("core_id"):
		var core_id = emulator_node.get_meta("core_id")
		if core_to_console_map.has(core_id):
			console_hint = str(core_to_console_map[core_id])
	
	if folder.is_empty():
		return
	
	if not DirAccess.dir_exists_absolute(folder):
		return
	
	var dir = DirAccess.open(folder)
	if not dir:
		return
	
	var rom_paths = []
	
	dir.list_dir_begin()
	var file_name = dir.get_next()
	
	while file_name != "":
		if not dir.current_is_dir():
			var ext = file_name.get_extension().to_lower()
			if extensions.is_empty() or ext in extensions:
				rom_paths.append(folder.path_join(file_name))
		file_name = dir.get_next()
	
	dir.list_dir_end()
	
	scan_roms_by_hash(rom_paths)

func scan_roms_by_hash(rom_paths: Array):
	print("=== ROM Hash Scan Started ===")
	print("scanning ", rom_paths.size(), " roms...")
	
	lookup_total = rom_paths.size()
	lookup_count = 0
	
	for rom_path in rom_paths:
		lookup_count += 1
		queue_progress.emit(lookup_count, lookup_total)
		
		print("\n[", lookup_count, "/", lookup_total, "] Processing: ", rom_path.get_file())
		
		if has_cached_data(rom_path):
			print("  ✓ Already cached, skipping")
			continue
		
		# print("  calculando...")
		var game_data = await identify_rom_by_hash(rom_path)
		
		if game_data.get("found", false):
			print("  + ", game_data.get("title", "Unknown"))
			# print("  capa: ", game_data.get("image_boxart", "None"))
			_cache_result(rom_path, game_data)
			game_data_loaded.emit(rom_path, game_data)
		else:
			print("  - not found on RA")
		
		await get_tree().create_timer(request_delay).timeout
	
	print("\n=== ROM Hash Scan Complete ===")
	queue_finished.emit()

func clear_cache():
	cache.clear()
	_save_cache()

func get_stats() -> Dictionary:
	return {
		"cached_games": cache.size(),
		"queue_size": download_queue.size(),
		"active_downloads": active_downloads,
		"is_processing": is_processing_queue
	}

func _get_cover_path(rom_path: String) -> String:
	var rom_name = rom_path.get_file().get_basename()
	var safe_name = rom_name.replace(" ", "_").replace("(", "").replace(")", "")
	return COVERS_FOLDER + "/" + safe_name + "_cover.png"

func _get_icon_path(rom_path: String) -> String:
	var rom_name = rom_path.get_file().get_basename()
	var safe_name = rom_name.replace(" ", "_").replace("(", "").replace(")", "")
	return ICONS_FOLDER + "/" + safe_name + "_icon.png"

func _queue_download(url: String, local_path: String, rom_path: String):
	download_queue.append({
		"type": "cover",
		"url": url,
		"local_path": local_path,
		"rom_path": rom_path
	})
	
	if not is_processing_queue:
		_process_queue()
