using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using LibVLCSharp.Shared;

namespace PGEmu.gui.Views;

public partial class SplashScreenView : UserControl
{
    // Optional, only set if VLC successfully initializes
    public LibVLC? MainLibVLC { get; private set; }
    public MediaPlayer? MainMediaPlayer { get; private set; }

    public SplashScreenView()
    {
        InitializeComponent();
        TryPlayAudio();
    }

    private void TryPlayAudio()
    {
        // Your crash path shows osx-x64 libs, but you are on osx-arm64.
        // Skip VLC so the splash does not crash the whole app.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            Console.WriteLine("[SplashScreenView] Skipping LibVLC on osx-arm64.");
            return;
        }

        try
        {
            PlayAudio();
        }
        catch (VLCException ex)
        {
            Console.WriteLine($"[SplashScreenView] VLC native libs not available: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SplashScreenView] Failed to start splash audio: {ex}");
        }
    }

    // Exists so CS0103 goes away. You can later fill this in with actual audio playback.
    private void PlayAudio()
    {
        Core.Initialize();

        MainLibVLC = new LibVLC(enableDebugLogs: false);
        MainMediaPlayer = new MediaPlayer(MainLibVLC);

        // No-op for now. If you already have a Media(...) + Play() block,
        // paste it here.
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        MainMediaPlayer?.Dispose();
        MainLibVLC?.Dispose();
    }
}