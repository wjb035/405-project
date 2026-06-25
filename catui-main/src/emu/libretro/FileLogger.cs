using Godot;
using System;
using System.IO;

public static class FileLogger
{
	private static string _logFilePath = "";
	private static bool _initialized = false;
	private static object _lock = new object();

	private static void Initialize()
	{
		if (_initialized) return;

		try
		{
			string logDir = ProjectSettings.GlobalizePath("user://");
			_logFilePath = Path.Combine(logDir, "debug_log.txt");
			
			if (File.Exists(_logFilePath))
				File.Delete(_logFilePath);
			
			_initialized = true;
			
			Log("=== DEBUG LOG STARTED ===");
			Log($"Platform: {OS.GetName()}");
			Log($"Time: {DateTime.Now}");
			Log($"Log file: {_logFilePath}");
			Log("=========================");
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to initialize FileLogger: {e.Message}");
		}
	}

	public static void Log(string message)
	{
		Initialize();
		
		string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
		
		GD.Print(logMessage);
		
		try
		{
			lock (_lock)
			{
				File.AppendAllText(_logFilePath, logMessage + "\n");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to write to log file: {e.Message}");
		}
	}

	public static void Error(string message)
	{
		Initialize();
		
		string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] error: {message}";
		
		GD.PrintErr(logMessage);
		
		try
		{
			lock (_lock)
			{
				File.AppendAllText(_logFilePath, logMessage + "\n");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to write error to log file: {e.Message}");
		}
	}

	public static string GetLogPath()
	{
		Initialize();
		return _logFilePath;
	}

	public static string ReadLog()
	{
		Initialize();
		
		try
		{
			if (File.Exists(_logFilePath))
			{
				lock (_lock)
				{
					return File.ReadAllText(_logFilePath);
				}
			}
		}
		catch (Exception e)
		{
			return $"Error reading log: {e.Message}";
		}
		
		return "No log file found";
	}
}
