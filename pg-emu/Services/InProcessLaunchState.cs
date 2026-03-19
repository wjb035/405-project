using System;

public sealed class InProcessLaunchRequest
{
	public string RomPath { get; init; } = string.Empty;
	public string CorePath { get; init; } = string.Empty;
	public string CoreId { get; init; } = string.Empty;
	public string GameTitle { get; init; } = string.Empty;
	public string ReturnScene { get; init; } = "res://GameSelect.tscn";
}

public static class InProcessLaunchState
{
	private static InProcessLaunchRequest? _pending;

	public static void SetPending(InProcessLaunchRequest request)
	{
		_pending = request;
	}

	public static bool TryConsume(out InProcessLaunchRequest request)
	{
		if (_pending == null)
		{
			request = new InProcessLaunchRequest();
			return false;
		}

		request = _pending;
		_pending = null;
		return true;
	}
}
