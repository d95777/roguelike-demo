using System;
using System.Collections.Generic;
using Godot;

namespace RogueDemo;

// 录制宣传集锦:自动驾驶走位 + 分段编排的高能战斗,隐藏界面,只留战斗画面。
// 配合 Movie Maker 以固定帧率逐帧输出,渲染慢也不会掉帧:
//   godot --path . --write-movie 集锦.avi --fixed-fps 30 --resolution 1920x1080 -- --test --record
public partial class Recorder : Node
{
	public Main Game;

	record Segment(float Seconds, Action Setup);

	readonly List<Segment> segments = new();
	int index = -1;
	float clock, segmentLength;
	ColorRect fade;
	Vector2 steer, wander = Vector2.Right;
	float orbit = 1f, wanderTimer;
	Enemy boss;
	readonly RandomNumberGenerator rng = new() { Seed = 20261101 };

	const float FadeTime = 0.2f;

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Game.SaveEnabled = false;
		Game.Showcase = true;           // 失焦不暂停
		Game.Ui.Visible = false;
		Game.Pilot = Steer;
		// 16:9 输出时横向扩展视野,而不是加黑边
		GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
		var layer = new CanvasLayer { Layer = 100 };
		AddChild(layer);
		fade = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
		fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(fade);
		stress = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--stress") >= 0;
		if (stress)
		{
			// 每次录都不一样:随机角色、武器、刷怪方向。--seed=N 让实机测帧和录像跑同一局
			rng.Randomize();
			Game.Rng.Randomize();
			foreach (var arg in OS.GetCmdlineUserArgs())
			{
				if (arg.StartsWith("--seed=") && ulong.TryParse(arg.Substring(7), out ulong seed)) { rng.Seed = seed; Game.Rng.Seed = seed; }
				if (arg.StartsWith("--perflog=")) perfPath = arg.Substring(10);
			}
			// 带界面才像实战,性能面板也留着,用数字说明承载量
			Game.Ui.Visible = true;
			// 录像时引擎帧率只是编码速度;面板改为回放同一种子实机运行时测到的帧率
			replay = OS.HasFeature("movie");
			if (replay && perfPath != "" && FileAccess.FileExists(perfPath))
			{
				using var f = FileAccess.Open(perfPath, FileAccess.ModeFlags.Read);
				while (!f.EofReached())
				{
					var parts = f.GetLine().Split(' ');
					if (parts.Length == 2) liveFps.Add((float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1])));
				}
				Game.Ui.FpsSource = ReplayFps;
			}
			BuildStress();
		}
		else BuildSegments();
		Next();
	}

	// 承载展示:一镜到底 20 秒,怪从屏幕外成群涌入,同屏从几十只涨到三百多只
	bool stress;
	float spawnTimer;

	void BuildStress()
	{
		segments.Add(new(20f, () =>
		{
			var g = Game;
			// 七种文具随机去掉一种,其余六件全部满阶
			var types = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
			types.RemoveAt(rng.RandiRange(0, types.Count - 1));
			var weapons = new List<(int, int)>();
			foreach (int type in types) weapons.Add((type, 4));
			Begin(rng.RandiRange(0, 2), rng.RandiRange(14, 18), 0.6f, weapons.ToArray());
			// 后期成型的构筑:攻速、伤害、暴击都堆过
			g.AttackSpeed = 1.5f;
			// 伤害够高,贴身的怪成片倒下,人物周围清出一圈空地,不会被怪海淹没
			g.DamageMult *= 4f;
			g.Regeneration = 5f;
			g.Crit = 0.25f;
			g.Level = 24 + rng.RandiRange(0, 6);
			g.Kills = rng.RandiRange(1800, 2600);
			g.Cash = rng.RandiRange(380, 720);
			g.WaveTime = 45f;
			// 能挨打但不会死,受击闪烁和掉血都是真的
			g.Invulnerable = 0f;
			g.Armor = 30f;
			g.BossSpawned = true;
			g.Ui.ShowHud();
			// 三只首领一开始就都在,各自出招节奏错开
			SpawnBoss(rng.RandfRange(320, 400));
			SpawnBoss(rng.RandfRange(380, 460));
			SpawnBoss(rng.RandfRange(420, 520));
			for (int i = 0; i < 16; i++) Pack(rng.RandiRange(14, 22));
			// 预热:开拍前先在后台跑 8 秒,第一帧就是满屏怪和满屏子弹,像中途切进一场激战,而不是从少到多
			warming = true;
			for (int i = 0; i < 240; i++)
			{
				StressTick(1f / 30f);
				g.Simulate(1f / 30f);
			}
			warming = false;
			g.WaveTime = g.Elapsed + rng.RandfRange(30f, 40f);
		}));
	}

	bool warming;
	float surge;
	int target = 440;

	int bossesSpawned;
	bool replay;
	string perfPath = "";
	float perfLog;
	readonly List<(float T, int Fps)> liveFps = new();
	readonly System.Text.StringBuilder perfOut = new();

	// 实机运行:每 0.25 秒记一次真实帧率,结束时写入 --perflog 文件
	void LogPerf(float dt)
	{
		perfLog += dt;
		if (perfLog < 0.25f) return;
		perfLog = 0f;
		int bullets = 0;
		foreach (var b in Game.Projectiles) if (b.Enemy) bullets++;
		int fps = (int)Engine.GetFramesPerSecond();
		// 引擎帧率按 1 秒平均,开头两秒还带着载入和后台预热的卡顿,不算进战斗帧率
		if (clock > 2.2f) perfOut.Append(clock.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(fps).Append('\n');
		if ((int)(clock * 4f) % 4 == 0) GD.Print($"PERF t={clock:0} 实机FPS={fps} 敌人={Game.Enemies.Count} 敌方弹幕={bullets} 全部弹幕={Game.Projectiles.Count}");
	}

	void SavePerf()
	{
		if (replay || perfPath == "") return;
		using var f = FileAccess.Open(perfPath, FileAccess.ModeFlags.Write);
		f.StoreString(perfOut.ToString());
	}

	int ReplayFps()
	{
		int fps = liveFps[0].Fps;
		foreach (var (t, v) in liveFps)
		{
			if (t > clock) break;
			fps = v;
		}
		return fps;
	}

	// 狂暴(半血以下)的首领出招更快、弹幕更密;血量足够厚,20 秒内打不死
	void SpawnBoss(float distance)
	{
		var g = Game;
		var b = g.SpawnEnemy(7, g.PlayerPos + Vector2.Right.Rotated(rng.Randf() * Mathf.Tau) * distance);
		// 伤害加成很高,血量同步加厚,保证一直在场放弹幕
		b.MaxHp *= 6f;
		b.Hp = b.MaxHp * rng.RandfRange(0.38f, 0.48f);
		bossesSpawned++;
		b.Cooldown = rng.RandfRange(0.3f, 1.3f);
	}

	// 屏幕外随机方向来一群同类怪,偶尔夹一只精英
	void Pack(int size)
	{
		var g = Game;
		float angle = rng.Randf() * Mathf.Tau;
		var center = g.PlayerPos + new Vector2(Mathf.Cos(angle) * rng.RandfRange(1000, 1250), Mathf.Sin(angle) * rng.RandfRange(560, 760));
		int type = rng.RandiRange(0, 6);
		for (int i = 0; i < size; i++)
		{
			var p = center + new Vector2(rng.RandfRange(-130, 130), rng.RandfRange(-95, 95));
			p = p.Clamp(Main.FIELD.Position + new Vector2(30, 40), Main.FIELD.End - new Vector2(30, 30));
			// 每群都混进远程怪(打印机怪、便利贴怪),弹幕才不会断档
			float roll = rng.Randf();
			int kind = roll < 0.65f ? type : (roll < 0.83f ? (rng.Randf() < 0.5f ? 2 : 6) : rng.RandiRange(0, 6));
			g.SpawnEnemy(kind, p, rng.Randf() < 0.06f);
		}
	}

	void StressTick(float dt)
	{
		int alive = 0;
		foreach (var e in Game.Enemies) if (e.Hp > 0f) alive++;
		// 全程维持四百多只,随清怪速度和一波波涌入自然起伏,不做线性增长
		surge -= dt;
		if (surge <= 0f)
		{
			surge = rng.RandfRange(1.5f, 3.5f);
			target = rng.RandiRange(400, 480);
		}
		spawnTimer -= dt;
		if (spawnTimer <= 0f && alive < target)
		{
			spawnTimer = rng.RandfRange(0.06f, 0.25f);
			int need = target - alive;
			Pack(Mathf.Min(need, rng.RandiRange(12, 26)));
		}
		// 出手间隔压短:远程怪 0.6~1.2 秒,首领 0.9~1.3 秒,满屏子弹不断档
		foreach (var e in Game.Enemies)
		{
			if (e.Kind == EKind.Ranged && e.Cooldown > 1.2f) e.Cooldown = rng.RandfRange(0.6f, 1.2f);
			if (e.Kind == EKind.Boss && e.Cooldown > 1.3f) e.Cooldown = rng.RandfRange(0.9f, 1.3f);
		}
		var g = Game;
		// 敌方子弹能飞 9 秒,会越积越多;超过 2300 颗时清掉屏幕外的(看不见,也打不到人)
		int enemyBullets = 0;
		foreach (var b in g.Projectiles) if (b.Enemy) enemyBullets++;
		if (enemyBullets > 2300)
		{
			var p = g.PlayerPos;
			g.Projectiles.RemoveAll(b => b.Enemy && Mathf.Abs(b.P.X - p.X) > 1150f || b.Enemy && Mathf.Abs(b.P.Y - p.Y) > 650f);
		}
		if (!replay && !warming) LogPerf(dt);
		g.Hp = Mathf.Max(g.Hp, g.MaxHp * 0.3f);
	}

	void BuildSegments()
	{
		var g = Game;
		// 1. 开局:实习生拿铅笔 + 美工刀清小怪
		segments.Add(new(6.5f, () =>
		{
			Begin(0, 4, 0.8f, (0, 2), (1, 2));
			Ring(22, 6, 260, 520);
		}));
		// 2. 会计四件文具,怪群从四面围上来
		segments.Add(new(7f, () =>
		{
			Begin(1, 8, 0.74f, (3, 3), (2, 2), (4, 2), (5, 2));
			Ring(46, 7, 240, 620);
		}));
		// 3. 警棍连锁电击
		segments.Add(new(6f, () =>
		{
			Begin(2, 9, 0.8f, (6, 4), (6, 3), (0, 3));
			Ring(34, 7, 180, 480);
		}));
		// 4. 精英怪围攻
		segments.Add(new(6.5f, () =>
		{
			Begin(2, 12, 0.72f, (0, 4), (1, 3), (2, 3), (6, 3), (4, 3));
			int[] kinds = { 3, 5, 2, 1, 4, 3 };
			for (int k = 0; k < kinds.Length; k++)
				g.SpawnEnemy(kinds[k], g.PlayerPos + Vector2.Right.Rotated(k * Mathf.Tau / kinds.Length) * 360f, true);
			Ring(30, 7, 300, 600);
		}));
		// 5. 首领战:复印机首领,残血开打,在片段内击杀
		segments.Add(new(8.5f, () =>
		{
			Begin(1, 10, 0.7f, (0, 4), (1, 4), (2, 4), (3, 3), (5, 4), (6, 4));
			g.BossSpawned = true;
			boss = g.SpawnEnemy(7, g.PlayerPos + new Vector2(300, -60));
			boss.Hp = boss.MaxHp * 0.05f;
			Ring(10, 7, 380, 600);
			g.Sound("boss");
		}));
		// 6. 收尾:满配六件四阶文具割草
		segments.Add(new(9f, () =>
		{
			Begin(0, 16, 0.66f, (0, 4), (1, 4), (2, 4), (3, 4), (5, 4), (6, 4));
			Ring(85, 7, 220, 900);
		}));
	}

	void Begin(int hero, int wave, float zoom, params (int Type, int Tier)[] weapons)
	{
		var g = Game;
		g.HeroIndex = hero;
		g.StartRun();
		g.Wave = wave;
		g.StartWave();
		g.WaveTime = 999f;              // 片段内不结束本波
		g.BannerTimer = 0f;
		g.Invulnerable = 999f;
		g.Cash = 60 + wave * 25;
		g.Level = 2 + wave;
		g.Weapons = new List<Weapon>();
		foreach (var (type, tier) in weapons)
			if (type < g.Balance.Weapons.Count) g.Weapons.Add(g.MakeWeapon(type, tier));
		g.ZoomOverride = zoom;
		steer = Vector2.Zero;
		orbit = rng.Randf() < 0.5f ? 1f : -1f;
	}

	// 以玩家为中心撒一圈怪
	void Ring(int count, int kinds, float near, float far)
	{
		var g = Game;
		for (int i = 0; i < count; i++)
		{
			var p = g.PlayerPos + Vector2.Right.Rotated(rng.Randf() * Mathf.Tau) * rng.RandfRange(near, far);
			p = p.Clamp(Main.FIELD.Position + new Vector2(30, 40), Main.FIELD.End - new Vector2(30, 30));
			g.SpawnEnemy(rng.RandiRange(0, kinds - 1), p);
		}
	}

	void Next()
	{
		index++;
		if (index >= segments.Count)
		{
			if (stress) SavePerf();
			GetTree().Quit();
			return;
		}
		segments[index].Setup();
		segmentLength = segments[index].Seconds;
		clock = 0f;
	}

	public override void _Process(double d)
	{
		if (index < 0 || index >= segments.Count) return;
		var g = Game;
		float dt = (float)d;
		clock += dt;
		if (!stress) g.Hp = g.MaxHp;
		if (g.Phase != "running") g.SetPhase("running");
		// 首尾各留一点淡入淡出,片段之间硬切会闪
		float a = Mathf.Max(1f - clock / FadeTime, (clock - (segmentLength - FadeTime)) / FadeTime);
		fade.Color = new Color(0, 0, 0, Mathf.Clamp(a, 0f, 1f));
		// 保持场面热闹:怪少了就补
		int alive = 0;
		foreach (var e in g.Enemies) if (e.Hp > 0f) alive++;
		if (stress) StressTick(dt);
		else if (alive < 16 && clock < segmentLength - 1.5f) Ring(10, 7, 420, 640);
		if (boss != null && boss.Hp <= 0f)
		{
			GD.Print($"首领在第 {index + 1} 段 {clock:0.0} 秒被击杀");
			boss = null;
		}
		if (clock >= segmentLength) Next();
	}

	// 像人按键盘:只有八个方向,按住一阵才换,偶尔松手站一下,闪避也不是每次都按得上
	Vector2 held;
	float holdTimer;

	Vector2 HumanInput(Vector2 desired, float nearest)
	{
		holdTimer -= 1f / 30f;
		bool danger = nearest < 45f;
		if (holdTimer <= 0f || (danger && rng.Randf() < 0.15f))
		{
			holdTimer = rng.RandfRange(0.22f, 0.75f);
			if (!danger && rng.Randf() < 0.12f) held = Vector2.Zero;
			else if (desired.Length() < 0.1f) held = Vector2.Zero;
			else
			{
				float octant = Mathf.Round(desired.Angle() / (Mathf.Pi / 4f));
				var d = Vector2.Right.Rotated(octant * Mathf.Pi / 4f);
				held = new Vector2(Mathf.Round(d.X), Mathf.Round(d.Y));
			}
		}
		if (danger && Game.DashCooldown <= 0f && rng.Randf() < 0.06f) Game.Dash();
		return held;
	}

	// 自动驾驶:躲开近身怪和敌方弹幕,绕着怪群转圈让武器持续命中,贴边时回中,顺手捡奖金
	Vector2 Steer()
	{
		var g = Game;
		var p = g.PlayerPos;
		var push = Vector2.Zero;
		float nearest = 1e9f;
		bool bossThreat = false;
		foreach (var e in g.Enemies)
		{
			if (e.Hp <= 0f) continue;
			var off = e.Pos - p;
			float dist = off.Length();
			nearest = Mathf.Min(nearest, dist - e.Radius);
			// 首领只在贴身时避让,离太远武器够不着,片段里就打不死
			float reach = e.Type == 7 ? 250f : 230f;
			if (dist < reach && dist > 0.1f)
			{
				float k = 1f - dist / reach;
				push -= off / dist * k * k * (e.Type == 7 ? 1.5f : 1f);
			}
			else if (e.Type == 7 && dist > 260f) push += off / dist * 1.4f;
			if (e.Type == 7 && e.Windup > 0f && dist < 320f) bossThreat = true;
		}
		foreach (var b in g.Projectiles)
		{
			if (!b.Enemy) continue;
			var off = b.P - p;
			float dist = off.Length();
			if (dist < 150f && dist > 0.1f) push -= off / dist * (1f - dist / 150f) * 1.6f;
		}
		var tangent = new Vector2(-push.Y, push.X) * orbit * 0.8f;
		// 镜头到场地边缘会停住,人物不再居中,所以尽量留在中间区域
		var inner = Main.FIELD.Grow(-560f);
		var home = inner.HasPoint(p) ? Vector2.Zero : (Main.FIELD.GetCenter() - p).Normalized() * 1.6f;
		if (stress)
		{
			// 视野更大,可活动范围更小
			var back = Main.FIELD.GetCenter() - p;
			home = back.Length() > 150f ? (back / 220f).LimitLength(3f) : Vector2.Zero;
		}
		var pull = Vector2.Zero;
		float best = 360f;
		foreach (var drop in g.Drops)
		{
			float dist = drop.P.DistanceTo(p);
			if (dist < best) { best = dist; pull = (drop.P - p).Normalized() * 0.5f; }
		}
		wanderTimer -= 1f / 30f;
		if (wanderTimer <= 0f)
		{
			wanderTimer = rng.RandfRange(1.2f, 2.4f);
			wander = Vector2.Right.Rotated(rng.Randf() * Mathf.Tau);
			if (rng.Randf() < 0.25f) orbit = -orbit;
		}
		var desired = push * 2.2f + tangent + home + pull + wander * 0.35f;
		// 怪多时躲避力会盖过回中,人物被挤到场地边上、出镜,所以承载模式回中权重更高
		// 满屏子弹时躲避力会叠加到很大,先封顶再和回中合成
		if (stress) return HumanInput(push.LimitLength(1.2f) * 1.3f + tangent.LimitLength(0.8f) * 0.6f + home * 2.2f + wander * 0.3f, nearest);
		steer = steer.Lerp(desired.LimitLength(1f), 0.14f);
		if ((nearest < 34f || bossThreat) && rng.Randf() < 0.2f) g.Dash();
		return steer.Length() < 0.08f ? Vector2.Zero : steer;
	}
}
