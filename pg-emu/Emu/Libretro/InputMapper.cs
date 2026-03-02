using Godot;
using System.Collections.Generic;

public partial class InputMapper : Node
{
	// Maps core IDs (gba, snes, ps1...) to per-button action suffixes.
	private static Dictionary<string, Dictionary<LibretroInput, string>> _coreMappings = new Dictionary<string, Dictionary<LibretroInput, string>>();
	// Generic mapping used when a core does not have a custom table.
	private static Dictionary<LibretroInput, string> _genericMapping = new Dictionary<LibretroInput, string>();
	// Tracks which cores already had default actions seeded this run.
	private static HashSet<string> _initializedActionSets = new HashSet<string>();

	public const int LAYOUT_XBOX = 0;
	public const int LAYOUT_PLAYSTATION = 1;

	public void set_layout_type(int type)
	{
		GD.Print($"InputMapper: Set layout to {type} (Not implemented yet)");
	}
	
	static InputMapper()
	{
		InitializeGenericMapping();
		InitializeCoreMappings();
	}
	
	private static void InitializeGenericMapping()
	{
		_genericMapping = new Dictionary<LibretroInput, string>
		{
			{ LibretroInput.A, "a" },
			{ LibretroInput.B, "b" },
			{ LibretroInput.X, "x" },
			{ LibretroInput.Y, "y" },
			{ LibretroInput.START, "start" },
			{ LibretroInput.SELECT, "select" },
			{ LibretroInput.UP, "up" },
			{ LibretroInput.DOWN, "down" },
			{ LibretroInput.LEFT, "left" },
			{ LibretroInput.RIGHT, "right" },
			{ LibretroInput.L, "l" },
			{ LibretroInput.R, "r" },
			{ LibretroInput.L2, "l2" },
			{ LibretroInput.R2, "r2" },
			{ LibretroInput.L3, "l3" },
			{ LibretroInput.R3, "r3" }
		};
	}
	
	private static void InitializeCoreMappings()
	{
		_coreMappings["gba"] = new Dictionary<LibretroInput, string>
		{
			{ LibretroInput.A, "a" },
			{ LibretroInput.B, "b" },
			{ LibretroInput.START, "start" },
			{ LibretroInput.SELECT, "select" },
			{ LibretroInput.UP, "up" },
			{ LibretroInput.DOWN, "down" },
			{ LibretroInput.LEFT, "left" },
			{ LibretroInput.RIGHT, "right" },
			{ LibretroInput.L, "l" },
			{ LibretroInput.R, "r" }
		};
		
		_coreMappings["gb"] = _coreMappings["gba"];
		_coreMappings["gbc"] = _coreMappings["gba"];
		
		_coreMappings["snes"] = new Dictionary<LibretroInput, string>
		{
			{ LibretroInput.A, "a" },
			{ LibretroInput.B, "b" },
			{ LibretroInput.X, "x" },
			{ LibretroInput.Y, "y" },
			{ LibretroInput.START, "start" },
			{ LibretroInput.SELECT, "select" },
			{ LibretroInput.UP, "up" },
			{ LibretroInput.DOWN, "down" },
			{ LibretroInput.LEFT, "left" },
			{ LibretroInput.RIGHT, "right" },
			{ LibretroInput.L, "l" },
			{ LibretroInput.R, "r" }
		};
		
		_coreMappings["nes"] = new Dictionary<LibretroInput, string>
		{
			{ LibretroInput.A, "a" },
			{ LibretroInput.B, "b" },
			{ LibretroInput.START, "start" },
			{ LibretroInput.SELECT, "select" },
			{ LibretroInput.UP, "up" },
			{ LibretroInput.DOWN, "down" },
			{ LibretroInput.LEFT, "left" },
			{ LibretroInput.RIGHT, "right" }
		};
		
		_coreMappings["megadrive"] = new Dictionary<LibretroInput, string>
		{
			{ LibretroInput.A, "b" },
			{ LibretroInput.B, "a" },
			{ LibretroInput.X, "y" },
			{ LibretroInput.Y, "x" },
			{ LibretroInput.L, "z" },
			{ LibretroInput.R, "c" },
			{ LibretroInput.START, "start" },
			{ LibretroInput.UP, "up" },
			{ LibretroInput.DOWN, "down" },
			{ LibretroInput.LEFT, "left" },
			{ LibretroInput.RIGHT, "right" }
		};
		
		_coreMappings["picodrive"] = _coreMappings["megadrive"];
		_coreMappings["genesis_plus_gx"] = _coreMappings["megadrive"];
		
		_coreMappings["ps1"] = new Dictionary<LibretroInput, string>
		{
			{ LibretroInput.A, "cross" },
			{ LibretroInput.B, "circle" },
			{ LibretroInput.X, "square" },
			{ LibretroInput.Y, "triangle" },
			{ LibretroInput.START, "start" },
			{ LibretroInput.SELECT, "select" },
			{ LibretroInput.UP, "up" },
			{ LibretroInput.DOWN, "down" },
			{ LibretroInput.LEFT, "left" },
			{ LibretroInput.RIGHT, "right" },
			{ LibretroInput.L, "l1" },
			{ LibretroInput.R, "r1" },
			{ LibretroInput.L2, "l2" },
			{ LibretroInput.R2, "r2" },
			{ LibretroInput.L3, "l3" },
			{ LibretroInput.R3, "r3" }
		};
		
		_coreMappings["pcsx_rearmed"] = _coreMappings["ps1"];
		_coreMappings["swanstation"] = _coreMappings["ps1"];
		_coreMappings["mednafen_psx"] = _coreMappings["ps1"];
		_coreMappings["mednafen_psx_hw"] = _coreMappings["ps1"];

		// PPSSPP uses PSP face-button semantics but still maps through RETRO_DEVICE_JOYPAD ids.
		_coreMappings["psp"] = new Dictionary<LibretroInput, string>
		{
			{ LibretroInput.A, "cross" },
			{ LibretroInput.B, "circle" },
			{ LibretroInput.X, "square" },
			{ LibretroInput.Y, "triangle" },
			{ LibretroInput.START, "start" },
			{ LibretroInput.SELECT, "select" },
			{ LibretroInput.UP, "up" },
			{ LibretroInput.DOWN, "down" },
			{ LibretroInput.LEFT, "left" },
			{ LibretroInput.RIGHT, "right" },
			{ LibretroInput.L, "l" },
			{ LibretroInput.R, "r" }
		};
	}
	
	private static string NormalizeCoreId(string coreId)
	{
		// Alias multiple core names to one logical input profile.
		return coreId switch
		{
			"pcsx_rearmed" => "ps1",
			"swanstation" => "ps1",
			"mednafen_psx" => "ps1",
			"mednafen_psx_hw" => "ps1",
			"mgba" => "gba",
			"vbam" => "gba",
			"snes9x" => "snes",
			"fceumm" => "nes",
			"gambatte" => "gb",
			"picodrive" => "megadrive",
			"genesis_plus_gx" => "megadrive",
			"ppsspp" => "psp",
			_ => coreId
		};
	}

	public static void EnsureDefaultActions(string coreId)
	{
		string normalizedId = NormalizeCoreId(coreId);
		if (string.IsNullOrWhiteSpace(normalizedId))
			return;

		if (_initializedActionSets.Contains(normalizedId))
			return;

		var mapping = _coreMappings.TryGetValue(normalizedId, out var coreMapping)
			? coreMapping
			: _genericMapping;

		// Build Godot InputMap actions like "gba_a", "gba_start", etc.
		foreach (var pair in mapping)
		{
			string actionName = $"{normalizedId}_{pair.Value}";
			EnsureActionExists(actionName);

			// Keep user-customized bindings intact; only seed empty actions.
			if (InputMap.ActionGetEvents(actionName).Count > 0)
				continue;

			// Seed CATui-style default pad bindings so cores are playable out of the box.
			foreach (var ev in BuildDefaultEvents(pair.Key))
			{
				InputMap.ActionAddEvent(actionName, ev);
			}
		}

		_initializedActionSets.Add(normalizedId);
	}
	
	public static string GetActionName(string coreId, LibretroInput button)
	{
		string normalizedId = NormalizeCoreId(coreId);
		
		if (_coreMappings.TryGetValue(normalizedId, out var mapping))
		{
			if (mapping.TryGetValue(button, out var buttonName))
			{
				return $"{normalizedId}_{buttonName}";
			}
		}
		
		if (_genericMapping.TryGetValue(button, out var genericButton))
		{
			return $"{normalizedId}_{genericButton}";
		}
		
		return "";
	}
	
	public static void RegisterCoreMapping(string coreId, Dictionary<LibretroInput, string> mapping)
	{
		_coreMappings[coreId] = mapping;
	}
	
	public static bool HasMapping(string coreId)
	{
		return _coreMappings.ContainsKey(coreId);
	}

	private static void EnsureActionExists(string actionName)
	{
		if (!InputMap.HasAction(actionName))
			InputMap.AddAction(actionName, 0.2f);
	}

	private static IEnumerable<InputEvent> BuildDefaultEvents(LibretroInput input)
	{
		// Defaults mirror CATui's logical gamepad mapping.
		switch (input)
		{
			case LibretroInput.A:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.A };
				break;
			case LibretroInput.B:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.B };
				break;
			case LibretroInput.X:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.X };
				break;
			case LibretroInput.Y:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.Y };
				break;
			case LibretroInput.START:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.Start };
				break;
			case LibretroInput.SELECT:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.Back };
				break;
			case LibretroInput.UP:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.DpadUp };
				break;
			case LibretroInput.DOWN:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown };
				break;
			case LibretroInput.LEFT:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.DpadLeft };
				break;
			case LibretroInput.RIGHT:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.DpadRight };
				break;
			case LibretroInput.L:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.LeftShoulder };
				break;
			case LibretroInput.R:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder };
				break;
			case LibretroInput.L2:
				yield return new InputEventJoypadMotion { Axis = JoyAxis.TriggerLeft, AxisValue = 1.0f };
				break;
			case LibretroInput.R2:
				yield return new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1.0f };
				break;
			case LibretroInput.L3:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.LeftStick };
				break;
			case LibretroInput.R3:
				yield return new InputEventJoypadButton { ButtonIndex = JoyButton.RightStick };
				break;
		}
	}
}
