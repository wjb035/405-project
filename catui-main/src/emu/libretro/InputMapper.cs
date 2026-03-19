using Godot;
using System.Collections.Generic;

public partial class InputMapper : Node
{
	private static Dictionary<string, Dictionary<LibretroInput, string>> _coreMappings = new Dictionary<string, Dictionary<LibretroInput, string>>();
	private static Dictionary<LibretroInput, string> _genericMapping = new Dictionary<LibretroInput, string>();

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
	}
	
	private static string NormalizeCoreId(string coreId)
	{
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
			_ => coreId
		};
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
}
