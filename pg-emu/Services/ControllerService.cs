using Godot;
using System;
using System.Collections.Generic;

namespace PGEmu.Services;

public enum ControllerFamily
{
	KeyboardMouse,
	Xbox,
	PlayStation,
	Nintendo,
	GameCube,
	Generic,
}

// Tracks connected controllers and exposes consistent labels for controller-specific UI prompts.
public partial class ControllerService : Node
{
	public const float MenuAxisDeadzone = 0.55f;
	public const int MenuAxisInitialRepeatMs = 220;
	public const int MenuAxisRepeatMs = 140;

	public static ControllerService? Instance { get; private set; }

	public event Action? ActiveControllerChanged;

	private readonly List<int> _connectedDevices = new();
	private int _activeDeviceId = -1;

	public int ActiveDeviceId => _activeDeviceId;
	public IReadOnlyList<int> ConnectedDevices => _connectedDevices;
	public bool HasActiveController => _activeDeviceId >= 0;

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		EnsureControllerUiBindings();
		Input.JoyConnectionChanged += OnJoyConnectionChanged;
		RefreshConnectedDevices();
	}

	public override void _ExitTree()
	{
		Input.JoyConnectionChanged -= OnJoyConnectionChanged;

		if (Instance == this)
			Instance = null;
	}

	public bool ShouldHandleMenuInput(int device)
	{
		if (InputRoutingService.Instance?.IsUiInputBlocked == true)
			return false;

		if (device < 0)
			return true;

		if (_activeDeviceId != device)
			SetActiveDevice(device);

		return true;
	}

	public void SetActiveDevice(int device)
	{
		if (device < 0)
			return;

		if (_activeDeviceId == device)
			return;

		_activeDeviceId = device;
		ActiveControllerChanged?.Invoke();
	}

	public ControllerFamily GetActiveControllerFamily()
	{
		if (_activeDeviceId < 0)
			return ControllerFamily.KeyboardMouse;

		return DetectFamily(Input.GetJoyName(_activeDeviceId), Input.GetJoyGuid(_activeDeviceId));
	}

	public string GetConfirmLabel()
	{
		return GetActiveControllerFamily() switch
		{
			ControllerFamily.PlayStation => "X",
			ControllerFamily.Nintendo => "B",
			ControllerFamily.KeyboardMouse => "Enter",
			_ => "A",
		};
	}

	public string GetBackLabel()
	{
		return GetActiveControllerFamily() switch
		{
			ControllerFamily.PlayStation => "O",
			ControllerFamily.Nintendo => "A",
			ControllerFamily.KeyboardMouse => "Esc",
			_ => "B",
		};
	}

	public string GetControllerDisplayName()
	{
		if (_activeDeviceId < 0)
			return "Keyboard/Mouse";

		var name = Input.GetJoyName(_activeDeviceId);
		return string.IsNullOrWhiteSpace(name) ? $"Controller {_activeDeviceId}" : name;
	}

	public static bool IsConfirmButton(JoyButton button)
	{
		return button == JoyButton.A;
	}

	public static bool IsBackButton(JoyButton button)
	{
		return button == JoyButton.B;
	}

	public static void ResetMenuAxis(ref int heldDir, ref long nextMs)
	{
		heldDir = 0;
		nextMs = 0;
	}

	public static bool TryHandleMenuAxis(float value, ref int heldDir, ref long nextMs, Action<int> stepAction)
	{
		var dir = 0;
		if (value <= -MenuAxisDeadzone)
			dir = -1;
		else if (value >= MenuAxisDeadzone)
			dir = 1;

		if (dir == 0)
		{
			ResetMenuAxis(ref heldDir, ref nextMs);
			return false;
		}

		var now = (long)Time.GetTicksMsec();
		if (dir != heldDir)
		{
			stepAction(dir);
			heldDir = dir;
			nextMs = now + MenuAxisInitialRepeatMs;
			return true;
		}

		if (now < nextMs)
			return false;

		stepAction(dir);
		nextMs = now + MenuAxisRepeatMs;
		return true;
	}

	public static void PrepareFocusable(Control? control)
	{
		if (control == null)
			return;

		control.FocusMode = Control.FocusModeEnum.All;
	}

	public static List<List<Button>> BuildVisibleRows(params IEnumerable<Button?>[] rowSources)
	{
		var rows = new List<List<Button>>();

		foreach (var rowSource in rowSources)
		{
			var row = new List<Button>();
			foreach (var button in rowSource)
			{
				if (button == null || !GodotObject.IsInstanceValid(button))
					continue;
				if (!button.Visible || button.Disabled)
					continue;

				PrepareFocusable(button);
				row.Add(button);
			}

			if (row.Count > 0)
				rows.Add(row);
		}

		return rows;
	}

	public static bool FocusRowEntry(IReadOnlyList<List<Button>> rows, ref int rowIndex, ref int columnIndex)
	{
		if (rows.Count == 0)
			return false;

		rowIndex = WrapIndex(rowIndex, rows.Count);
		var row = rows[rowIndex];
		if (row.Count == 0)
			return false;

		columnIndex = WrapIndex(columnIndex, row.Count);
		row[columnIndex].GrabFocus();
		return true;
	}

	public static bool MoveRowSelection(IReadOnlyList<List<Button>> rows, ref int rowIndex, ref int columnIndex, int rowDelta, int columnDelta)
	{
		if (rows.Count == 0)
			return false;

		if (rowIndex < 0 || rowIndex >= rows.Count)
		{
			rowIndex = 0;
			columnIndex = 0;
			return FocusRowEntry(rows, ref rowIndex, ref columnIndex);
		}

		if (rowDelta != 0)
		{
			float? sourceCenterX = null;
			var currentRow = rows[rowIndex];
			if (columnIndex >= 0 && columnIndex < currentRow.Count)
				sourceCenterX = GetControlCenter(currentRow[columnIndex]).X;

			rowIndex = WrapIndex(rowIndex + rowDelta, rows.Count);
			columnIndex = sourceCenterX.HasValue
				? FindNearestColumnByX(rows[rowIndex], sourceCenterX.Value)
				: Mathf.Clamp(columnIndex, 0, rows[rowIndex].Count - 1);
		}

		if (columnDelta != 0)
			columnIndex = WrapIndex(columnIndex + columnDelta, rows[rowIndex].Count);

		return FocusRowEntry(rows, ref rowIndex, ref columnIndex);
	}

	private static int FindNearestColumnByX(IReadOnlyList<Button> row, float sourceCenterX)
	{
		if (row.Count == 0)
			return 0;

		var nearestIndex = 0;
		var nearestDistance = float.MaxValue;

		for (int i = 0; i < row.Count; i++)
		{
			var distance = Mathf.Abs(GetControlCenter(row[i]).X - sourceCenterX);
			if (distance >= nearestDistance)
				continue;

			nearestDistance = distance;
			nearestIndex = i;
		}

		return nearestIndex;
	}

	private static Vector2 GetControlCenter(Control control)
	{
		var rect = control.GetGlobalRect();
		return rect.Position + (rect.Size * 0.5f);
	}

	public static bool ActivateRowSelection(IReadOnlyList<List<Button>> rows, int rowIndex, int columnIndex)
	{
		if (rows.Count == 0)
			return false;
		if (rowIndex < 0 || rowIndex >= rows.Count)
			return false;

		var row = rows[rowIndex];
		if (columnIndex < 0 || columnIndex >= row.Count)
			return false;

		var button = row[columnIndex];
		if (button.HasFocus())
			return true;

		button.EmitSignal(Button.SignalName.Pressed);
		return true;
	}

	public static bool TryHandleBackAction(InputEvent @event, Action backAction)
	{
		if (@event is not InputEventJoypadButton joypadButton)
			return false;
		if (!joypadButton.Pressed || !IsBackButton(joypadButton.ButtonIndex))
			return false;
		if (Instance?.ShouldHandleMenuInput(joypadButton.Device) == false)
			return false;

		backAction();
		return true;
	}

	private void OnJoyConnectionChanged(long device, bool connected)
	{
		var priorDevice = _activeDeviceId;
		RefreshConnectedDevices();

		if (connected && _activeDeviceId < 0)
			_activeDeviceId = (int)device;

		if (!connected && priorDevice == (int)device)
			_activeDeviceId = _connectedDevices.Count > 0 ? _connectedDevices[0] : -1;

		if (priorDevice != _activeDeviceId)
			ActiveControllerChanged?.Invoke();
	}

	private void RefreshConnectedDevices()
	{
		_connectedDevices.Clear();
		foreach (var device in Input.GetConnectedJoypads())
			_connectedDevices.Add(device);

		_connectedDevices.Sort();

		if (_activeDeviceId >= 0 && !_connectedDevices.Contains(_activeDeviceId))
			_activeDeviceId = -1;

		if (_activeDeviceId < 0 && _connectedDevices.Count > 0)
			_activeDeviceId = _connectedDevices[0];
	}

	private static int WrapIndex(int value, int count)
	{
		if (count <= 0)
			return 0;

		value %= count;
		if (value < 0)
			value += count;
		return value;
	}

	private static void EnsureControllerUiBindings()
	{
		EnsureActionExists("ui_accept");
		EnsureActionExists("ui_cancel");
		EnsureActionExists("ui_left");
		EnsureActionExists("ui_right");
		EnsureActionExists("ui_up");
		EnsureActionExists("ui_down");

		EnsureActionHasJoyButton("ui_accept", JoyButton.A);
		EnsureActionHasJoyButton("ui_cancel", JoyButton.B);
		EnsureActionHasJoyButton("ui_left", JoyButton.DpadLeft);
		EnsureActionHasJoyButton("ui_right", JoyButton.DpadRight);
		EnsureActionHasJoyButton("ui_up", JoyButton.DpadUp);
		EnsureActionHasJoyButton("ui_down", JoyButton.DpadDown);

		EnsureActionHasJoyMotion("ui_left", JoyAxis.LeftX, -1.0f);
		EnsureActionHasJoyMotion("ui_right", JoyAxis.LeftX, 1.0f);
		EnsureActionHasJoyMotion("ui_up", JoyAxis.LeftY, -1.0f);
		EnsureActionHasJoyMotion("ui_down", JoyAxis.LeftY, 1.0f);
	}

	private static void EnsureActionExists(string actionName)
	{
		if (!InputMap.HasAction(actionName))
			InputMap.AddAction(actionName, 0.2f);
	}

	private static void EnsureActionHasJoyButton(string actionName, JoyButton button)
	{
		foreach (var existingEvent in InputMap.ActionGetEvents(actionName))
		{
			if (existingEvent is InputEventJoypadButton joypadButton &&
				joypadButton.ButtonIndex == button)
			{
				return;
			}
		}

		InputMap.ActionAddEvent(actionName, new InputEventJoypadButton { ButtonIndex = button });
	}

	private static void EnsureActionHasJoyMotion(string actionName, JoyAxis axis, float axisValue)
	{
		foreach (var existingEvent in InputMap.ActionGetEvents(actionName))
		{
			if (existingEvent is not InputEventJoypadMotion motion)
				continue;
			if (motion.Axis != axis)
				continue;
			if (Math.Sign(motion.AxisValue) != Math.Sign(axisValue))
				continue;

			return;
		}

		InputMap.ActionAddEvent(actionName, new InputEventJoypadMotion
		{
			Axis = axis,
			AxisValue = axisValue,
		});
	}

	private static ControllerFamily DetectFamily(string joyName, string joyGuid)
	{
		var text = $"{joyName} {joyGuid}".ToLowerInvariant();

		if (text.Contains("xbox"))
			return ControllerFamily.Xbox;

		if (text.Contains("dualsense") ||
			text.Contains("dualshock") ||
			text.Contains("playstation") ||
			text.Contains("ps4") ||
			text.Contains("ps5") ||
			text.Contains("wireless controller"))
		{
			return ControllerFamily.PlayStation;
		}

		if (text.Contains("gamecube") || text.Contains("mayflash"))
			return ControllerFamily.GameCube;

		if (text.Contains("joy-con") ||
			text.Contains("nintendo") ||
			text.Contains("switch") ||
			text.Contains("pro controller"))
		{
			return ControllerFamily.Nintendo;
		}

		return ControllerFamily.Generic;
	}
}
