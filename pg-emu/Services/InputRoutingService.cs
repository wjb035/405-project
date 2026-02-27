using Godot;
using System;

namespace PGEmu.Services;

// Central place for temporarily blocking launcher UI input while an emulator window is active.
public partial class InputRoutingService : Node
{
    public static InputRoutingService? Instance { get; private set; }

    public bool IsUiInputBlocked { get; private set; }

    private bool _waitForFocusReturn;

    public event Action<bool>? UiInputBlockChanged;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void LockUiInputForExternalLaunch(bool minimizeWindow = true)
    {
        SetBlocked(true);
        _waitForFocusReturn = true;

        if (!minimizeWindow)
            return;

        // Push the launcher into the background so the emulator can own focus/input.
        var window = GetWindow();
        if (window != null && window.Mode != Window.ModeEnum.Minimized)
            window.Mode = Window.ModeEnum.Minimized;
    }

    public void UnlockUiInput()
    {
        _waitForFocusReturn = false;
        SetBlocked(false);
    }

    public override void _Process(double delta)
    {
        if (!_waitForFocusReturn)
            return;

        if (DisplayServer.WindowIsFocused())
            UnlockUiInput();
    }

    private void SetBlocked(bool blocked)
    {
        if (IsUiInputBlocked == blocked)
            return;

        IsUiInputBlocked = blocked;
        UiInputBlockChanged?.Invoke(blocked);
    }
}
