using Godot;
using System.Collections.Generic;

/// <summary>
/// Tiny sound-effect helper. Plays res://Music/sfx/{name}.ogg (or .wav / .mp3) if the file exists,
/// and silently does nothing if it does not. Teammates only have to drop files with the agreed names
/// (see ASSETS.md); no code changes are needed when a sound is added.
/// </summary>
public static class Sfx
{
	public const string Folder = "res://Music/sfx/";
	private static readonly string[] Extensions = { "ogg", "wav", "mp3" };
	private static readonly Dictionary<string, AudioStream> cache = new();

	public static void Play(Node context, string name, float volumeDb = 0f)
	{
		if (context == null || !GodotObject.IsInstanceValid(context) || !context.IsInsideTree()) return;

		AudioStream stream = Load(name);
		if (stream == null) return;

		var player = new AudioStreamPlayer { Stream = stream, VolumeDb = volumeDb };
		player.ProcessMode = Node.ProcessModeEnum.Always; // still audible while paused (menus)
		context.GetTree().Root.AddChild(player);
		player.Finished += player.QueueFree;
		player.Play();
	}

	private static AudioStream Load(string name)
	{
		if (cache.TryGetValue(name, out AudioStream cached)) return cached;

		foreach (string ext in Extensions)
		{
			string path = $"{Folder}{name}.{ext}";
			if (ResourceLoader.Exists(path))
			{
				var stream = GD.Load<AudioStream>(path);
				if (stream != null)
				{
					cache[name] = stream;
					return stream;
				}
			}
		}
		return null; // not cached: a file added later is picked up automatically
	}
}