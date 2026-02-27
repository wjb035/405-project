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
