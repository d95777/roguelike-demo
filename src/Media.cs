using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace RogueDemo;

public class CueVariant
{
	public AudioStream Stream;
	public float GainDb;
}

public class Cue
{
	public string Group;
	public float Cooldown;
	public List<CueVariant> Variants = new();
}

public class EffectDef
{
	public Texture2D Texture;
	public int Frames, Columns;
	public float Fps, Cell, Duration;
	public Vector2 Anchor;
}

// 独立的音效随机数与分类声部,不改变战斗掉落和暴击序列。
public partial class Media : Node
{
	public Dictionary<string, Cue> Bank = new();
	public Dictionary<string, EffectDef> EffectBank = new();
	public readonly Dictionary<string, List<AudioStreamPlayer>> Voices = new();
	readonly Dictionary<string, float> nextCue = new();
	readonly Dictionary<string, float> nextGroup = new();
	readonly RandomNumberGenerator audioRng = new();
	public bool Muted, Paused;
	public AudioStreamPlayer MusicPlayer;
	public string MusicName = "";
	public float MusicDuck;                       // 额外衰减(dB),商店等安静界面使用
	readonly Dictionary<string, (AudioStream Stream, float Gain)> music = new();
	public Dictionary<string, int> Played = new();

	public override void _Ready()
	{
		audioRng.Randomize();
		using (var doc = JsonDocument.Parse(U.Json("res://data/audio.json")))
		{
			foreach (var p in doc.RootElement.EnumerateObject())
			{
				var cue = new Cue { Group = p.Value.GetProperty("group").GetString(), Cooldown = p.Value.GetProperty("cooldown").GetSingle() };
				foreach (var v in p.Value.GetProperty("variants").EnumerateArray())
					cue.Variants.Add(new CueVariant { Stream = GD.Load<AudioStream>(v.GetProperty("path").GetString()), GainDb = v.GetProperty("gain_db").GetSingle() });
				Bank[p.Name] = cue;
			}
		}
		var counts = new Dictionary<string, int> { ["attack"] = 3, ["impact"] = 2, ["player"] = 1, ["ui"] = 3, ["alert"] = 1, ["pickup"] = 2, ["death"] = 2 };
		foreach (var kv in counts)
		{
			var list = new List<AudioStreamPlayer>();
			for (int i = 0; i < kv.Value; i++)
			{
				var voice = new AudioStreamPlayer();
				AddChild(voice);
				list.Add(voice);
			}
			Voices[kv.Key] = list;
		}
		using (var doc = JsonDocument.Parse(U.Json("res://data/music.json")))
		{
			foreach (var p in doc.RootElement.EnumerateObject())
			{
				var stream = GD.Load<AudioStream>(p.Value.GetProperty("path").GetString());
				if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
				music[p.Name] = (stream, p.Value.GetProperty("gain_db").GetSingle());
			}
		}
		MusicPlayer = new AudioStreamPlayer();
		AddChild(MusicPlayer);
		using (var doc = JsonDocument.Parse(U.Json("res://data/effects.json")))
		{
			foreach (var p in doc.RootElement.EnumerateObject())
			{
				var e = p.Value;
				var anchor = e.GetProperty("anchor");
				EffectBank[p.Name] = new EffectDef
				{
					Texture = GD.Load<Texture2D>(e.GetProperty("path").GetString()),
					Frames = e.GetProperty("frames").GetInt32(),
					Columns = e.GetProperty("columns").GetInt32(),
					Fps = e.GetProperty("fps").GetSingle(),
					Cell = e.GetProperty("cell").GetSingle(),
					Duration = e.GetProperty("duration").GetSingle(),
					Anchor = new Vector2(anchor[0].GetSingle(), anchor[1].GetSingle()),
				};
			}
		}
	}

	public bool Play(string cue)
	{
		if (Muted || Paused || !Bank.TryGetValue(cue, out var config)) return false;
		string group = config.Group;
		float now = Time.GetTicksMsec() / 1000f;
		if (now < nextCue.GetValueOrDefault(cue, 0f) || now < nextGroup.GetValueOrDefault(group, 0f)) return false;
		AudioStreamPlayer voice = null;
		foreach (var candidate in Voices[group])
		{
			if (!candidate.Playing)
			{
				voice = candidate;
				break;
			}
		}
		// 战斗声部满时舍弃新声音;重要提示只在自身分组内替换。
		if (voice == null)
		{
			if (group == "attack" || group == "impact" || group == "pickup" || group == "death") return false;
			voice = Voices[group][0];
		}
		var variant = config.Variants[audioRng.RandiRange(0, config.Variants.Count - 1)];
		voice.Stream = variant.Stream;
		voice.VolumeDb = variant.GainDb;
		voice.PitchScale = group == "attack" || group == "impact" || group == "pickup" || group == "death" ? audioRng.RandfRange(0.97f, 1.03f) : 1f;
		voice.Play();
		nextCue[cue] = now + config.Cooldown;
		nextGroup[group] = now + (group == "attack" ? 0.05f : group == "impact" ? 0.075f : 0f);
		Played[cue] = Played.GetValueOrDefault(cue, 0) + 1;
		return true;
	}

	// 背景音乐:同名曲目不重启;name 为空表示停止。
	public void PlayMusic(string name, float duck = 0f)
	{
		MusicDuck = duck;
		if (name == null || name == "" || !music.TryGetValue(name, out var track))
		{
			MusicName = "";
			MusicPlayer.Stop();
			return;
		}
		MusicPlayer.VolumeDb = track.Gain + duck;
		if (MusicName == name && MusicPlayer.Playing) return;
		MusicName = name;
		MusicPlayer.Stream = track.Stream;
		if (!Muted) MusicPlayer.Play();
	}

	public void StopAll()
	{
		foreach (var group in Voices.Values)
			foreach (var voice in group) voice.Stop();
		nextCue.Clear();
		nextGroup.Clear();
	}

	public void SetPaused(bool value)
	{
		Paused = value;
		foreach (var group in Voices.Values)
			foreach (var voice in group) voice.StreamPaused = value;
		MusicPlayer.StreamPaused = value;
	}

	public void SetMuted(bool value)
	{
		Muted = value;
		if (value)
		{
			StopAll();
			MusicPlayer.Stop();
		}
		else if (MusicName != "") MusicPlayer.Play();
	}

	public int FrameIndex(string name, float age, bool loop = false)
	{
		var effect = EffectBank[name];
		int index = Mathf.Max(0, (int)(age * effect.Fps));
		return loop ? index % effect.Frames : Mathf.Min(index, effect.Frames - 1);
	}

	public void DrawEffect(CanvasItem canvas, string name, Vector2 point, float size, float angle, float age, bool loop = false, Color? tint = null)
	{
		var effect = EffectBank[name];
		int index = FrameIndex(name, age, loop);
		float cell = effect.Cell;
		var source = new Rect2((index % effect.Columns) * cell, (index / effect.Columns) * cell, cell, cell);
		var anchor = effect.Anchor / cell * size;
		canvas.DrawSetTransform(point, angle);
		canvas.DrawTextureRectRegion(effect.Texture, new Rect2(-anchor, new Vector2(size, size)), source, tint ?? Colors.White);
		canvas.DrawSetTransform(Vector2.Zero);
	}

	public override void _ExitTree()
	{
		// 独立发行版可能在音效仍播放时退出,显式释放播放句柄。
		StopAll();
		MusicPlayer.Stop();
		MusicPlayer.Stream = null;
		foreach (var group in Voices.Values)
			foreach (var voice in group) voice.Stream = null;
		Bank.Clear();
	}
}
