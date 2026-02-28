using Godot;
public partial class AudioManager : Node
{
	private AudioStreamPlayer _sfxPlayer;
	public static AudioManager Instance { get; private set; }

	public override void _Ready()
	{
		Instance = this;
		_sfxPlayer = GetNode<AudioStreamPlayer>("Sfx");
	}

	public void PlaySfx(string path)
	{
		var stream = GD.Load<AudioStream>(path);
		if (stream != null)
		{
			_sfxPlayer.Stream = stream;
			_sfxPlayer.Play();
			GD.Print("play");
		}
		else
		{
			GD.PrintErr("SFX not found: " + path);
		}
	}
}
