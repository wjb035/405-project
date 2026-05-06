using Godot;
using System;

namespace PGEmu.Services;

// Central place for temporarily blocking launcher UI input while an emulator window is active.
public partial class InputRoutingService : Node
{
    public static InputRoutingService? Instance { get; private set; }

    public bool IsUiInputBlocked { get; private set; }

    private bool _minimizedForExternalLaunch;

    /// <summary>Transparent full-window layer so mouse / focus never reach game UI below.</summary>
    private CanvasLayer? _inputShieldLayer;
    private ColorRect? _inputShield;

    public event Action<bool>? UiInputBlockChanged;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        SetProcessInput(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void LockUiInputForExternalLaunch(bool minimizeWindow = true)
    {
        SetBlocked(true);

        if (!minimizeWindow)
        {
            _minimizedForExternalLaunch = false;
            return;
        }

        var window = GetWindow();
        if (window != null && window.Mode != Window.ModeEnum.Minimized)
        {
            window.Mode = Window.ModeEnum.Minimized;
            _minimizedForExternalLaunch = true;
        }
        else
        {
            _minimizedForExternalLaunch = false;
        }
    }

    public void UnlockUiInput()
    {
        if (_minimizedForExternalLaunch)
        {
            _minimizedForExternalLaunch = false;
            var window = GetWindow();
            // Bring the launcher back the same way the project is configured (fullscreen),
            // instead of leaving it windowed after un-minimize.
            if (window != null && window.Mode == Window.ModeEnum.Minimized)
                window.Mode = Window.ModeEnum.Fullscreen;
        }

        SetBlocked(false);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsUiInputBlocked)
            return;

        GetViewport()?.SetInputAsHandled();
    }

    private void EnsureInputShield()
    {
        if (_inputShieldLayer != null)
            return;

        _inputShieldLayer = new CanvasLayer
        {
            Layer = 120,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(_inputShieldLayer);

        _inputShield = new ColorRect
        {
            Color = Colors.Transparent,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
        };
        _inputShield.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _inputShield.OffsetLeft = 0;
        _inputShield.OffsetTop = 0;
        _inputShield.OffsetRight = 0;
        _inputShield.OffsetBottom = 0;
        _inputShieldLayer.AddChild(_inputShield);
    }

    private void SetInputShieldVisible(bool visible)
    {
        if (!visible)
        {
            if (_inputShieldLayer != null)
                _inputShieldLayer.Visible = false;
            return;
        }

        EnsureInputShield();
        _inputShieldLayer!.Visible = true;
        _inputShield!.CallDeferred("grab_focus");
    }

    private void SetBlocked(bool blocked)
    {
        if (IsUiInputBlocked == blocked)
            return;

        IsUiInputBlocked = blocked;
        SetInputShieldVisible(blocked);
        UiInputBlockChanged?.Invoke(blocked);
    }
}
