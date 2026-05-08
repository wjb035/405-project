using Godot;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using PGEmu.app;
using PGEmu.Services;
using PGEmu.GameSelect;
using PGEmu.Helpers;
using PGEmu.UI;
using PGEmu;

namespace PGEmu;

public partial class CartReader : PopupPanel
{
	[Export] private NodePath ConsolePrompt;
	[Export] private NodePath GameBoyChoicePath;
	[Export] private NodePath N64ChoicePath;
	[Export] private NodePath NESChoicePath;
	[Export] private NodePath SNESChoicePath;
	[Export] private NodePath CancelChoicePath;
	
	[Export] private NodePath GameFoundMessagePath;
	
	[Export] private NodePath LoadingMessagePath;
	
	[Export] private NodePath InstallMessagePath;
	[Export] private NodePath CancelInstallPath;
	[Export] private NodePath InstallButtonPath;
	[Export] private NodePath InstallingLabelPath;
	[Export] private NodePath ProgressBarPath;
	[Export] private NodePath ProgressBarContainerPath;
	
	[Export] private NodePath NowInstalledButtonPath;
	
	private VBoxContainer _consolePrompt;
	private Button _gameBoySelection;
	private Button _n64Selection;
	private Button _nesSelection;
	private Button _snesSelection;
	private Button _cancelSelection;
	
	private VBoxContainer _gameFoundMessage;
	
	private VBoxContainer _loadingMessage;
	
	private VBoxContainer _installMessage;
	private Button _installButton;
	private Button _cancelInstallButton;
	private Label _installingLabel;
	private ProgressBar _progressBar;
	private MarginContainer _progressBarContainer;
	
	private VBoxContainer _nowInstalledMessage;
	
	public ScreenTransition Transition;

	//private CartReaderHelper _cartReaderHelper = new CartReaderHelper();
	//private Thread _cartReaderFinder = null;
	//private bool _running = true; 
	//public SerialPort port;
	
	private const int GAMEBOYNUM = 1;
	private const int N64NUM = 3;
	private const int SNESNUM = 4;
	private const int NESNUM = 5;
	
	private Thread _cartReaderFinder;
	private SerialPort port = null;
	private bool _running = true; 
	private List<byte> fileBuffer = new List<byte>();
	
	public string fileName = null;
	public int fileSize = -1;
	public byte[] fileContent = null;
	bool readingRom = false;
	private int platformNum = -1;
	private string platformPath;
	private int totalBytesRead = 0;
	//private FileStream stream; 
	
	public static CartReader Instance { get; private set; }
	
	
		private const string SelectedPlatformMetaKey = "pgemu_selected_platform_id";
	
	private AppConfig _config = new();          // In-memory config (loaded or default).
	private string? _configPath;                // Base config.json path (shared).
	private string? _localConfigPath;           // config.local.json path (user overrides).
	
	
	public bool cartReaderActive = false;
	
	
	private readonly List<PlatformConfig> _platforms = new();
	// new game rom path, when file name detected, we get this johnson
	string path;
	
	public override async void _Ready()
	{
		Transition = GetNode<ScreenTransition>("/root/ScreenTransition");
		Instance = this;
		
		_consolePrompt = GetNode<VBoxContainer>(ConsolePrompt);
		_gameBoySelection = GetNode<Button>(GameBoyChoicePath);
		_n64Selection = GetNode<Button>(N64ChoicePath);
		_nesSelection = GetNode<Button>(NESChoicePath);
		_snesSelection = GetNode<Button>(SNESChoicePath);
		_cancelSelection = GetNode<Button>(CancelChoicePath);
		
		_gameFoundMessage = GetNode<VBoxContainer>(GameFoundMessagePath);
		
		_loadingMessage = GetNode<VBoxContainer>(LoadingMessagePath);
		
		_installMessage = GetNode<VBoxContainer>(InstallMessagePath);
		_installingLabel = GetNode<Label>(InstallingLabelPath);
		_progressBar = GetNode<ProgressBar>(ProgressBarPath);
		_progressBarContainer = GetNode<MarginContainer>(ProgressBarContainerPath);
		_installButton = GetNode<Button>(InstallButtonPath);
		_cancelInstallButton = GetNode<Button>(CancelInstallPath);
		
		_nowInstalledMessage = GetNode<VBoxContainer>(NowInstalledButtonPath);
		
		
		
		_gameBoySelection.Pressed += () => SendChoice(GAMEBOYNUM);
		_n64Selection.Pressed += () => SendChoice(N64NUM);
		_nesSelection.Pressed += () => SendChoice(NESNUM);
		_snesSelection.Pressed += () => SendChoice(SNESNUM);
		_cancelSelection.Pressed += CancelRead;
		
		LoadConfigAndPlatforms();
		


		GD.Print("helllsursufre");
		

	}
	
	public async void SendChoice(int choice)
	{

		switch(choice)
		{
			// gameboy
			case 1:
				platformNum = GAMEBOYNUM;
				SendConsoleChoice(1);
				break;
				
			// n64
			case 3:
				platformNum = N64NUM;
				SendConsoleChoice(3);
				break;
			
			// snes	
			case 4:
				platformNum = SNESNUM;
				SendConsoleChoice(4);
				break;
				
				
			// nes
			case 5:
				platformNum = NESNUM;
				SendConsoleChoice(5);
				break;
		}
	}
	
	
	public async Task<bool> CartReaderFound()
	{
		GD.Print(fileName);
		GD.Print(fileSize);
		if(!cartReaderActive) {
			GD.Print("false");
		}else{GD.Print("true");}
		if (!cartReaderActive) {
			fileName = null;
			GD.Print("-1 at cartsearch");
			int fileSize = -1;
		
			string[] portNames = SerialPort.GetPortNames();
			string? line;
			foreach (string portName in portNames.Reverse())
			{
					GD.Print("finding CartREader");
					GD.Print(portName);
					var possiblePort = new SerialPort(portName, 250000);
					possiblePort.ReadTimeout = 4000;
					//possiblePort.WriteTimeout = 2000;
					//possiblePort.DtrEnable = true;
					//possiblePort.RtsEnable = true;s
					//possiblePort.Handshake = Handshake.None;
					try
					{
						await Task.Run(() => possiblePort.Open());

						line = await Task.Run(() => possiblePort.ReadLine().Trim());
												GD.Print(line);
						line = await Task.Run(() => possiblePort.ReadLine().Trim());
												GD.Print("gunga" +line);
						
						GD.Print(" LLLIIIIIINNNENENNEEN : ");
						GD.Print(line);
						if (line == "OSCR Serial V15.5")
						{
							GD.Print("YOOOOOOO WE FOUND DA CART READDERR");
							cartReaderActive = true;
							//Thread.Sleep(2000);
							_installMessage.CallDeferred("set_visible", false);
							_progressBar.CallDeferred("set_visible", false);
							_nowInstalledMessage.CallDeferred("set_visible", false);
							_loadingMessage.CallDeferred("set_visible", false);
							_consolePrompt.CallDeferred("set_visible", true);
							
							Show();
							port = possiblePort;
							port.DataReceived += OnDataRecieved;
							return true;
						}
					}
					catch(System.Exception e){GD.Print("ERRRRRRR");}
					possiblePort.Close();
			}
		}
		if (port.IsOpen)
		{
			GD.Print("Cart reader found");
			Show();
			return false;
		}
		GD.Print("CartReader not found, restarting search");
		cartReaderActive = false;
		CartReaderFound();
		return false;
	}
	
	
	
	
	public async void SendConsoleChoice(short choice)
	{
		
		_consolePrompt.CallDeferred("set_visible", false);
		_loadingMessage.CallDeferred("set_visible", true);
		switch (choice)
		{
			// gameboy (color)
			case 0:
				port.Write("00");
				break;
				
			// gameboy adv
			case 1:
				GD.Print("errr we sending shi");
				try {port.Write("0");
					port.Write("1");}
				catch (TimeoutException) {
				}
				
				break;
				
			// nes
			case 5:
				port.Write("1");
				break;
				
			// snes
			case 4: 
				port.Write("2");
				port.Write("0");
				break;
				
			// n64
			case 3:
				port.Write("3");
				port.Write("0");
				port.Write("0");
				break;
			
		}
	}
	
	private void OnDataRecieved(object sender, SerialDataReceivedEventArgs e)
	{
		try 
		{
			int bytesToRead = port.BytesToRead;
			byte[] bytes = new byte[bytesToRead];
			port.Read(bytes, 0, bytesToRead);
		
			lock (fileBuffer)
			{
				fileBuffer.AddRange(bytes);
				ProcessData();
			}
		}
		catch (Exception ex)
		{
			GD.Print("EERRRRROOOOORRR" + ex.Message);
		}
	}
	
	private void ProcessData()
	{
		// check if cart reader needs confirmation and write to serial
		// get file name, size, and content
		// if name or size found, dont look again to prevent incorrect data
		if (fileName == null)
		{
			FindFileName();
		}
		
		if (fileSize == -1) 
		{
			fileSize = FindFileSize();
		} 
		
		ConfirmOptions();
		
		if ((fileName != null) && (fileSize != -1))
		{
			CreateFile(fileName, fileSize, fileContent);
			FindFileContent(fileSize);	
		}		
	}
	
	private void ConfirmOptions()
	{	
		// send 0 after confirm to read rom
		if (NeedsDoubleConfirmation() == true)
		{
			port.Write(" ");
			port.Write(" ");
			port.Write("0");
			GD.Print("Cart reader double confirm sent");
		}
		if (NeedsSingleConfirmation() == true)
		{
			port.Write(" ");
			port.Write("0");
			GD.Print("Cart reader single confirm sent");
		}
		
		if (NeedsReadConfirmation() == true)
		{
			port.Write("0");
			GD.Print("Cart reader read confirm sent");
		}
	}
	
	private bool NeedsDoubleConfirmation()
	{
		byte[] confirmationMarkerBytes = System.Text.Encoding.ASCII.GetBytes("Space/Zero");
		int confirmationMarkerIndex = FindString(fileBuffer, confirmationMarkerBytes);
		if (confirmationMarkerIndex != -1)
		{
			fileBuffer.RemoveRange(0, confirmationMarkerIndex + confirmationMarkerBytes.Length);
			return true;
		}
		return false;
	}
	
	private bool NeedsSingleConfirmation()
	{
		byte[] confirmationMarkerBytes = System.Text.Encoding.ASCII.GetBytes("Press Button...");
		int confirmationMarkerIndex = FindString(fileBuffer, confirmationMarkerBytes);
		if (confirmationMarkerIndex != -1)
		{
			fileBuffer.RemoveRange(0, confirmationMarkerIndex + confirmationMarkerBytes.Length);
			return true;
		}
		return false;
	}
	
	private bool NeedsReadConfirmation()
	{
		byte[] confirmationMarkerBytes = System.Text.Encoding.ASCII.GetBytes("0)Read ROM");
		int confirmationMarkerIndex = FindString(fileBuffer, confirmationMarkerBytes);
		if (confirmationMarkerIndex != -1 && readingRom == false)
		{
			fileBuffer.RemoveRange(0, confirmationMarkerIndex + confirmationMarkerBytes.Length);
			readingRom = true;
			return true;
		} else if (confirmationMarkerIndex != -1 && readingRom == true)
		{
			fileBuffer.RemoveRange(0, confirmationMarkerIndex + confirmationMarkerBytes.Length);
			readingRom = false;
			port.Write("5");
			return false;
		}
		return false;
	}
	
	private async Task<string> FindFileName() 
	{
		// vars for different markers and their index in the buffer
		byte[] nameMarkerBytes;
		int nameMarkerIndex;
		int nameLineEndIndex;
		int nameStartIndex;
		int nameLength;
		
		// file name vars
		byte[] nameBytes;

		nameMarkerBytes = System.Text.Encoding.ASCII.GetBytes("NAME: ");
		nameMarkerIndex = FindString(fileBuffer, System.Text.Encoding.ASCII.GetBytes("NAME: "));

		if (nameMarkerIndex != -1)
		{
			nameLineEndIndex = fileBuffer.IndexOf(0x0A, nameMarkerIndex);
			if (nameLineEndIndex != -1)
			{
				GD.Print(nameLineEndIndex);
				nameStartIndex = nameMarkerIndex + nameMarkerBytes.Length;
				nameLength = nameLineEndIndex - nameStartIndex;
				nameBytes = fileBuffer.GetRange(nameStartIndex, nameLength).ToArray();
				fileName = System.Text.Encoding.ASCII.GetString(nameBytes).Trim();
				GD.Print("Name: " + fileName);
				
				fileBuffer.RemoveRange(0, nameLineEndIndex + 1);
				if (GameIsInLibrary(fileName, 1) && new FileInfo(path).Length != 0)
				{
					// close port to prevent game install since no longer needed
					port.DataReceived -= OnDataRecieved;
					port.Close();
					GD.Print("Game found in libraryyy");
					await GoToGame(fileName[..^4]);
					this.CallDeferred("set_visible", false);
					_loadingMessage.CallDeferred("set_visible", false);
					
					GD.Print("resetting cart reader name and file size");
					fileName = null;
					
					fileSize = -1;
					cartReaderActive = false;

				}
				// otherwise, install it
				{
					GD.Print("Game not found, installing now");
					_installingLabel.CallDeferred(Label.MethodName.SetText, "Saving " + fileName[..^4] + " to cartridge reader...");
					_loadingMessage.CallDeferred("set_visible", false);
					_installMessage.CallDeferred("set_visible", true);
				}
			}
		} 
		// no name found, return nothing
		return null;
	}
	
	private int FindFileSize()
	{
					// vars for different markers and their index in the buffer
		byte[] sizeMarkerBytes;
		int sizeMarkerIndex;
		int sizeLineEndIndex;
		int sizeStartIndex;
		int sizeLength;
		
		// file size vars
		byte[] sizeBytes;
		string sizeString;
		int size;
				
		sizeMarkerBytes = System.Text.Encoding.ASCII.GetBytes("SIZE: ");
		sizeMarkerIndex = FindString(fileBuffer, sizeMarkerBytes);
		//GD.Print(sizeMarkerIndex);
		//GD.Print("INDZZZLELE: "+ sizeMarkerIndex);
		if (sizeMarkerIndex != -1)
		{
			sizeLineEndIndex = fileBuffer.IndexOf(0x0A, sizeMarkerIndex);
			if (sizeLineEndIndex != -1)
			{
				GD.Print(sizeLineEndIndex);
				sizeStartIndex = sizeMarkerIndex + sizeMarkerBytes.Length;
				sizeLength = sizeLineEndIndex - sizeStartIndex;
				sizeBytes = fileBuffer.GetRange(sizeStartIndex, sizeLength).ToArray();
				sizeString = System.Text.Encoding.ASCII.GetString(sizeBytes).Trim();
				Int32.TryParse(sizeString, out size);
				GD.Print("Size: " + size);
				
				// remove all data up to now, as file content comes next
				fileBuffer.RemoveRange(0, sizeLineEndIndex + 1);
				_progressBar.Value = 0;
				_progressBar.SetDeferred(ProgressBar.PropertyName.MaxValue, size);
				GD.Print("biiig: " + _progressBar.MaxValue);
				_progressBar.CallDeferred("set_visible", true);
				_installingLabel.SetDeferred(Label.PropertyName.Text, "Transferring " + fileName[..^4] + " to PC...");
				
				return size;
			}
		} 
		return -1;
	}
	
	private void FindFileContent(int size) 
	{
		// initialize vars for file recieving(obviously)
		int length;
		byte[] buffer = null;
		byte[] lastBuffer = null;
		int bytesRead;
		int expectedChecksum;
		int calculatedChecksum;
		int totalBytesRead = 0;
		int count = 0;
		
		port.DataReceived -= OnDataRecieved;
		var portStream = port.BaseStream;
		GD.Print("starting file write");
		
		// read bytes chunk at a time and check checksum to verify no bytes lost
		while(true)
		{
			try
			{	

				length = port.ReadByte();
				//if((fileSize - totalBytesRead) < 64) 
				//{
					//GD.Print("shit almost done" + (fileSize - totalBytesRead));
					//length = fileSize-totalBytesRead;
				//}
				
				
				if (length == 0)
				{
					GD.Print("EOF byte read, finalizing file");
					//cartReaderActive = false;
					break;
				}
				if (buffer != null)
				{
					lastBuffer = buffer;
				}
				buffer = new byte[length];
				bytesRead = 0;
				
				// keep reading until length of bytes read equals lengthet we were given
				while (bytesRead < length)
				{
					bytesRead += portStream.Read(buffer, bytesRead, length - bytesRead);
				}
				
				expectedChecksum = port.ReadByte();
				
				calculatedChecksum = 0;
				foreach (byte b in buffer) 
				{
					calculatedChecksum ^= b;
				}
				
				if (calculatedChecksum == expectedChecksum)
				{ 
					// for some reason this bunk ass shit sends duplicate lines at start, dont write those lines so shit stay right
					if (count > 2) {
						if (WriteToFile(buffer))
						{
							return;
						}

						//stream.Write(buffer, 0, buffer.Length);
						totalBytesRead += 16;
						if (totalBytesRead == 4096) 
						{
							_progressBar.SetDeferred(ProgressBar.PropertyName.Value, _progressBar.Value + 4096);
							totalBytesRead = 0;
						}
					}
					
					//if(stream.Length == fileSize)
					//{
						////stream.close();
						//GD.Print("File write done");
						//cartReaderActive = false;
						//port.Close();
						//_installMessage.CallDeferred("set_visible", false);
						//_progressBar.CallDeferred("set_visible", false);
						//_nowInstalledMessage.CallDeferred("set_visible", true);
						//this.CallDeferred("Show");
										//if(stream.Length == fileSize)
					//{
						////stream.close();
						//GD.Print("File write done");
						//cartReaderActive = false;
						//port.Close();
						//_installMessage.CallDeferred("set_visible", false);
						//_progressBar.CallDeferred("set_visible", false);
						//_nowInstalledMessage.CallDeferred("set_visible", true);
						//this.CallDeferred("Show");
						
					else
					{
						count += 1;	
					}
					//everything good, we send ack
					port.Write("A");
				}
				else
				{
					GD.Print("checksum cooked, sending no ack");
					// shit is fucked ass up, tell arduino to send again
					port.Write("N");
				}
			}
			catch (Exception e)
			{
				GD.Print("timeout >:(");
				GD.Print(e.Message);
				// bruh, arduino buggin >:( send that shit again 
				port.Write("N");
			}	
		}
		
		//fileContent = fileBuffer.GetRange(0, size).ToArray();
		//GD.Print("Final File Byte: " + fileContent[fileContent.Length - 1]);	
		//fileBuffer.RemoveRange(0, size-1);
	}
	
	private void CreateFile(string name, int size, byte[] content)
	{
		LoadConfig();
		path = platformPath + "\\" + name;
		path = @path;
		GD.Print("pahththththhtht: " + path);
		
		using(File.Create(path))
		{}
		
		GD.Print("file created");
		 

		fileContent = null;
	}
	
	private bool WriteToFile(byte[] buffer) {
		using (FileStream stream = new FileStream(path, FileMode.Append, System.IO.FileAccess.Write))
		{
			stream.Write(buffer, 0, buffer.Length);

			if(stream.Length == fileSize)
			{
				//stream.close();
				GD.Print("File write done");
//				cartReaderActive = false;
				port.Close();
				_installMessage.CallDeferred("set_visible", false);	
				_progressBar.CallDeferred("set_visible", false);
				_nowInstalledMessage.CallDeferred("set_visible", true);
				this.CallDeferred("Show");
			
				fileName = null;
				fileSize = -1;
				return true;
			}
		}
		return false;
	}
	
	public void LoadConfig()
	{

		try
		{
			var tree = GetTree();

			// Prefer config path passed in from the previous scene, then fall back to heuristics.
			_configPath = tree.HasMeta("pgemu_config_path") ? tree.GetMeta("pgemu_config_path").AsString() : null;
			_configPath = string.IsNullOrWhiteSpace(_configPath) ? null : _configPath;
			_configPath ??= ConfigFinder.FindConfigPath();
			//_configPath ??= TryFindConfigNearGodotProject();

			// If we have a real config.json, load it and set up the local override path.
			if (_configPath != null && File.Exists(_configPath))
			{
				_config = AppConfig.Load(_configPath);
				_localConfigPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "config.local.json");
				GD.Print("Pathhhhh: "+ _config.LibraryRoot);
				platformPath = _config.Platforms[platformNum].RomPath;
			}

			// No config.json found, start with a blank/default config.
			_config = new AppConfig();
			//return null;
		}
		catch (Exception ex)
		{
			// Reset to safe defaults on failure.
			_config = new AppConfig();
			_configPath = null;
			//return null;

		}
	}
	
	private int FindString(List<byte> buffer, byte[] sequence)
	{
		for (int i = 0; i <= buffer.Count - sequence.Length; i++)
		{
			if (sequence.Select((b, index) => buffer[i+index] == b).All(v => v))
			{
				return i;
			}
		}
		

		return -1;
	}
	
	private bool GameIsInLibrary(string name, int consoleChoice)
	{
		LoadConfig();
		path = platformPath + "\\" + name;
		path = @path;
		GD.Print(path);
		if (File.Exists(path))
		{
			return true;
		}
		return false;
	}
	
		public async Task GoToGame(string name)
	{

		GD.Print("Going to select: " + name);
		var platformMatched = _platforms[0];
		foreach (var p in _platforms){
			var scanned = LibraryScanner.Scan(p, _config.LibraryRoot, out var scanDir);
			foreach (var g in scanned){
				if (g.Title == name){
					platformMatched = p;
					break;
				}
			}
		}
		if (platformMatched != null){

			var tree = GetTree();
			tree.SetMeta(SelectedPlatformMetaKey, platformMatched.Id);
			if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);
			//using this to just store how we sort by games;
			CollectionStorage.SearchResult = name;
			Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Spiral, .35f, 0f);
		
		}
		else{
			GD.Print("Error getting the platform!");
		}
		/*// Pass selection to the next screen without needing a singleton.
		
		*/
	}
	
		private void LoadConfigAndPlatforms()
	{
		try
		{
			_configPath = ConfigFinder.FindConfigPath();

			// 2) Godot-friendly fallback: look relative to the project root.
			_configPath ??= TryFindConfigNearGodotProject();

			if (_configPath == null)
			{
				//SetStatus("config.json not found. Put it in the repo root or inside the Godot project folder.");
				_config = null;
				return;
			}

			_config = AppConfig.Load(_configPath);
			// Your sample config uses `~/` for LibraryRoot; .NET doesn't auto-expand that.
			_config.LibraryRoot = ExpandHomePath(_config.LibraryRoot);
			PlatformList._configuration = _config;

			//SetStatus($"Loaded config: {_configPath}");
		}
		catch (Exception ex)
		{
			_config = null;
			_configPath = null;
			//SetStatus($"Config load failed: {ex.Message}");
		}
		
		// Load platforms
		_platforms.Clear();
		if (_config?.Platforms is { Count: > 0 } platforms)
		{
			_platforms.AddRange(platforms);
			PlatformList.platformList = platforms;
		}
		else
		{
			_platforms.Add(new PlatformConfig { Id = "missing", Name = "Missing config.json" });
		}

		UpdateNavEnabled();
		}
	
		private static string? TryFindConfigNearGodotProject()
	{
		try
		{
			// `res://` is the project root; GlobalizePath gives an OS path.
			var projectDir = ProjectSettings.GlobalizePath("res://");

			var inProject = Path.Combine(projectDir, "config.json");
			if (File.Exists(inProject)) return inProject;

			var inParent = Path.GetFullPath(Path.Combine(projectDir, "..", "config.json"));
			if (File.Exists(inParent)) return inParent;
		}
		catch
		{
			// Best-effort; ignore.
		}

		return null;
	}
	
		private static string ExpandHomePath(string path)
	{
		// Expand "~" and "~/..." into an absolute path. (On Windows, "~" isn't typically used, but this helps macOS/Linux.)
		if (string.IsNullOrWhiteSpace(path)) return path;

		if (path == "~")
			return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

		if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
		{
			var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
			var rest = path.Substring(2);
			return Path.Combine(home, rest);
		}

		return path;
	}

	private void SetStatus(string text)
	{
		//if (_status != null)
			//_status.Text = text;
		//else
			//GD.Print(text);
	}
	
		private void UpdateNavEnabled()
	{
		//var enabled = Count > 1;
		//if (_prev != null) _prev.Disabled = !enabled;
		//if (_next != null) _next.Disabled = !enabled;
	}
	
	private void CancelRead() 
	{
		Hide();
		port.Close();
		cartReaderActive = false;
	}
	
}
