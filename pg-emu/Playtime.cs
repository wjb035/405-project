using Godot;
using System;
using System.Diagnostics;
using PGEmu.app;
using System.Collections.Generic;
using System.Linq;

public partial class Playtime : Node
{
	private Timer checkTimer;
	private Process runningGame;
	private int totalSecondsPlayed = 0;
	private GameEntry currentRunningGame = null;
	
	public override void _Ready()
	{
		PlaytimeStorage.LoadFromJson();
		checkTimer = new Timer();
		checkTimer.WaitTime = 30;
		checkTimer.Autostart = false;
		checkTimer.OneShot = false;

		AddChild(checkTimer);
		checkTimer.Timeout += OnTimerTimeout;
	}


	public void FindPlatform(PlatformConfig? platformCheck, GameEntry? currentGame){
		// iterate through all other emulators to kill their process
		// For some reason, if PPSSPP is open beforehand, then it won't be closed
		// (Since it has a different exeName, PPSSPPWindows64)
		// However we wouldn't be tracking that so it's not going to intrude on that
		foreach (var kvp in PlaytimeStorage.EmulatorToName){
			if (kvp.Key == platformCheck.DefaultEmulatorId){
				
			}
			else{
				Process[] otherEmulators = Process.GetProcessesByName(kvp.Value);
				foreach (var p in otherEmulators){
					p.Kill();
				}
			}
		}
		
		Process[] processes = [];
		if (PlaytimeStorage.EmulatorToName.TryGetValue(platformCheck.DefaultEmulatorId, out string ExeName)){
			processes = Process.GetProcessesByName(ExeName);
			GD.Print("The process existed under " + ExeName);
		}
		else{
			GD.Print("Error setting up playtime!");
			//return;
		}
		
		//Process[] processes = Process.GetProcessesByName("platformCheck.DefaultEmulatorId");
		GD.Print(processes.Length + " "+ platformCheck.DefaultEmulatorId + " processes were found");
		/*
		This is basically just to find what the name of each exe is when running to find
		
*/
		foreach (var p in Process.GetProcesses())
{
			if (p.ProcessName.ToLower().Contains("ppsspp"))
			{
				GD.Print("Found: " + p.ProcessName);
			}
		
		}
		
		
		// if we have a single process, that's all we need
		// in the future we will have to handle more of them, and also tying it to a game
		
		if (processes.Length == 1){
			bool isPlatformInPlaytime = false;
			foreach (var p in PlaytimeStorage.playtimeTracked){
				if (p.Key == platformCheck.DefaultEmulatorId){
					isPlatformInPlaytime = true;
					bool isGameInList = false;
					
					// now that we know the platform exists in the list, we gotta see if the game is in the list
					foreach (var g in p.Value){
						if (g.Name == currentGame.Name){
							isGameInList = true;
							currentRunningGame = g;
							GD.Print("Yay, " + g.Name + " was in the list and has ran for " + currentRunningGame.TimePlayed);
							break;
						}
					}
					
					// if the game isn't in the list, add it, then grab a reference to the right one
					if (!isGameInList){
						GD.Print("Boo, " + currentGame.Name + " was NOT in the list");
						p.Value.Add(currentGame);
						foreach (var g in p.Value){
							if (g.Name == currentGame.Name){
								currentRunningGame = g;
							}
						}
					}
					
					break;
				}
		
			}
			// the platform was not found in all the playtimes, so we have a new platform to add
			if (!isPlatformInPlaytime){
				PlaytimeStorage.playtimeTracked.Add(new KeyValuePair<string, List<GameEntry>>(platformCheck.DefaultEmulatorId, new List<GameEntry>()));;
				
				GD.Print("LOL! There was no reference to " +  platformCheck.DefaultEmulatorId + " in the list!");
				
				foreach (var p in PlaytimeStorage.playtimeTracked){
					if (p.Key == platformCheck.DefaultEmulatorId){
					p.Value.Add(currentGame);
							foreach (var g in p.Value){
								if (g.Name == currentGame.Name){
									currentRunningGame = g;
								}
							}
						}
						}
			}
			else{
				GD.Print("Epic snakes, " +  platformCheck.DefaultEmulatorId + " was in the list!");
			}
				
			
			
			StartMonitoring(processes[0]);
		}
		else if (processes.Length == 0){
			GD.Print("We weren't able to detect any instances of "+ platformCheck.DefaultEmulatorId);
		}
		else if (processes.Length > 1){
			// We want to sort the list of processes by when they were started
			processes = processes
	   	 	.OrderByDescending(p => p.StartTime)
			.ToArray();
			
			foreach(var p in processes){
				GD.Print(p.StartTime);
			}
			// Now that the newest is up front, kill EVERYTHING. 
			for (int i = 1; i < processes.Length; i++){
				processes[i].Kill();
			}
			
			StartMonitoring(processes[0]);
		}
		
	}
	public void StartMonitoring(Process gameProcess)
	{
		runningGame = gameProcess;
		checkTimer.Start();
	}

	private void OnTimerTimeout()
	{
		
		if (runningGame == null)
			return;

		if (runningGame.HasExited)
		{
			GD.Print("Game closed!");
			checkTimer.Stop();
			OnGameClosed();
			PlaytimeStorage.SaveToJson();
			GD.Print("Playtime Saved!");
		}
		else
		{
 			currentRunningGame.TimePlayed+=30;
			GD.Print(currentRunningGame.Name + " has been running for " + currentRunningGame.TimePlayed + " seconds");
			GD.Print("Game still running...");
			PlaytimeStorage.SaveToJson();
		}
	}

	public void OnGameClosed()
	{
		GD.Print("Run cleanup code here");
	}
}
