using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace RogueDemo;

// 性能矩阵:敌人数量 × 敌方弹幕数量,逐组合测真实帧率(关闭垂直同步)。
// 敌人不会死亡也不会自行放招,弹幕数量由测试注入并持续补充,保证每个组合的数量精确。
// 用法: godot --path . -- --bench 300:300 300:500 [--repeat=3] [--out=res://验证记录/性能矩阵_cs.json]
public partial class Bench : Node
{
	public Main Game;
	const float Warmup = 1.5f;
	const float Measure = 3.0f;
	readonly RandomNumberGenerator rng = new();
	readonly StringBuilder json = new();

	Vector2 RingPos(int i, int count)
	{
		float a = Mathf.Tau * i / count + 0.37f * (i % 5);
		float r = 140f + (i * 37) % 420;
		return Game.PlayerPos + new Vector2(Mathf.Cos(a) * r * 1.6f, Mathf.Sin(a) * r);
	}

	Projectile MakeBullet()
	{
		var sprite = (PSprite)rng.RandiRange(1, 4);
		var pos = Game.PlayerPos + U.Dir(rng.Randf() * Mathf.Tau) * rng.RandfRange(240f, 620f);
		pos.X = Mathf.Clamp(pos.X, Main.FIELD.Position.X + 40f, Main.FIELD.End.X - 40f);
		pos.Y = Mathf.Clamp(pos.Y, Main.FIELD.Position.Y + 40f, Main.FIELD.End.Y - 40f);
		return new Projectile { P = pos, V = U.Dir(rng.Randf() * Mathf.Tau) * rng.RandfRange(20f, 60f), Life = 1e6f, Damage = 1f, Enemy = true, R = 7f, Color = new Color("e7a087"), Sprite = sprite, Size = sprite == PSprite.Orb ? 88f : 66f, Spin = rng.Randf() * Mathf.Tau };
	}

	int EnemyBullets()
	{
		int n = 0;
		foreach (var b in Game.Projectiles)
			if (b.Enemy) n++;
		return n;
	}

	void Setup(int enemies)
	{
		Game.StartRun();
		Game.Invulnerable = 999f;
		Game.Wave = 12;
		Game.WaveTime = 9999f;
		Game.SpawnTimer = 999f;
		Game.Weapons = new List<Weapon>();
		for (int i = 0; i < 6; i++) Game.Weapons.Add(Game.MakeWeapon(i, 4));
		Game.Ui.ShowHud();
		for (int i = 0; i < enemies; i++)
		{
			var e = Game.SpawnEnemy(i % 7, RingPos(i, enemies));
			e.Hp = 1e9f;
			e.MaxHp = 1e9f;
			e.Cooldown = 1e9f;     // 不放招、不冲锋,弹幕数量由测试精确控制
		}
	}

	async System.Threading.Tasks.Task<string> RunCell(int enemies, int bullets)
	{
		Setup(enemies);
		var frameMs = new List<double>();
		long total = 0;
		ulong begin = Time.GetTicksUsec();
		ulong prev = begin;
		int frames = 0, allFrames = 0;
		System.Array.Clear(Main.Prof, 0, Main.Prof.Length);
		double cpuSum = 0, gpuSum = 0, callSum = 0;
		var vrid = GetViewport().GetViewportRid();
		RenderingServer.ViewportSetMeasureRenderTime(vrid, true);
		while (Time.GetTicksUsec() - begin < (ulong)((Warmup + Measure) * 1000000f))
		{
			Game.Invulnerable = 999f;
			foreach (var e in Game.Enemies) e.Cooldown = 1e9f;
			for (int need = bullets - EnemyBullets(); need > 0; need--) Game.Projectiles.Add(MakeBullet());
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			ulong now = Time.GetTicksUsec();
			allFrames++;
			if (now - begin > (ulong)(Warmup * 1000000f))
			{
				frameMs.Add((now - prev) / 1000.0);
				total += Game.Projectiles.Count;
				frames++;
				cpuSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vrid);
				gpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vrid);
				callSum += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
			}
			prev = now;
		}
		var prof = new StringBuilder();
		for (int pi = 0; pi < 7; pi++) prof.Append($" {Main.ProfNames[pi]} {Main.Prof[pi] / System.Math.Max(1, allFrames):0.00}");
		frameMs.Sort();
		int n = System.Math.Max(1, frameMs.Count);
		double avg = frameMs.Sum() / n;
		double p99 = frameMs[System.Math.Min(n - 1, (int)(n * 0.99))];
		double worst = frameMs[n - 1];
		string line = $"敌人 {Game.Enemies.Count,3} | 弹幕 {bullets,4} | 平均 {1000.0 / avg,6:0.0} FPS | 1%低 {1000.0 / p99,6:0.0} | 最低 {1000.0 / worst,6:0.0} | 平均 {avg:0.00} ms | 最大 {worst:0.00} ms | 渲染CPU {cpuSum / System.Math.Max(1, frames):0.00} ms | 渲染GPU {gpuSum / System.Math.Max(1, frames):0.00} ms | 绘制调用 {callSum / System.Math.Max(1, frames):0} |{prof}";
		json.Append($"{{\"敌人\":{Game.Enemies.Count},\"弹幕\":{bullets},\"平均FPS\":{1000.0 / avg:0.0},\"百分之一低帧FPS\":{1000.0 / p99:0.0},\"最低FPS\":{1000.0 / worst:0.0},\"平均帧耗时\":{avg:0.00},\"最大帧耗时\":{worst:0.00}}},");
		return line;
	}

	public override async void _Ready()
	{
		rng.Randomize();
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;
		Game.SaveEnabled = false;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Game.Camera.Zoom = Vector2.One * Main.VIEW_ZOOM;
		var cells = new List<(int, int)>();
		int repeat = 1;
		string output = "res://验证记录/性能矩阵_cs.json";
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--repeat=")) repeat = int.Parse(arg.Substring(9));
			else if (arg.StartsWith("--out=")) output = arg.Substring(6);
			else if (arg.Contains(':'))
			{
				var parts = arg.Split(':');
				cells.Add((int.Parse(parts[0]), int.Parse(parts[1])));
			}
		}
		if (cells.Count == 0)
			foreach (int en in new[] { 50, 100, 200, 300 })
				foreach (int bu in new[] { 0, 50, 100, 200, 300 }) cells.Add((en, bu));
		foreach (var (en, bu) in cells)
			for (int r = 0; r < repeat; r++) GD.Print(await RunCell(en, bu));
		using (var file = Godot.FileAccess.Open(output, Godot.FileAccess.ModeFlags.Write))
			file.StoreString("[" + json.ToString().TrimEnd(',') + "]");
		GetTree().Quit();
	}
}
