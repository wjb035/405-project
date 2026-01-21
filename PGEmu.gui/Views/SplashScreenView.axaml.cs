using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using System;

namespace PGEmu.gui.Views;

public partial class SplashScreenView : UserControl
{
    
    private LibVLC MainLibVLC { get; set; }
    private MediaPlayer MainMediaPlayer { get; set; }


    public SplashScreenView()
    {
        InitializeComponent();
        PlayAudio();
    }

    public void PlayAudio()
    {
        MainLibVLC = new(enableDebugLogs: true);
        MainMediaPlayer = new(MainLibVLC);
        
        Media media = new(MainLibVLC, "Assets/SouljaBoy.mp3");
        MainMediaPlayer.Media = media;
        MainMediaPlayer.Play();
    }


}