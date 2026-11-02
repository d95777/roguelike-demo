using System.Collections.Generic;
using Godot;

namespace RogueDemo;

public partial class Main : Node2D
{
	public static readonly Rect2 FIELD = new Rect2(0, 0, 2600, 1600);
	static readonly Rect2 ProjectileBounds = FIELD.Grow(80);
	// 相机拉远后角色显得更小、视野更大;战斗距离仍用世界单位，不随缩放改动。
	public const float VIEW_ZOOM = 0.65f;
	// 装饰性特效（纸屑、扬尘、符号）的数量上限;超过后不再生成，伤害数字单独放宽。
	public const int FX_SOFT_CAP = 260;
	public const int FX_TEXT_CAP = 360;
	// 地上的掉落物数量上限;超过后新产生的掉落直接结算给玩家（奖金和经验照拿，只是不再生成地面实体）。
	public const int DROP_CAP = 150;
	public static readonly Color JADE = new Color("f2b04c");
	public static readonly Color GOLD = new Color("e8c487");
	static readonly Color DangerRing = new Color("ff6a5a");

	public Balance Balance;
	public string Phase = "title";
	public string PreviousPhase = "running";
	public int HeroIndex;
	// 当前角色的特点(开局时从数据表读入)
	public float XpMult = 1f, CashMult = 1f, PriceMult = 1f, LevelDamage, DashCooldownMax = 3f, DashInvuln = 0.28f;
	public bool DashShock;
	float xpCarry, cashCarry;
	public Actor Player;
	public Vector2 PlayerPos;
	public Node2D Actors;
	public Interface Ui;
	public List<Weapon> Weapons = new();
	public List<Enemy> Enemies = new();
	public List<Projectile> Projectiles = new();
	public List<Drop> Drops = new();
	public List<Fx> Effects = new();
	public List<Warning> Warnings = new();
	readonly List<Actor> dyingActors = new();	public Dictionary<string, Texture2D> Textures = new();
	public Media Media;
	public RandomNumberGenerator Rng = new();
	public float Hp = 100f, MaxHp = 100f, Speed = 225f, DamageMult = 1f, AttackSpeed = 1f, Armor, Crit = 0.05f, PickupRange = 88f, Regeneration;
	public int Cash, Xp, Level = 1, NextXp = 10, PendingUpgrades, Wave = 1;
	public float WaveTime = 25f, Elapsed, TotalTime;
	public int Kills, RunDamage;
	public float SpawnTimer, Invulnerable, DashTimer, DashCooldown;
	float spawnCarry;
	public Vector2 MoveDirection = Vector2.Right;
	public Vector2 LastInput;
	public bool BossSpawned, BossDefeated;
	int nextEnemyId = 1;
	public List<Offer> Offers = new();
	public List<int> LockedOffers = new();
	public int RefreshCost = 5;
	public List<Upgrade> Upgrades = new();
	public bool Muted;
	public int BestWave, Victories, BestKills;
	float ambientClock;
	readonly List<(Vector2 p, float h, float kind)> floorMarks = new();
	int sepTick;
	GradientTexture2D glowTex;
	readonly List<Texture2D> dropTex = new();
	float sepStrength = 1f;
	bool neOk;
	Vector2[] nePos = new Vector2[0];
	byte[] neDead = new byte[0];
	int neCount;
	readonly RandomNumberGenerator fxRng = new();
	public Dictionary<int, int> Items = new();
	public Camera2D Camera;
	ImageTexture floorTexture;
	// 每一波开始时的大横幅(波次、提示),淡入淡出
	public string BannerTitle = "", BannerSub = "";
	public float BannerTimer;
	public const float BannerLen = 2.4f;
	int waveStartKills;
	public string Notice = "";
	public float NoticeTimer;
	string capturePath = "";
	float captureTime;
	public bool Showcase;
	// 录像模式:自动驾驶的移动方向与镜头缩放(见 Recorder.cs)
	public System.Func<Vector2> Pilot;
	public float ZoomOverride;
	public bool SaveEnabled = true;
	int spriteFx;
	Fx holdFx;          // 调试截图用:让一个特效停在固定进度
	Enemy holdEnemy;
	bool holdBolt;
	// 分段耗时(毫秒,累计),基准测试读取后清零
	public static readonly double[] Prof = new double[8];
	public static readonly string[] ProfNames = { "敌人更新", "敌人分离", "武器索敌开火", "弹道更新", "角色动画", "节点写回", "整个Simulate", "整个_Process" };
	static long ProfStart() => System.Diagnostics.Stopwatch.GetTimestamp();
	static void ProfEnd(int i, long t0) => Prof[i] += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
	ShadowLayer shadowLayer;
	WeaponBackLayer weaponBack;
	float[] sepRad = new float[0];
	Vector2[] sepPos = new Vector2[0];
	Vector2[] sepPush = new Vector2[0];
	readonly Dictionary<int, List<int>> sepGrid = new();
	readonly Stack<List<int>> sepPool = new();
	static readonly int[] SepForward = { 1000, -1000 + 1, 1, 1000 + 1 };   // 右、左下、下、右下：配合同格内配对，覆盖全部 8 邻域

	public override void _Ready()
	{
		Balance = Balance.Load("res://data/balance.json");
		Rng.Randomize();
		foreach (var w in Balance.Weapons)
		{
			var original = GD.Load<Texture2D>("res://assets/weapons/" + w.Image);
			Textures[w.Image] = new AtlasTexture { Atlas = original, Region = (Rect2)original.GetImage().GetUsedRect() };
		}
		foreach (var it in Balance.Items)
			if (ResourceLoader.Exists("res://assets/items/" + it.Icon)) Textures[it.Icon] = GD.Load<Texture2D>("res://assets/items/" + it.Icon);
		Textures["arrow"] = GD.Load<Texture2D>("res://assets/projectiles/arrow.png");
		fxRng.Randomize();
		foreach (var tint in new[] { new Color("ffd45e"), new Color("d69a89") })
		{
			var img = Image.CreateEmpty(24, 24, false, Image.Format.Rgba8);
			for (int py = 0; py < 24; py++)
			{
				for (int px = 0; px < 24; px++)
				{
					float dx = Mathf.Abs(px - 11.5f), dy = Mathf.Abs(py - 11.5f);
					float dist = new Vector2(px - 11.5f, py - 11.5f).Length();
					if (dx / 4f + dy / 6f <= 1f) img.SetPixel(px, py, tint);
					else if (dist < 11f) img.SetPixel(px, py, new Color(tint, 0.09f * (1f - dist / 11f) * 3f));
				}
			}
			dropTex.Add(ImageTexture.CreateFromImage(img));
		}
		// 回血道具:红色爱心带白边,比金币大得多,远处也能一眼认出
		{
			const int hs = 44;
			var himg = Image.CreateEmpty(hs, hs, false, Image.Format.Rgba8);
			for (int py = 0; py < hs; py++)
			{
				for (int px = 0; px < hs; px++)
				{
					float x = (px - hs / 2f + 0.5f) / (hs * 0.36f), y = -(py - hs / 2f + 0.5f) / (hs * 0.36f) + 0.15f;
					float v = Mathf.Pow(x * x + y * y - 1f, 3f) - x * x * y * y * y;
					if (v <= -0.02f) himg.SetPixel(px, py, new Color(y > 0.35f && x < -0.15f && x > -0.75f ? "ff9aa8" : "e8283c"));
					else if (v <= 0.22f) himg.SetPixel(px, py, new Color("fff4f4"));
				}
			}
			dropTex[1] = ImageTexture.CreateFromImage(himg);
		}
		// 弹道的红色危险光晕用预先生成的径向渐变贴图，比 DrawCircle 现场生成多边形便宜得多
		var gradient = new Gradient();
		gradient.SetColor(0, new Color(1, 1, 1, 1));
		gradient.SetColor(1, new Color(1, 1, 1, 0));
		glowTex = new GradientTexture2D { Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64 };
		foreach (var pn in new[] { "Paper", "Note", "Ink", "Orb" })
		{
			string ppath = $"res://assets/projectiles/Proj_{pn}.png";
			if (ResourceLoader.Exists(ppath)) Textures["proj_" + pn.ToLower()] = GD.Load<Texture2D>(ppath);
		}
		foreach (var n in new[] { "sky", "far", "mid" }) Textures[n] = GD.Load<Texture2D>($"res://assets/maps/{n}.png");
		var floorLayer = new FloorLayer { Game = this, ZIndex = -5 };
		AddChild(floorLayer);
		shadowLayer = new ShadowLayer { Game = this, ZIndex = -2 };
		AddChild(shadowLayer);
		weaponBack = new WeaponBackLayer { Game = this, ZIndex = -1 };
		AddChild(weaponBack);
		Actors = new Node2D { YSortEnabled = true, ZIndex = -1 };
		AddChild(Actors);
		BuildBulletBatches();
		Media = new Media();
		AddChild(Media);
		Camera = new Camera2D { LimitLeft = (int)FIELD.Position.X, LimitTop = (int)FIELD.Position.Y, LimitRight = (int)FIELD.End.X, LimitBottom = (int)FIELD.End.Y, Position = new Vector2(640, 400) };
		AddChild(Camera);
		Camera.MakeCurrent();
		for (int i = 0; i < 900; i++)
			floorMarks.Add((new Vector2(Rng.RandfRange(20, FIELD.End.X - 20), Rng.RandfRange(20, FIELD.End.Y - 20)), Rng.RandfRange(2, 8), Rng.Randf()));
		BuildFloor();
		LoadRecord();
		Ui = new Interface { Game = this };
		AddChild(Ui);
		Ui.ShowTitle();
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--capture=")) capturePath = arg.Substring("--capture=".Length);
			if (arg == "--showcase") Showcase = true;
			if (arg == "--test") SaveEnabled = false;
			if (arg == "--bench") AddChild(new Bench { Game = this });
			if (arg == "--selftest") AddChild(new SelfTest { Game = this });
			if (arg == "--mediatest") AddChild(new MediaTest { Game = this });
			if (arg == "--facingtest") AddChild(new FacingTest { Game = this });
			if (arg == "--uitest") AddChild(new UiTest { Game = this });
			if (arg == "--idletest") AddChild(new IdleTest { Game = this });
			if (arg == "--balancetest") AddChild(new BalanceTest { Game = this });
			if (arg == "--record") AddChild(new Recorder { Game = this });
		}
		if (Showcase)
		{
			StartRun();
			Wave = 8;
			WaveTime = 40;
			Announce("第 08 波 · 加班求生");
			Cash = 126;
			Weapons = new List<Weapon> { MakeWeapon(0, 2), MakeWeapon(1, 1), MakeWeapon(2, 2), MakeWeapon(3, 1) };
			for (int i = 0; i < 18; i++)
				SpawnEnemy(i % 7, FIELD.GetCenter() + new Vector2(Rng.RandfRange(-560, 560), Rng.RandfRange(-330, 330)));
			Invulnerable = 999f;
			Ui.ShowHud();
			// 调试截图:--shop / --upgrade 直接进入对应界面
			foreach (var a in OS.GetCmdlineUserArgs())
			{
				if (a == "--merge") { Weapons.Add(MakeWeapon(4, 1)); Cash = 200; GenerateOffers(); Offers[1] = new Offer { Type = 4, Tier = 1, Price = 28 }; SetPhase("shop"); Ui.ShowShop(); }
				if (a == "--shop") { Weapons.Add(MakeWeapon(4, 3)); ApplyItem(0); ApplyItem(3); GenerateOffers(); SetPhase("shop"); Ui.ShowShop(); }
				if (a == "--pick") { HeroIndex = 2; BeginRun(); }
				if (a == "--bolt") { HeroIndex = 2; Weapons = new List<Weapon> { MakeWeapon(6, 2) }; for (int k = 1; k <= 4; k++) SpawnEnemy(k - 1, PlayerPos + new Vector2(70f * k + 30f, -10f * k + 14f * (k % 2))).Cooldown = 1e9f; holdBolt = true; }
				if (a == "--heart") { Drops.Add(new Drop { P = PlayerPos + new Vector2(300, -140), Heart = true }); Drops.Add(new Drop { P = PlayerPos + new Vector2(-280, -120), Value = 2, Xp = 1 }); PickupRange = 0f; }
				if (a == "--tip") { Weapons.Add(MakeWeapon(4, 1)); ApplyItem(0); ApplyItem(0); Armor = 6; GenerateOffers(); SetPhase("shop"); Ui.ShowShop(); Ui.PreviewTip(false); }
				if (a == "--tipstat") { Armor = 6; GenerateOffers(); SetPhase("shop"); Ui.ShowShop(); Ui.PreviewTip(true); }
				if (a == "--elite")
				{
					Wave = 12;
					foreach (var en in Enemies) en.Node.QueueFree();
					Enemies.Clear();
					Invulnerable = 999f;
					int[] kinds = { 3, 5, 2, 1, 4 };
					for (int k = 0; k < kinds.Length; k++)
					{
						var el = SpawnEnemy(kinds[k], PlayerPos + new Vector2(-330 + k * 165, -230 + (k % 2) * 330), k < 3);
						el.Windup = 0.45f;
						el.Dir = (PlayerPos - el.Pos).Normalized();
						el.Cooldown = 1e9f;
					}
				}
				if (a == "--flood")
				{
					for (int i = 0; i < 400; i++)
					{
						var sp = (PSprite)Rng.RandiRange(1, 4);
						Projectiles.Add(new Projectile { P = FIELD.GetCenter() + U.Dir(Rng.Randf() * Mathf.Tau) * Rng.RandfRange(60, 420), V = U.Dir(Rng.Randf() * Mathf.Tau) * 8f, Life = 1e6f, Damage = 0f, Enemy = true, R = 7f, Sprite = sp, Size = sp == PSprite.Orb ? 88f : 66f, Spin = Rng.Randf() * Mathf.Tau });
					}
				}
				if (a == "--six") { Weapons = new List<Weapon> { MakeWeapon(0, 4), MakeWeapon(1, 3), MakeWeapon(2, 4), MakeWeapon(3, 2), MakeWeapon(4, 3), MakeWeapon(5, 4) }; }
				if (a == "--pause") { PreviousPhase = "running"; SetPhase("paused"); Ui.ShowPause(); }
				if (a == "--result") { for (int k = 0; k < Balance.Items.Count; k++) Items[k] = 1 + k % 4; Kills = 237; RunDamage = 48210; Level = 14; Wave = 20; EndRun(true); }
				if (a == "--victory") { Wave = Balance.WaveCount; Elapsed = 1e6f; BossDefeated = true; BeginVictory(); }
				if (a == "--boss") { Wave = 10; var bs = SpawnEnemy(7, PlayerPos + new Vector2(260, -60)); bs.Windup = 0.5f; bs.Pattern = 2; bs.Dir = (PlayerPos - bs.Pos).Normalized(); bs.Hp = bs.MaxHp * 0.6f; }
				if (a == "--clear") { Wave = 7; Kills = 38; waveStartKills = 0; ClearBonus = 24; PendingUpgrades = 1; BeginWaveClear(); }
				if (a == "--lancefx")
				{
					Weapons = new List<Weapon> { MakeWeapon(0, 3) };
					Enemies.RemoveAll(en => { en.Node.QueueFree(); return true; });
					for (int k = 1; k <= 3; k++) SpawnEnemy(k, PlayerPos + new Vector2(70f * k + 40f, -18f + k * 4f)).Cooldown = 1e9f;
					holdFx = new Fx { Kind = FxKind.Lance, P = PlayerPos + new Vector2(0, -22), Angle = 0f, R = 270f, Count = 3, N = 0, Color = new Color("ffd24a"), Life = 0.12f, Max = 0.3f };
				}
				if (a == "--charge")
				{
					Enemies.RemoveAll(en => { en.Node.QueueFree(); return true; });
					holdEnemy = SpawnEnemy(3, PlayerPos + new Vector2(-440f, 0f));
					holdEnemy.Cooldown = 1e9f;
					holdEnemy.Dir = Vector2.Right;
				}
				if (a == "--upgrade") { PendingUpgrades = 1; OpenUpgrade(); }
			}
		}
		QueueRedraw();
	}

	public Weapon MakeWeapon(int index, int tier = 1) => new Weapon { Type = index, Tier = tier, Cd = Rng.RandfRange(0f, 0.4f) };

	public void ClearWorld()
	{
		Media.StopAll();
		foreach (var child in Actors.GetChildren()) child.QueueFree();
		Player = null;
		Enemies.Clear();
		Projectiles.Clear();
		Drops.Clear();
		Effects.Clear();
		Warnings.Clear();
		pendingSpawns.Clear();
		dyingActors.Clear();
		spriteFx = 0;
	}

	public void StartRun()
	{
		ClearWorld();
		var h = Balance.Heroes[HeroIndex];
		MaxHp = h.Hp;
		Hp = MaxHp;
		Speed = h.Speed;
		DamageMult = h.Damage;
		AttackSpeed = 1f;
		Armor = h.Armor;
		Crit = 0.05f;
		PickupRange = h.Pickup;
		XpMult = h.XpMult;
		CashMult = h.CashMult;
		PriceMult = h.PriceMult;
		LevelDamage = h.LevelDamage;
		DashCooldownMax = h.DashCooldown;
		DashInvuln = h.DashInvuln;
		DashShock = h.DashShockwave;
		xpCarry = 0f;
		cashCarry = 0f;
		Regeneration = 0f;
		Cash = 0;
		Xp = 0;
		Level = 1;
		NextXp = 10;
		PendingUpgrades = 0;
		Wave = 1;
		Kills = 0;
		RunDamage = 0;
		TotalTime = 0f;
		Invulnerable = 1f;
		DashTimer = 0f;
		DashCooldown = 0f;
		Weapons = new List<Weapon> { MakeWeapon(h.Weapon) };
		Items.Clear();
		Offers.Clear();
		LockedOffers.Clear();
		Player = new Actor();
		Actors.AddChild(Player);
		Player.Setup(h.Model, 102f);
		Player.Smooth = true;
		PlayerPos = FIELD.GetCenter();
		Player.Place(PlayerPos);
		StartWave();
	}

	// 新一局:先布置好第一波,再让玩家选起始文具,选完才开始计时
	public void BeginRun()
	{
		StartRun();
		SetPhase("pick");
		Ui.ShowPickWeapon();
	}

	// 角色专属文具以正式级开局,其余文具从实习级开始
	public void ChooseStartWeapon(int type)
	{
		if (Phase != "pick" || type >= Balance.Weapons.Count) return;
		if (type < 0) type = Rng.RandiRange(0, Balance.Weapons.Count - 1);
		bool signature = type == Balance.Heroes[HeroIndex].Weapon;
		Weapons = new List<Weapon> { MakeWeapon(type, signature ? 2 : 1) };
		Sound("purchase");
		SetPhase("running");
		Ui.ShowHud();
	}

	public void SetPhase(string value)
	{
		Phase = value;
		Media.SetPaused(Phase == "paused");
		if (Phase == "title" || Phase == "gameover" || Phase == "win") Media.StopAll();
		UpdateMusic();
		Actors.ProcessMode = Phase == "running" ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
	}

	void UpdateMusic()
	{
		switch (Phase)
		{
			case "title": case "pick": Media.PlayMusic("menu"); break;
			case "running": case "paused": Media.PlayMusic(Wave % 5 == 0 ? "boss" : "battle"); break;
			case "shop": case "upgrade": case "clear": Media.PlayMusic("battle", -9f); break;
			default: Media.PlayMusic(null); break;
		}
	}

	public void StartWave()
	{
		foreach (var e in Enemies) e.Node.QueueFree();
		Enemies.Clear();
		Projectiles.Clear();
		Warnings.Clear();
		pendingSpawns.Clear();
		Effects.Clear();
		spriteFx = 0;
		Elapsed = 0f;
		WaveTime = Mathf.Min(Balance.MaxWaveSeconds, Balance.BaseWaveSeconds + (Wave - 1) * Balance.ExtraSecondsPerWave);
		SpawnTimer = 0.5f;
		BossSpawned = false;
		BossDefeated = false;
		Invulnerable = 1f;
		Hp = Mathf.Min(MaxHp, Hp + MaxHp * 0.12f);
		RefreshCost = 8 + Wave * 2;
		waveStartKills = Kills;
		BannerTitle = Wave == Balance.WaveCount ? "最终波" : $"第 {Wave} 波";
		BannerSub = Wave == Balance.WaveCount ? "击败首领，熬过去就能下班" : (Wave % 5 == 0 ? "首领降临 · 避开红色预警区" : "怪物来袭");
		BannerTimer = BannerLen;
		SetPhase("running");
		if (Wave % 5 != 0) Sound("wave_start");
		Ui.ShowHud();
		Announce(Wave == Balance.WaveCount ? "最终波 · 击败首领，熬过这一波就能下班" : $"第 {Wave:00} 波 · {(Wave % 5 == 0 ? "首领降临" : "怪物来袭")}");
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
		var code = key.Keycode;
		if (code == Key.F11)
		{
			bool full = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
			DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
		}
		if (code == Key.M) ToggleMute();
		if (code == Key.F3) Ui.TogglePerf();
		if (code == Key.Escape || code == Key.P)
		{
			if (Phase == "paused") Resume();
			else if (Phase == "running" || Phase == "shop" || Phase == "upgrade")
			{
				PreviousPhase = Phase;
				SetPhase("paused");
				Ui.ShowPause();
			}
		}
		if (code == Key.Space) Dash();
		if (code == Key.Enter && Phase == "title") BeginRun();
	}

	public void Dash()
	{
		if (Phase != "running" || DashCooldown > 0f) return;
		DashTimer = 0.18f;
		DashCooldown = DashCooldownMax;
		Invulnerable = Mathf.Max(Invulnerable, DashInvuln);
		Sound("dash");
		if (DashShock) DashShockwave();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationApplicationFocusOut && Phase == "running" && !Showcase)
		{
			PreviousPhase = Phase;
			SetPhase("paused");
			Ui?.ShowPause();
		}
	}

	// 保安:闪避时冲击波撞开周围的敌人(首领不受影响)
	public void DashShockwave()
	{
		foreach (var e in Enemies)
		{
			if (e.Hp <= 0f || e.Type == 7) continue;
			var away = e.Pos - PlayerPos;
			float d = away.Length();
			if (d > 150f) continue;
			e.Pos += (d > 1f ? away / d : Vector2.Right) * (90f * (1f - d / 220f));
			e.Hit = 0.09f;
		}
		Effects.Add(new Fx { Kind = FxKind.Ring, P = PlayerPos + new Vector2(0, -20), R = 190f, Color = JADE, Life = 0.4f, Max = 0.4f });
		Shake(3f);
	}

	// 奖金与经验按角色倍率结算,小数部分累积,不丢失
	public int GainCash(int amount)
	{
		cashCarry += amount * CashMult;
		int whole = (int)cashCarry;
		cashCarry -= whole;
		Cash += whole;
		return whole;
	}

	int GainXp(int amount)
	{
		xpCarry += amount * XpMult;
		int whole = (int)xpCarry;
		xpCarry -= whole;
		return whole;
	}

	public void Resume()
	{
		SetPhase(PreviousPhase);
		switch (Phase)
		{
			case "running": Ui.ShowHud(); break;
			case "shop": Ui.ShowShop(); break;
			case "upgrade": Ui.ShowUpgrades(); break;
		}
	}

	public void ToggleMute()
	{
		Muted = !Muted;
		AudioServer.SetBusMute(0, Muted);
		Media.SetMuted(Muted);
		if (Phase == "paused") Ui.ShowPause();
	}

	public override void _Process(double d)
	{
		float delta = (float)d;
		ambientClock += delta;
		NoticeTimer = Mathf.Max(0f, NoticeTimer - delta);
		if (Phase == "running") BannerTimer = Mathf.Max(0f, BannerTimer - delta);
		if (Phase == "running")
		{
			long tp = ProfStart();
			Simulate(Mathf.Min(delta, 0.04f));
			ProfEnd(6, tp);
			tp = ProfStart();
			TickActors(delta);
			ProfEnd(4, tp);
		}
		if (Ui != null)
		{
			Ui.UpdateHud();
			Ui.UpdatePerf(delta);
		}
		if (holdFx != null)
		{
			holdFx.Life = 0.12f;
			if (Weapons.Count > 0) { Weapons[0].Flash = LanceFlash * 0.7f; Weapons[0].LanceAngle = 0f; }
			if (!Effects.Contains(holdFx)) Effects.Add(holdFx);
		}
		if (holdEnemy != null) { holdEnemy.Windup = 0.3f; holdEnemy.Dir = Vector2.Right; }
		if (holdBolt && Enemies.Count >= 3)
		{
			Effects.RemoveAll(f => f.Kind == FxKind.Bolt);
			Weapons[0].Flash = 0.1f; Weapons[0].Angle = 0f;
			var org = PlayerPos + new Vector2(0, -22);
			Effects.Add(new Fx { Kind = FxKind.Bolt, P = org, End = Enemies[0].Pos + new Vector2(0, -18), N = 1, Color = Colors.White, Life = 0.12f, Max = 0.26f });
			for (int k = 0; k < Enemies.Count - 1; k++)
				Effects.Add(new Fx { Kind = FxKind.Bolt, P = Enemies[k].Pos + new Vector2(0, -18), End = Enemies[k + 1].Pos + new Vector2(0, -18), N = 3 + k, Color = Colors.White, Life = 0.12f, Max = 0.26f });
		}
		if (Phase == "victory") VictoryTick(delta);
		if (Phase == "clear") ClearTick(delta);
		UpdateCamera();
		if (shake > 0f)
		{
			shake = Mathf.Max(0f, shake - 40f * delta);
			Camera.Offset = new Vector2(fxRng.RandfRange(-shake, shake), fxRng.RandfRange(-shake, shake));
		}
		else if (Camera.Offset != Vector2.Zero) Camera.Offset = Vector2.Zero;
		UpdateBulletBatches();
		weaponBack.QueueRedraw();
		shadowLayer.QueueRedraw();
		QueueRedraw();
		if (capturePath != "")
		{
			captureTime += delta;
			if (captureTime > 2f)
			{
				string target = capturePath;
				capturePath = "";
				Capture(target);
			}
		}
	}

	async void Capture(string target)
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		GetViewport().GetTexture().GetImage().SavePng(target);
		GD.Print("截图已保存:", target);
	}

	// 所有角色的程序动画统一在这里推进
	public void TickActors(float delta)
	{
		Actor.AnimStride = Enemies.Count > 120 ? 2 : 1;
		if (Player != null && IsInstanceValid(Player)) Player.Tick(delta);
		foreach (var e in Enemies)
			if (e.Hp > 0f) e.Node.Tick(delta);
		for (int i = dyingActors.Count - 1; i >= 0; i--)
		{
			var a = dyingActors[i];
			if (!IsInstanceValid(a) || !a.Tick(delta)) dyingActors.RemoveAt(i);
		}
	}

	void BuildFloor()
	{
		// 地砖和地面标记在启动时一次性烘焙进贴图，战斗中地板层不再逐帧绘制。
		var image = Image.CreateEmpty((int)FIELD.Size.X, (int)FIELD.Size.Y, false, Image.Format.Rgb8);
		image.Fill(new Color("4b4943"));
		var bounds = new Rect2I(0, 0, (int)FIELD.Size.X, (int)FIELD.Size.Y);
		var tileA = new Color("635f57");
		var tileB = new Color("57554e");
		for (int y = 0; y < (int)(FIELD.Size.Y / 82) + 1; y++)
		{
			for (int x = 0; x < (int)(FIELD.Size.X / 92) + 1; x++)
			{
				var tile = new Rect2I(x * 92 + (y % 2) * 16, y * 82, 87, 77).Intersection(bounds);
				if (tile.Size.X <= 0 || tile.Size.Y <= 0) continue;
				image.FillRect(tile, (x + y) % 3 == 0 ? tileA : tileB);
			}
		}
		foreach (var mark in floorMarks)
		{
			var at = new Vector2I((int)mark.p.X, (int)mark.p.Y);
			if (!bounds.HasPoint(at)) continue;
			var baseColor = image.GetPixelv(at);
			int height = (int)mark.h;
			if (mark.kind < 0.40f)
			{
				var light = baseColor.Lerp(new Color(0.80f, 0.78f, 0.70f), 0.14f);
				image.FillRect(new Rect2I(at.X, at.Y - height, 1, height).Intersection(bounds), light);
				image.FillRect(new Rect2I(at.X + 3, at.Y - (int)(height * 0.8f), 1, (int)(height * 0.8f)).Intersection(bounds), light);
			}
			else
			{
				int radius = Mathf.Max(1, (int)(mark.h * 0.35f));
				image.FillRect(new Rect2I(at.X - radius, at.Y - radius, radius * 2 + 1, radius * 2 + 1).Intersection(bounds), baseColor.Darkened(0.15f));
			}
		}
		floorTexture = ImageTexture.CreateFromImage(image);
	}

	public void UpdateCamera()
	{
		if (Camera == null) return;
		if (Phase == "title" || Player == null || !IsInstanceValid(Player))
		{
			Camera.Zoom = Vector2.One;
			Camera.Position = new Vector2(640, 400);
		}
		else
		{
			Camera.Zoom = Vector2.One * (ZoomOverride > 0f ? ZoomOverride : VIEW_ZOOM);
			Camera.Position = PlayerPos;
		}
	}

	// 逻辑位置写回节点：一帧只写一次，节点属性的读写开销比脚本内的运算大得多
	public void SyncNodes()
	{
		if (Player != null && IsInstanceValid(Player)) Player.Pos = PlayerPos;
		foreach (var e in Enemies) e.Node.Pos = e.Pos;
	}

	public void Simulate(float dt)
	{
		Elapsed += dt;
		TotalTime += dt;
		Invulnerable = Mathf.Max(0f, Invulnerable - dt);
		DashCooldown = Mathf.Max(0f, DashCooldown - dt);
		DashTimer = Mathf.Max(0f, DashTimer - dt);
		Hp = Mathf.Min(MaxHp, Hp + Regeneration * dt);
		float right = (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) ? 1f : 0f;
		float left = (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) ? 1f : 0f;
		float down = (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) ? 1f : 0f;
		float up = (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) ? 1f : 0f;
		var direction = new Vector2(right - left, down - up);
		if (Pilot != null) direction = Pilot();
		else if (Showcase) direction = new Vector2(Mathf.Cos(TotalTime), Mathf.Sin(TotalTime)) * 0.5f;
		LastInput = direction;
		if (direction.LengthSquared() > 0f)
		{
			MoveDirection = direction.Normalized();
			Player.Face(direction.X);
			Player.Locomotion(true);
		}
		else Player.Locomotion(false);
		PlayerPos += (DashTimer > 0f ? MoveDirection * Speed * 3.2f : direction.Normalized() * Speed) * dt;
		PlayerPos = PlayerPos.Clamp(FIELD.Position + new Vector2(18, 25), FIELD.End - new Vector2(18, 12));
		Player.Lunge = DashTimer > 0f;
		Player.Lean = 0.07f;
		// 演示/录像用的超长无敌不闪烁
		Player.Alpha = Invulnerable > 0f && Invulnerable < 100f && TotalTime % 0.12f < 0.06f ? 0.5f : 1f;
		if (DashTimer > 0f)
			Effects.Add(new Fx { Kind = FxKind.Ring, P = PlayerPos, R = 20f, Color = JADE, Life = 0.18f, Max = 0.18f });
		SpawnTimer -= dt;
		if (SpawnTimer <= 0f && Elapsed < WaveTime)
		{
			// 刷怪速率随波次平滑增长(每秒几只由 balance.json 决定),不再在 5 的倍数波突然翻倍
			SpawnTimer = 0.5f;
			spawnCarry += Balance.SpawnRate(Wave) * 0.5f;
			int spawnNow = (int)spawnCarry;
			spawnCarry -= spawnNow;
			for (int i = 0; i < spawnNow; i++)
			{
				if (Enemies.Count + Warnings.Count >= Balance.MaxEnemies) continue;
				int type = Rng.RandiRange(0, Mathf.Min(6, 1 + Wave / 2));
				// 第 5 波起出现精英,波次越高越常见
				bool elite = Wave >= 5 && Rng.Randf() < Mathf.Min(0.20f, 0.02f + 0.010f * Wave);
				QueueEnemy(type, elite);
				// 第 6 波起鼠标怪成群出现
				if (type == 1 && !elite && Wave >= 6)
					for (int k = 0; k < 3 && Enemies.Count + Warnings.Count < Balance.MaxEnemies; k++) QueueEnemy(1);
			}
		}
		if (Wave % 5 == 0 && Elapsed > 3f && !BossSpawned)
		{
			BossSpawned = true;
			QueueEnemy(7);
			Announce("复印机首领 · 避开红色预警区");
			Sound("boss");
		}
		foreach (var w in Warnings)
		{
			w.Time -= dt;
			if (w.Time <= 0f) SpawnEnemy(w.Type, w.P, w.Elite);
		}
		Warnings.RemoveAll(static w => w.Time <= 0f);
		foreach (var (pt, pp) in pendingSpawns)
			if (Enemies.Count < Balance.MaxEnemies + 30) SpawnEnemy(pt, pp);
		pendingSpawns.Clear();
		long t0 = ProfStart();
		UpdateEnemies(dt);
		ProfEnd(0, t0);
		if (Phase != "running") return;
		t0 = ProfStart();
		UpdateWeapons(dt);
		ProfEnd(2, t0);
		t0 = ProfStart();
		UpdateProjectiles(dt);
		ProfEnd(3, t0);
		if (Phase != "running") return;
		UpdateDrops(dt);
		UpdateEffects(dt);
		long tn = ProfStart();
		SyncNodes();
		ProfEnd(5, tn);
		if (Elapsed >= WaveTime && (Wave % 5 != 0 || BossDefeated)) FinishWave();
	}

	// 分裂、召唤产生的小怪先排队,在敌人更新之前统一生成,避免遍历时修改列表
	readonly List<(int Type, Vector2 P)> pendingSpawns = new();

	// 冲锋:约 390 像素(原来 175),预警区长度与实际距离一致
	public const float ChargeSpeed = 640f, ChargeTime = 0.95f;

	// 特性怪:盆栽怪周期性治疗附近的怪物,文件柜怪周期性召唤鼠标怪
	void TraitTick(Enemy e, float dt)
	{
		if (e.Type == 4 && Wave >= 4)
		{
			e.AuraTimer -= dt;
			if (e.AuraTimer > 0f) return;
			e.AuraTimer = 1.4f;
			foreach (var o in Enemies)
			{
				if (o == e || o.Hp <= 0f || o.Hp >= o.MaxHp || o.Type == 7) continue;
				if (o.Pos.DistanceTo(e.Pos) < 170f) o.Hp = Mathf.Min(o.MaxHp, o.Hp + o.MaxHp * 0.05f);
			}
			if (Effects.Count < FX_SOFT_CAP) Effects.Add(new Fx { Kind = FxKind.Ring, P = e.Pos + new Vector2(0, -16), R = 170f, Color = new Color(0.4f, 1f, 0.5f), Life = 0.5f, Max = 0.5f });
		}
		else if (e.Type == 5 && Wave >= 8)
		{
			e.AbilityTimer -= dt;
			if (e.AbilityTimer > 0f) return;
			e.AbilityTimer = 7f;
			pendingSpawns.Add((1, e.Pos + new Vector2(-34, 6)));
			pendingSpawns.Add((1, e.Pos + new Vector2(34, 6)));
			if (Effects.Count < FX_SOFT_CAP) Effects.Add(new Fx { Kind = FxKind.Puff, P = e.Pos + new Vector2(0, -20), R = 30f, Color = Colors.White, Life = 0.4f, Max = 0.4f });
		}
	}

	// 精英专属技能:远程精英打出五连扇形,近战精英震地(范围伤害 + 一圈弹幕)
	void EliteAttack(Enemy e)
	{
		var from = e.Pos + new Vector2(0, -18);
		if (e.Kind == EKind.Ranged)
		{
			for (int i = 0; i < 5; i++) AddEnemyBullet(from, e.Dir.Rotated((i - 2) * 0.22f) * 210f, e.Damage, PSprite.Orb, 70f);
		}
		else
		{
			if (PlayerPos.DistanceTo(e.Pos) < 150f) HurtPlayer(e.Damage * 1.2f);
			for (int i = 0; i < 10; i++) AddEnemyBullet(from, U.Dir(Mathf.Tau * i / 10f) * 180f, e.Damage * 0.8f, PSprite.Orb, 70f);
			Effects.Add(new Fx { Kind = FxKind.Ring, P = e.Pos + new Vector2(0, -16), R = 150f, Color = DangerRing, Life = 0.45f, Max = 0.45f });
			Shake(4f);
		}
		AddSpriteEffect("impact", e.Pos + new Vector2(0, -20), 100f);
	}

	void UpdateEffects(float dt)
	{
		foreach (var e in Effects)
		{
			e.Life -= dt;
			if (e.Kind == FxKind.Text) e.P.Y -= 25f * dt;
		}
		Effects.RemoveAll(static e => e.Life <= 0f);
		spriteFx = 0;
		foreach (var e in Effects)
			if (e.Kind == FxKind.Sprite) spriteFx++;
	}

	void QueueEnemy(int type, bool elite = false)
	{
		// 在玩家四周、屏幕边缘附近刷怪（有 1 秒预警）;贴墙时换到对侧，避免出现在玩家脚下。
		var p = PlayerPos;
		for (int attempt = 0; attempt < 8; attempt++)
		{
			float angle = Rng.Randf() * Mathf.Tau;
			p = PlayerPos + new Vector2(Mathf.Cos(angle) * Rng.RandfRange(480, 700), Mathf.Sin(angle) * Rng.RandfRange(320, 500));
			p = p.Clamp(FIELD.Position + new Vector2(30, 40), FIELD.End - new Vector2(30, 30));
			if (p.DistanceTo(PlayerPos) >= 300f) break;
		}
		Warnings.Add(new Warning { Type = type, Elite = elite, P = p, Time = 1f });
	}

	public Enemy SpawnEnemy(int type, Vector2 p, bool elite = false)
	{
		var data = Balance.Enemies[type];
		float height = data.Height * (elite ? 1.35f : 1f);
		var actor = new Actor();
		Actors.AddChild(actor);
		actor.Setup(data.Model, height);
		actor.Locomotion(true);
		actor.SetGait(data.Kind, data.Speed);
		actor.Place(p);
		// 普通怪生命随波次加速增长;首领生命按 (波次/5)^2.2 增长,第 5 波 4500,第 20 波约 9.4 万
		float strength = 1f + (Wave - 1) * Balance.HpGrowth + (Wave - 1) * (Wave - 1) * Balance.HpQuad;
		// 强化倍率从第 1 波的 1 倍逐渐过渡到第 10 波的满额,前期不至于太难上手
		float ramp = Mathf.Clamp((Wave - 1) / 9f, 0f, 1f);
		float hpMult = Mathf.Lerp(1f, Balance.EnemyHpMult, ramp);
		float dmgMult = Mathf.Lerp(1f, Balance.EnemyDamageMult, ramp);
		float spdMult = Mathf.Lerp(1f, Balance.EnemySpeedMult, ramp);
		float health = type == 7 ? Balance.BossBaseHp * Mathf.Pow(Mathf.Max(5, Wave) / 5f, 2.2f) : data.Hp * strength * hpMult * (elite ? 6f : 1f);
		var enemy = new Enemy
		{
			Id = nextEnemyId, Type = type, Def = data, Node = actor, Pos = p, Hp = health, MaxHp = health,
			Speed = data.Speed * (1f + Wave * 0.014f) * spdMult, Damage = data.Damage * (1f + Wave * 0.05f) * dmgMult * (elite ? 1.4f : 1f), Radius = height * 0.155f,
			Cooldown = Rng.RandfRange(1.4f, 3.0f), Kind = data.Kind, Elite = elite, AbilityTimer = Rng.RandfRange(2f, 5f),
		};
		nextEnemyId++;
		Enemies.Add(enemy);
		return enemy;
	}

	// 电击:普通怪麻痹一小会儿,随后一段时间减速;首领不会被麻痹,只受较轻的减速
	public void ShockEnemy(Enemy e, int tier)
	{
		if (e.Hp <= 0f) return;
		Sound("stun");
		if (e.Type == 7)
		{
			e.Slow = Mathf.Max(e.Slow, 1.5f + 0.3f * tier);
			e.SlowMul = Mathf.Max(e.SlowMul, 0.75f);
			return;
		}
		e.Stun = Mathf.Max(e.Stun, 0.45f + 0.1f * tier);
		e.Slow = Mathf.Max(e.Slow, 2.0f + 0.4f * tier);
		e.SlowMul = 0.5f;
	}

	public void UpdateEnemies(float frameDt)
	{
		for (int idx = 0; idx < Enemies.Count; idx++)
		{
			var e = Enemies[idx];
			if (e.Hp <= 0f) continue;
			float dt = e.Slow > 0f ? frameDt * e.SlowMul : frameDt;
			if (e.Slow > 0f) e.Slow -= frameDt;
			var node = e.Node;
			var delta = PlayerPos - e.Pos;
			float dist = delta.Length();
			var direction = dist > 0f ? delta / dist : Vector2.Zero;
			var kind = e.Kind;
			e.Cooldown -= dt;
			e.Hit = Mathf.Max(0f, e.Hit - dt);
			// 状态着色:受击闪白 > 麻痹青白闪烁 > 减速淡蓝
			int tintState = e.Hit > 0f ? 1 : (e.Stun > 0f ? 2 + ((int)(TotalTime * 24f) & 1) : (e.Slow > 0f ? 4 : 0));
			if (tintState != e.TintState)
			{
				e.TintState = tintState;
				node.Tint = tintState switch { 1 => new Color(2.8f, 2.4f, 1.7f), 2 => new Color(0.7f, 1.5f, 2.6f), 3 => new Color(1.6f, 2.0f, 2.6f), 4 => new Color(0.75f, 0.95f, 1.35f), _ => Colors.White };
			}
			if (e.Stun > 0f)
			{
				// 麻痹:停在原地,攻击与冲锋被打断,期间不造成接触伤害
				e.Stun -= frameDt;
				e.Windup = 0f;
				e.Dash = 0f;
				node.Alert = false;
				node.Lunge = false;
				continue;
			}
			TraitTick(e, dt);
			node.Alert = e.Windup > 0f;
			node.Lunge = e.Dash > 0f;
			var movementStart = e.Pos;
			if (e.Windup > 0f)
			{
				node.Face(e.Dir.X);
				e.Windup -= dt;
				if (e.Windup <= 0f)
				{
					if (kind == EKind.Charge) e.Dash = ChargeTime;
					else
					{
						if (kind == EKind.Boss) FireBossPattern(e);
						else if (e.Elite) EliteAttack(e);
						else
						{
							var sprite = e.Type == 6 ? PSprite.Note : (fxRng.Randf() < 0.35f ? PSprite.Ink : PSprite.Paper);
							// 第 10 波起便利贴怪改为三连发
							int shots = e.Type == 6 && Wave >= 10 ? 3 : 1;
							for (int k = 0; k < shots; k++)
								AddEnemyBullet(e.Pos + new Vector2(0, -18), e.Dir.Rotated((k - (shots - 1) * 0.5f) * 0.28f) * 170f, e.Damage, sprite, 66f);
							AddSpriteEffect("impact", e.Pos + new Vector2(0, -20), 54f);
							Effects.Add(new Fx { Kind = FxKind.Ring, P = e.Pos + new Vector2(0, -16), R = 46f, Color = DangerRing, Life = 0.25f, Max = 0.25f });
						}
						Sound("swing_paper");
					}
				}
				continue;
			}
			if (e.Dash > 0f)
			{
				e.Dash -= dt;
				e.Pos += e.Dir * ChargeSpeed * dt;
				if (e.Dash <= 0f && e.Chain > 0)
				{
					// 精英冲锋怪:冲完立刻对准玩家再冲(最多三连)
					e.Chain--;
					e.Dir = direction;
					e.Windup = 0.3f;
				}
				e.Puff -= dt;
				if (e.Puff <= 0f)
				{
					e.Puff = 0.04f;
					if (Effects.Count < FX_SOFT_CAP)
						Effects.Add(new Fx { Kind = FxKind.Puff, P = e.Pos + new Vector2(fxRng.RandfRange(-8, 8), fxRng.RandfRange(-3, 3)), R = fxRng.RandfRange(10, 18), Color = Colors.White, Life = 0.35f, Max = 0.35f });
				}
			}
			else if (kind == EKind.Ranged && dist < 290f)
			{
				if (dist < 190f) e.Pos -= direction * e.Speed * 0.45f * dt;
			}
			else e.Pos += direction * e.Speed * dt;
			var travel = e.Pos - movementStart;
			node.Face(travel.LengthSquared() > 0.0001f ? travel.X : direction.X);
			if ((kind == EKind.Ranged || (kind == EKind.Charge && dist > 240f) || kind == EKind.Boss || (e.Elite && (kind == EKind.Chase || kind == EKind.Tank) && dist < 300f)) && e.Cooldown <= 0f)
			{
				e.Cooldown = kind != EKind.Boss ? (e.Elite ? 2.6f : 3.2f) : 2.7f;
				e.Chain = e.Elite && kind == EKind.Charge ? 2 : 0;
				e.Windup = 0.7f;
				e.Dir = direction;
				if (kind == EKind.Boss)
				{
					e.Pattern = e.PatternIdx++ % 4;
					if (e.Hp < e.MaxHp * 0.5f) e.Cooldown = 2.0f;     // 半血后进入狂暴,出招更快
				}
			}
			if (dist < e.Radius + 17f)
			{
				HurtPlayer(e.Damage);
				if (Phase != "running") return;
			}
			e.Pos = e.Pos.Clamp(FIELD.Position + new Vector2(10, 20), FIELD.End - new Vector2(10, 8));
		}
		long ts = ProfStart();
		SeparateEnemies();
		ProfEnd(1, ts);
	}

	List<int> RentList()
	{
		if (sepPool.Count > 0)
		{
			var l = sepPool.Pop();
			l.Clear();
			return l;
		}
		return new List<int>(8);
	}

	// 敌方弹幕飞行时间:速度 170 时约 1500 像素,几乎飞到战场边缘才消失
	public const float EnemyBulletLife = 9f;

	void AddEnemyBullet(Vector2 pos, Vector2 vel, float damage, PSprite sprite, float size)
	{
		Projectiles.Add(new Projectile { P = pos, V = vel, Life = EnemyBulletLife, Damage = damage, Enemy = true, R = 7f, Color = new Color("e7a087"), Kind = WKind.Lance, Sprite = sprite, Size = size, Spin = fxRng.Randf() * Mathf.Tau });
	}

	// 首领三种招式轮换:环形弹幕、三层螺旋、朝玩家的扇形;半血后弹数增加
	void FireBossPattern(Enemy e)
	{
		bool rage = e.Hp < e.MaxHp * 0.5f;
		var from = e.Pos + new Vector2(0, -18);
		float baseAngle = (TotalTime + 0.7f) * 0.4f;
		switch (e.Pattern)
		{
			case 0:
			{
				int n = rage ? 16 : 12;
				for (int i = 0; i < n; i++) AddEnemyBullet(from, U.Dir(Mathf.Tau * i / n + baseAngle) * 170f, e.Damage, PSprite.Orb, 88f);
				break;
			}
			case 1:
			{
				int n = rage ? 10 : 8;
				for (int layer = 0; layer < 3; layer++)
					for (int i = 0; i < n; i++)
						AddEnemyBullet(from, U.Dir(Mathf.Tau * i / n + baseAngle + layer * 0.28f) * (140f + 50f * layer), e.Damage, PSprite.Orb, 88f);
				break;
			}
			case 3:
			{
				// 召唤一圈小怪(订书机怪与鼠标怪),狂暴后更多
				int n = rage ? 8 : 5;
				for (int i = 0; i < n; i++) pendingSpawns.Add((i % 3 == 0 ? 0 : 1, e.Pos + U.Dir(Mathf.Tau * i / n) * 90f));
				break;
			}
			default:
			{
				int n = rage ? 13 : 9;
				for (int i = 0; i < n; i++)
				{
					float spread = (i - (n - 1) * 0.5f) * (1.24f / (n - 1));
					AddEnemyBullet(from, e.Dir.Rotated(spread) * 240f, e.Damage, PSprite.Orb, 88f);
				}
				break;
			}
		}
		AddSpriteEffect("impact", e.Pos + new Vector2(0, -20), 130f);
		Effects.Add(new Fx { Kind = FxKind.Ring, P = e.Pos + new Vector2(0, -16), R = 170f, Color = DangerRing, Life = 0.5f, Max = 0.5f });
		Shake(5f);
	}

	public void SeparateEnemies()
	{
		// 按 36 像素网格找近邻（要大于首领的最大互推距离）,只处理相邻格子里的怪物对（每对只算一次）,推力累加后统一应用。
		// 敌人很多时隔帧计算，推力放大补偿，避免聚成一团时成本平方级增长。
		int n = Enemies.Count;
		if (n < 2) return;
		sepTick++;
		bool crowded = n > 120;
		if (crowded && sepTick % 2 == 1) return;
		sepStrength = crowded ? 1.6f : 1f;
		if (sepPos.Length < n)
		{
			int size = n + 64;
			sepPos = new Vector2[size];
			sepRad = new float[size];
			sepPush = new Vector2[size];
		}
		System.Array.Clear(sepPush, 0, n);
		foreach (var cell in sepGrid.Values) sepPool.Push(cell);
		sepGrid.Clear();
		for (int i = 0; i < n; i++)
		{
			var e = Enemies[i];
			if (e.Hp <= 0f) { sepPos[i] = Vector2.Zero; sepRad[i] = 0f; continue; }
			var p = e.Pos;
			sepPos[i] = p;
			sepRad[i] = e.Radius;
			int key = (int)(p.X / 36f) * 1000 + (int)(p.Y / 36f);
			if (!sepGrid.TryGetValue(key, out var list))
			{
				list = RentList();
				sepGrid[key] = list;
			}
			list.Add(i);
		}
		foreach (var kv in sepGrid)
		{
			var cell = kv.Value;
			for (int x = 0; x < cell.Count; x++)
				for (int y = x + 1; y < cell.Count; y++) PushPair(cell[x], cell[y]);
			foreach (int off in SepForward)
			{
				if (!sepGrid.TryGetValue(kv.Key + off, out var other)) continue;
				for (int a = 0; a < cell.Count; a++)
					for (int b = 0; b < other.Count; b++) PushPair(cell[a], other[b]);
			}
		}
		for (int i = 0; i < n; i++)
			if (sepPush[i] != Vector2.Zero) Enemies[i].Pos += sepPush[i];
	}

	void PushPair(int i, int j)
	{
		var dv = sepPos[i] - sepPos[j];
		float minDist = (sepRad[i] + sepRad[j]) * 0.65f;
		float d2 = dv.LengthSquared();
		if (d2 < minDist * minDist && d2 > 0.01f)
		{
			float dist = Mathf.Sqrt(d2);
			var amount = dv / dist * Mathf.Min(1f, (minDist - dist) * 0.14f) * sepStrength;
			sepPush[i] += amount;
			sepPush[j] -= amount;
		}
	}

	public Enemy NearestEnemy(Vector2 origin, float reach, List<int> excluded = null)
	{
		if (neOk && (excluded == null || excluded.Count == 0)) return NearestCached(origin, reach);
		Enemy chosen = null;
		float best = reach * reach;
		foreach (var e in Enemies)
		{
			if (e.Hp <= 0f || (excluded != null && excluded.Contains(e.Id))) continue;
			float distance = origin.DistanceSquaredTo(e.Pos);
			if (distance < best)
			{
				best = distance;
				chosen = e;
			}
		}
		return chosen;
	}

	// 索敌缓存:UpdateWeapons 开始时把敌人位置拍成数组,6 件武器共用。
	Enemy NearestCached(Vector2 origin, float reach)
	{
		while (true)
		{
			float best = reach * reach;
			int chosen = -1;
			for (int i = 0; i < neCount; i++)
			{
				if (neDead[i] == 1) continue;
				float d = origin.DistanceSquaredTo(nePos[i]);
				if (d < best)
				{
					best = d;
					chosen = i;
				}
			}
			if (chosen < 0) return null;
			var e = Enemies[chosen];
			if (e.Hp <= 0f)
			{
				neDead[chosen] = 1;
				continue;
			}
			return e;
		}
	}

	// 武器绕着人物身体外侧的椭圆轨道排列:人物约 100 像素高、70 像素宽,轨道要在身体之外,不能压住脸和躯干
	public const float WeaponOrbitX = 112f, WeaponOrbitY = 72f, WeaponOrbitLift = -52f;

	// 铅笔发射:整支铅笔沿锁定方向飞出,飞行路径上的怪物都受伤害,然后飞回身边。
	// 0~0.45 飞出,0.45~0.55 停在最远处,0.55~1 飞回;只有飞出阶段造成伤害。
	public const float LanceFlash = 0.70f;
	public const float PencilScale = 1.35f;       // 铅笔比其他武器稍大
	const float LanceGrow = 1.25f;                // 飞行时再放大一点

	public float LanceLength(int i) => 50f * TierScale[Weapons[i].Tier - 1] * PencilScale * LanceGrow;

	public Vector2 LanceCenter(int i, float age, out float grow, out float length)
	{
		var w = Weapons[i];
		length = LanceLength(i);
		grow = LanceGrow;
		float far = WeaponReach(w) - length * 0.5f + 10f;
		float dist, blend;
		if (age < 0.45f)
		{
			float u = age / 0.45f;
			dist = far * (1f - Mathf.Pow(1f - u, 3f));
			blend = Mathf.Min(1f, u * 5f);
		}
		else if (age < 0.55f)
		{
			dist = far;
			blend = 1f;
		}
		else
		{
			float v = (age - 0.55f) / 0.45f;
			dist = far * (1f - v * v);
			blend = Mathf.Min(1f, (1f - v) * 5f);
		}
		var origin = PlayerPos + new Vector2(0, -22);
		return WeaponPosition(i).Lerp(origin + U.Dir(w.LanceAngle) * dist, blend);
	}

	// 飞行中的铅笔逐帧检测:整支笔身(尾到尖)扫过的怪物都受一次伤害
	void UpdateLance(int i, Weapon w)
	{
		float age = 1f - w.Flash / LanceFlash;
		if (age > 0.55f) return;
		var data = Balance.Weapons[w.Type];
		var center = LanceCenter(i, age, out _, out float length);
		var dir = U.Dir(w.LanceAngle);
		var tail = center - dir * (length * 0.5f);
		var tip = center + dir * (length * 0.5f);
		float half = 16f + 3f * (w.Tier - 1);
		foreach (var e in Enemies)
		{
			if (e.Hp <= 0f || w.LanceHit.Contains(e.Id)) continue;
			var body = e.Pos + new Vector2(0, -18);
			if (ClosestOnSegment(body, tail, tip).DistanceTo(body) > half + e.Radius * 0.6f) continue;
			w.LanceHit.Add(e.Id);
			HitEnemy(e, w.LanceDamage, w.LanceCrit);
			e.Pos += dir * 10f;
		}
	}

	public Vector2 WeaponPosition(int index)
	{
		float orbit = -Mathf.Pi * 0.90f + index * Mathf.Tau / Mathf.Max(Weapons.Count, 1);
		return PlayerPos + new Vector2(Mathf.Cos(orbit) * WeaponOrbitX, Mathf.Sin(orbit) * WeaponOrbitY + WeaponOrbitLift);
	}

	public float WeaponRotation(int index)
	{
		var weapon = Weapons[index];
		return weapon.Angle - Mathf.DegToRad(Balance.Weapons[weapon.Type].NativeAngleDeg);
	}

	public Vector2 WeaponMuzzle(int index)
	{
		var weapon = Weapons[index];
		float distance = Balance.Weapons[weapon.Type].Kind == WKind.Bow ? 28f : 24f;
		distance = Mathf.Min(distance, weapon.AimDistance * 0.5f);
		return WeaponPosition(index) + U.Dir(weapon.Angle) * distance;
	}

	// 升阶带来的范围加成：每升一阶 +10%（近战类）,其余武器按各自机制成长。
	public float WeaponReach(Weapon w)
	{
		var data = Balance.Weapons[w.Type];
		if (data.Kind == WKind.Lance || data.Kind == WKind.Spear || data.Kind == WKind.Blade || data.Kind == WKind.Baton) return data.Range * (1f + 0.10f * (w.Tier - 1));
		return data.Range;
	}

	public void UpdateWeapons(float dt)
	{
		int count = Enemies.Count;
		if (nePos.Length < count)
		{
			nePos = new Vector2[count + 64];
			neDead = new byte[count + 64];
		}
		neCount = count;
		for (int k = 0; k < count; k++)
		{
			var cached = Enemies[k];
			nePos[k] = cached.Pos;
			neDead[k] = cached.Hp <= 0f ? (byte)1 : (byte)0;
		}
		neOk = true;
		UpdateWeaponsCached(dt);
		neOk = false;
	}

	void UpdateWeaponsCached(float dt)
	{
		for (int i = 0; i < Weapons.Count; i++)
		{
			var w = Weapons[i];
			w.Cd -= dt;
			w.Flash = Mathf.Max(0f, w.Flash - dt);
			if (Balance.Weapons[w.Type].Kind == WKind.Lance && w.Flash > 0f) UpdateLance(i, w);
			var data = Balance.Weapons[w.Type];
			var target = NearestEnemy(PlayerPos, WeaponReach(w));
			if (target != null)
			{
				var aim = target.Pos + new Vector2(0, -18) - WeaponPosition(i);
				w.Angle = aim.Angle();
				w.AimDistance = aim.Length();
			}
			else
			{
				w.Angle = -Mathf.Pi / 2f + i * Mathf.Tau / Mathf.Max(Weapons.Count, 1) + Mathf.Sin(TotalTime) * 0.15f;
				w.AimDistance = float.PositiveInfinity;
			}
			if (w.Cd > 0f || target == null || (data.Kind == WKind.Lance && w.Flash > 0f)) continue;
			w.Cd = data.Cooldown / AttackSpeed / (1f + (w.Tier - 1) * 0.06f);
			w.Flash = 0.20f;
			int tier = w.Tier;
			float amount = data.Damage * DamageMult * (1f + (tier - 1) * 0.62f);
			bool critical = Rng.Randf() < Crit;
			if (critical) amount *= 2f;
			var origin = PlayerPos + new Vector2(0, -22);
			if (data.Kind == WKind.Bow || data.Kind == WKind.Magic || data.Kind == WKind.Thunder) origin = WeaponMuzzle(i);
			var direction = (target.Pos + new Vector2(0, -18) - origin).Normalized();
			var color = data.Color;
			switch (data.Kind)
			{
				case WKind.Lance:
				{
					// 铅笔:整支发射出去,沿途所有怪物受伤害,再飞回身边
					w.LanceAngle = direction.Angle();
					w.Flash = LanceFlash;
					w.LanceHit.Clear();
					w.LanceDamage = amount;
					w.LanceCrit = critical;
					Effects.Add(new Fx { Kind = FxKind.Lance, P = origin, Angle = w.LanceAngle, R = WeaponReach(w), Count = tier, N = i, Color = new Color("ffd24a"), Life = LanceFlash, Max = LanceFlash });
					Sound("swing_light");
					break;
				}
				case WKind.Spear:
				case WKind.Blade:
				{
					float radius = WeaponReach(w);
					// 铅笔的扇面随阶数变宽;美工刀的刺击通道随阶数变宽
					float cone = 0.84f - 0.03f * (tier - 1);
					foreach (var e in Enemies)
					{
						if (e.Hp <= 0f) continue;
						var relative = e.Pos - PlayerPos;
						if (relative.Length() > radius) continue;
						float dot = direction.Dot(relative.Normalized());
						if (data.Kind == WKind.Blade || dot > cone)
						{
							HitEnemy(e, amount, critical);
							e.Pos += relative.Normalized() * 14f;
						}
					}
					switch (data.Kind)
					{
						case WKind.Spear:
							// 美工刀：推出刀片，沿直线利落一划，末端迸出火花
							Effects.Add(new Fx { Kind = FxKind.Cut, P = origin, End = origin + direction * radius, W = 10f + 3f * tier, Color = new Color("f4f8ff"), Life = 0.2f, Max = 0.2f });
							break;
						case WKind.Blade:
							// 文件夹：抡一圈，把纸张甩向四周
							Effects.Add(new Fx { Kind = FxKind.Fling, P = PlayerPos + new Vector2(0, -18), R = radius, N = 8 + 2 * tier, Color = Colors.White, Life = 0.42f, Max = 0.42f });
							break;
					}
					Sound(data.Kind == WKind.Spear ? "swing_blade" : "swing_paper");
					break;
				}
				case WKind.Baton:
				{
					// 警棍:朝目标挥出宽扇面,命中者被重重击退;每升一阶扇面更宽
					float radius = WeaponReach(w);
					float cone = 0.30f - 0.05f * (tier - 1);
					float centre = direction.Angle();
					var struck = new List<Enemy>();
					foreach (var e in Enemies)
					{
						if (e.Hp <= 0f) continue;
						var relative = e.Pos - PlayerPos;
						if (relative.Length() > radius) continue;
						if (direction.Dot(relative.Normalized()) <= cone) continue;
						HitEnemy(e, amount, critical);
						if (e.Type != 7) e.Pos += relative.Normalized() * 34f;
						struck.Add(e);
						ShockEnemy(e, tier);
					}
					// 带电:棍身先放一道电弧到最近的目标,再从前几个被击中的敌人跳向附近未被击中的敌人(半伤害)
					if (struck.Count > 0)
					{
						Effects.Add(new Fx { Kind = FxKind.Bolt, P = WeaponPosition(i) + U.Dir(w.Angle) * (32f * TierScale[tier - 1] + 26f), End = struck[0].Pos + new Vector2(0, -18), N = i, Color = Colors.White, Life = 0.22f, Max = 0.22f });
						var zapped = new List<int>();
						foreach (var s in struck) zapped.Add(s.Id);
						for (int k = 0; k < Mathf.Min(struck.Count, 2 + tier); k++)
						{
							var from = struck[k].Pos + new Vector2(0, -18);
							var next = NearestEnemy(struck[k].Pos, 150f + 20f * (tier - 1), zapped);
							if (next == null) continue;
							zapped.Add(next.Id);
							Effects.Add(new Fx { Kind = FxKind.Bolt, P = from, End = next.Pos + new Vector2(0, -18), N = i * 7 + k + 1, Color = Colors.White, Life = 0.26f, Max = 0.26f });
							HitEnemy(next, amount * 0.5f, false);
							ShockEnemy(next, tier);
						}
					}
					Shake(1.5f);
					Sound("zap");
					break;
				}
				case WKind.Thunder:
				{
					// 荧光笔：在目标之间画出黄色荧光标记，每升一阶多标记一个目标、跳得更远
					var hit = new List<int>();
					var point = origin;
					var next = target;
					for (int bounce = 0; bounce < 3 + tier; bounce++)
					{
						if (next == null) break;
						var end = next.Pos + new Vector2(0, -18);
						Effects.Add(new Fx { Kind = FxKind.Marker, P = point, End = end, Color = new Color("ffe83a"), Life = 0.34f, Max = 0.34f });
						HitEnemy(next, amount * Mathf.Pow(0.83f, bounce), critical);
						hit.Add(next.Id);
						point = end;
						next = NearestEnemy(point, 160f + 20f * (tier - 1), hit);
					}
					Sound("zap");
					break;
				}
				case WKind.Bow:
				{
					// 订书机：每升一阶多射一颗订书钉（最多 4 颗）,三阶起可穿透两个目标
					int shots = Mathf.Min(tier, 4);
					for (int k = 0; k < shots; k++)
					{
						float spread = (k - (shots - 1) * 0.5f) * 0.14f;
						Projectiles.Add(new Projectile { P = origin, V = direction.Rotated(spread) * 570f, Life = 1.7f, Damage = amount, Enemy = false, R = 4f, Color = color, Pierce = tier >= 3 ? 2 : 1, Kind = WKind.Bow, Critical = critical });
					}
					Sound("stapler");
					break;
				}
				default:
					// 计算器：扔出去，砸中后迸出一圈数学符号;爆炸范围随阶数扩大
					Projectiles.Add(new Projectile { P = origin, V = direction * 360f, Life = 1.7f, Damage = amount, Enemy = false, R = 9f, Color = color, Pierce = 0, Kind = data.Kind, Critical = critical, Icon = Textures.GetValueOrDefault(data.Image), Splash = 85f * (1f + 0.18f * (tier - 1)) });
					Sound("calc");
					break;
			}
		}
	}

	public void HitEnemy(Enemy e, float damage, bool critical = false)
	{
		if (e.Hp <= 0f) return;
		e.Hp -= damage;
		e.Hit = 0.09f;
		// 受击轻微后退(首领不动),暴击后退更多并带一点屏幕震动
		if (e.Type != 7)
		{
			var away = e.Pos - PlayerPos;
			if (away.LengthSquared() > 1f) e.Pos += away.Normalized() * (critical ? 10f : 5f);
		}
		if (critical) Shake(1.5f);
		Sound(critical ? "crit" : "hit");
		AddSpriteEffect("impact", e.Pos + new Vector2(0, -22), critical ? 62f : 44f);
		RunDamage += (int)damage;
		if (Effects.Count < FX_TEXT_CAP)
			Effects.Add(new Fx { Kind = FxKind.Text, P = e.Pos + new Vector2(Rng.RandfRange(-8, 8), -48), Text = ((int)damage).ToString() + (critical ? "!" : ""), Color = critical ? GOLD : new Color("e5eddc"), Life = 0.55f, Max = 0.55f });
		if (e.Hp <= 0f)
		{
			Kills++;
			Sound("die");
			var data = Balance.Enemies[e.Type];
			var coin = new Drop { P = e.Pos, Value = Mathf.Max(1, U.RoundI(data.Reward * (e.Elite ? 4 : 1) / (1f + 0.12f * (Wave - 1)))), Xp = (e.Type >= 4 ? 2 : 1) + (e.Elite ? 3 : 0), Heart = false };
			if (e.Type == 0 && Wave >= 6)
			{
				// 订书机怪被击杀后裂成两只鼠标怪
				pendingSpawns.Add((1, e.Pos + new Vector2(-20, 0)));
				pendingSpawns.Add((1, e.Pos + new Vector2(20, 0)));
			}
			if (Drops.Count >= DROP_CAP) CollectDrop(coin);
			else Drops.Add(coin);
			if (Rng.Randf() < (e.Elite ? 0.25f : 0.018f))
			{
				var heart = new Drop { P = e.Pos + new Vector2(14, 0), Value = 0, Xp = 0, Heart = true };
				if (Drops.Count >= DROP_CAP) CollectDrop(heart);
				else Drops.Add(heart);
			}
			Effects.Add(new Fx { Kind = FxKind.Ring, P = e.Pos + new Vector2(0, -16), R = 28f, Color = JADE, Life = 0.25f, Max = 0.25f });
			for (int k = 0; k < (e.Type == 7 ? 7 : 5); k++)
			{
				if (Effects.Count >= FX_SOFT_CAP) break;
				float ang = fxRng.Randf() * Mathf.Tau;
				Effects.Add(new Fx { Kind = FxKind.Scrap, P = e.Pos + new Vector2(0, -18), V = U.Dir(ang) * fxRng.RandfRange(60, 150) + new Vector2(0, -90), Rot = fxRng.Randf() * Mathf.Tau, Spin = fxRng.RandfRange(-9, 9), Color = Colors.White, Life = 0.55f, Max = 0.55f });
			}
			if (e.Type == 7)
			{
				BossDefeated = true;
				Announce("首领已倒 · 坚持到本波结束");
			}
			if (e.Type == 7) Shake(16f);
			e.Node.Die(e.Type == 7);
			dyingActors.Add(e.Node);
		}
	}

	// 线段上离点最近的位置，避免每颗子弹每帧都调用引擎接口
	static Vector2 ClosestOnSegment(Vector2 point, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		float len2 = ab.LengthSquared();
		if (len2 <= 0f) return a;
		float t = Mathf.Clamp((point - a).Dot(ab) / len2, 0f, 1f);
		return a + ab * t;
	}

	static readonly string[] Symbols = { "+", "−", "×", "÷", "=", "%" };

	public void UpdateProjectiles(float dt)
	{
		var bodyPoint = PlayerPos + new Vector2(0, -20);
		for (int pi = 0; pi < Projectiles.Count; pi++)
		{
			var b = Projectiles[pi];
			b.Life -= dt;
			var old = b.P;
			b.P += b.V * dt;
			if (b.Enemy)
			{
				var close = ClosestOnSegment(bodyPoint, old, b.P);
				if (close.DistanceTo(bodyPoint) < 19f)
				{
					HurtPlayer(b.Damage);
					AddSpriteEffect("impact", PlayerPos + new Vector2(0, -22), 70f);
					Effects.Add(new Fx { Kind = FxKind.Ring, P = bodyPoint, R = 36f, Color = DangerRing, Life = 0.22f, Max = 0.22f });
					b.Life = 0f;
				}
			}
			else
			{
				foreach (var e in Enemies)
				{
					if (e.Hp <= 0f || b.Hit.Contains(e.Id)) continue;
					var center = e.Pos + new Vector2(0, -18);
					var close = ClosestOnSegment(center, old, b.P);
					if (close.DistanceTo(center) > e.Radius + b.R) continue;
					b.Hit.Add(e.Id);
					if (b.Kind == WKind.Magic)
					{
						float splash = b.Splash;
						foreach (var other in Enemies)
						{
							if (other.Hp > 0f && other.Pos.DistanceTo(e.Pos) < splash) HitEnemy(other, b.Damage, b.Critical);
						}
						Effects.Add(new Fx { Kind = FxKind.Ring, P = b.P, R = splash, Color = b.Color, Life = 0.35f, Max = 0.35f });
						AddSpriteEffect("impact", b.P, 145f * (splash / 85f));
						for (int k = 0; k < 8; k++)
						{
							if (Effects.Count >= FX_SOFT_CAP) break;
							float ang = Mathf.Tau * k / 8f + fxRng.Randf() * 0.4f;
							Effects.Add(new Fx { Kind = FxKind.Glyph, P = b.P, V = U.Dir(ang) * (splash * 1.3f), Text = Symbols[k % Symbols.Length], Color = new Color("ffb347"), Life = 0.55f, Max = 0.55f });
						}
					}
					else HitEnemy(e, b.Damage, b.Critical);
					b.Pierce -= 1;
					if (b.Pierce < 0)
					{
						b.Life = 0f;
						break;
					}
				}
			}
		}
		Projectiles.RemoveAll(static b => b.Life <= 0f || !ProjectileBounds.HasPoint(b.P));
		Enemies.RemoveAll(static e => e.Hp <= 0f);
	}

	public void HurtPlayer(float amount)
	{
		if (Invulnerable > 0f || Phase != "running") return;
		float dealt = Mathf.Max(1f, amount * 100f / (100f + Armor * 6f));
		Hp -= dealt;
		Shake(9f);
		Invulnerable = 0.55f;
		Player.HitFlash();
		Effects.Add(new Fx { Kind = FxKind.Text, P = PlayerPos + new Vector2(0, -85), Text = "−" + ((int)dealt).ToString(), Color = new Color("ff9b8e"), Life = 0.8f, Max = 0.8f });
		Sound("hurt");
		if (Hp <= 0f)
		{
			Hp = 0f;
			EndRun(false);
		}
	}

	void UpdateDrops(float dt)
	{
		foreach (var d in Drops)
		{
			float distance = d.P.DistanceTo(PlayerPos);
			if (distance < PickupRange) d.P = d.P.MoveToward(PlayerPos, (250f + (PickupRange - distance) * 5f) * dt);
			if (distance < 21f)
			{
				CollectDrop(d);
				d.Collected = true;
			}
		}
		Drops.RemoveAll(static d => d.Collected);
	}

	public void CollectDrop(Drop d)
	{
		if (d.Heart)
		{
			Hp = Mathf.Min(MaxHp, Hp + 8f);
			Sound("heal");
			Effects.Add(new Fx { Kind = FxKind.Text, P = PlayerPos + new Vector2(0, -85), Text = "+8", Color = new Color("7dff7d"), Life = 0.8f, Max = 0.8f });
		}
		else
		{
			GainCash(d.Value);
			Sound("coin");
			Xp += GainXp(d.Xp);
			while (Xp >= NextXp)
			{
				Xp -= NextXp;
				Level++;
				NextXp = 12 + Level * Level * 3;
				PendingUpgrades++;
				if (LevelDamage > 0f)
				{
					DamageMult += LevelDamage;
					if (Player != null && IsInstanceValid(Player)) Effects.Add(new Fx { Kind = FxKind.Ring, P = PlayerPos + new Vector2(0, -30), R = 120f, Color = GOLD, Life = 0.5f, Max = 0.5f });
				}
				Announce("升级！本波结束后选择技能（可累计）");
				Sound("upgrade");
			}
			ClampStats();
		}
	}

	public void FinishWave()
	{
		foreach (var d in Drops) CollectDrop(d);
		Drops.Clear();
		ClearBonus = GainCash(10 + Wave * 2);
		foreach (var e in Enemies)
			if (IsInstanceValid(e.Node)) e.Node.QueueFree();
		Enemies.Clear();
		Warnings.Clear();
		pendingSpawns.Clear();
		Projectiles.Clear();
		Effects.Clear();
		spriteFx = 0;
		if (Wave >= Balance.WaveCount)
		{
			BeginVictory();
			return;
		}
		BeginWaveClear();
	}

	// ---- 每一波结束:短暂的"本波完成"提示,再进入升级或商店 ----
	public const float ClearLen = 1.8f;
	public int ClearBonus;
	float clearTimer;

	void BeginWaveClear()
	{
		SetPhase("clear");
		clearTimer = 0f;
		Player.Locomotion(false);
		Effects.Add(new Fx { Kind = FxKind.Ring, P = PlayerPos + new Vector2(0, -40), R = 200f, Color = JADE, Life = 0.7f, Max = 0.7f });
		Ui.ShowWaveClear();
		Sound("wave_clear");
	}

	void ClearTick(float dt)
	{
		clearTimer += dt;
		UpdateEffects(dt);
		TickActors(dt);
		if (clearTimer >= ClearLen) EndWaveClear();
	}

	// 测试和跳过用:立即结束波次提示
	public void SkipClear()
	{
		if (Phase == "clear") EndWaveClear();
	}

	void EndWaveClear()
	{
		GenerateOffers();
		if (PendingUpgrades > 0) OpenUpgrade();
		else
		{
			SetPhase("shop");
			Ui.ShowShop();
		}
	}

	public int WaveKills => Kills - waveStartKills;

	// ---- 通关演出:横幅、彩纸、音效,几秒后进入结算 ----
	float victoryTimer;
	public const float VictoryLen = 3.4f;
	static readonly Color[] ConfettiColors = { new Color("ffd45e"), new Color("ff8a7a"), new Color("b996ff"), new Color("7dff7d"), new Color("6fc3ff") };

	public void BeginVictory()
	{
		SetPhase("victory");
		victoryTimer = 0f;
		Effects.Clear();
		spriteFx = 0;
		Player.Locomotion(false);
		Ui.ShowVictory();
		Sound("victory");
		Shake(6f);
	}

	public void AdvanceVictory(float dt) => VictoryTick(dt);

	void VictoryTick(float dt)
	{
		victoryTimer += dt;
		UpdateEffects(dt);
		if (victoryTimer < 2.6f)
		{
			for (int i = 0; i < 5 && Effects.Count < FX_SOFT_CAP; i++)
				Effects.Add(new Fx { Kind = FxKind.Confetti, P = PlayerPos + new Vector2(fxRng.RandfRange(-460, 460), fxRng.RandfRange(-420, -300)), V = new Vector2(fxRng.RandfRange(-40, 40), fxRng.RandfRange(110, 220)), Rot = fxRng.Randf() * Mathf.Tau, Spin = fxRng.RandfRange(-8, 8), Color = ConfettiColors[fxRng.RandiRange(0, ConfettiColors.Length - 1)], Life = 2.4f, Max = 2.4f });
			if ((int)(victoryTimer * 10f) != (int)((victoryTimer - dt) * 10f) && (int)(victoryTimer * 10f) % 6 == 0)
				Effects.Add(new Fx { Kind = FxKind.Ring, P = PlayerPos + new Vector2(0, -40), R = 160f, Color = JADE, Life = 0.6f, Max = 0.6f });
		}
		TickActors(dt);
		if (victoryTimer >= VictoryLen) EndRun(true);
	}

	// ---- 屏幕震动 ----
	float shake;
	public void Shake(float amount) => shake = Mathf.Max(shake, amount);

	static readonly float[] TierMult = { 1.0f, 1.6f, 2.3f, 3.2f };

	public Upgrade MakeUpgrade(UpgradeTemplate template, int tier)
	{
		// 稀有度越高数值越大:1 / 1.6 / 2.3 / 3.2 倍。
		float mult = TierMult[tier - 1];
		float value = template.Base * mult;
		int bonusCash = 0;
		string desc = "";
		switch (template.Stat)
		{
			case "hp":
				value = Mathf.Round(value);
				desc = $"最大生命 +{(int)value}，并恢复 {(int)value} 点生命";
				break;
			case "armor":
				value = Mathf.Round(value);
				desc = $"护甲 +{(int)value}，降低所受伤害";
				break;
			case "regen":
				desc = $"每秒恢复 {U.Fmt(value)} 点生命";
				break;
			case "pickup":
				value = Mathf.Round(value);
				bonusCash = U.RoundI(15f * mult);
				desc = $"拾取范围 +{(int)value}，立即获得 {bonusCash} 奖金";
				break;
			case "crit": desc = $"暴击概率 +{U.RoundI(value * 100)}%,暴击伤害为两倍"; break;
			case "damage": desc = $"所有文具伤害 +{U.RoundI(value * 100)}%"; break;
			case "attack": desc = $"攻击速度 +{U.RoundI(value * 100)}%"; break;
			case "speed": desc = $"移动速度 +{U.RoundI(value * 100)}%"; break;
		}
		return new Upgrade { Name = template.Name, Desc = desc, Stat = template.Stat, Value = value, Glyph = template.Glyph, Tier = tier, Cash = bonusCash };
	}

	public void OpenUpgrade()
	{
		SetPhase("upgrade");
		var pool = new List<UpgradeTemplate>
		{
			new() { Name = "健身卡", Stat = "hp", Base = 18f, Glyph = "体" },
			new() { Name = "绩效", Stat = "damage", Base = 0.12f, Glyph = "攻" },
			new() { Name = "咖啡", Stat = "attack", Base = 0.12f, Glyph = "速" },
			new() { Name = "运动鞋", Stat = "speed", Base = 0.08f, Glyph = "行" },
			new() { Name = "工服", Stat = "armor", Base = 3f, Glyph = "甲" },
			new() { Name = "专注", Stat = "crit", Base = 0.07f, Glyph = "暴" },
			new() { Name = "午睡", Stat = "regen", Base = 0.6f, Glyph = "生" },
			new() { Name = "年终奖", Stat = "pickup", Base = 30f, Glyph = "奖" },
		};
		Upgrades.Clear();
		while (Upgrades.Count < 4)
		{
			int index = Rng.RandiRange(0, pool.Count - 1);
			float roll = Rng.Randf();
			int tier = 1;
			if (Wave >= 15 && roll < 0.04f) tier = 4;
			else if (Wave >= 8 && roll < 0.14f) tier = 3;
			else if (Wave >= 3 && roll < 0.40f) tier = 2;
			Upgrades.Add(MakeUpgrade(pool[index], tier));
			pool.RemoveAt(index);
		}
		Ui.ShowUpgrades();
	}

	public void ChooseUpgrade(int index)
	{
		if (Phase != "upgrade" || index < 0 || index >= Upgrades.Count) return;
		var u = Upgrades[index];
		switch (u.Stat)
		{
			case "hp": MaxHp += u.Value; Hp += u.Value; break;
			case "damage": DamageMult += u.Value; break;
			case "attack": AttackSpeed += u.Value; break;
			case "speed": Speed *= 1 + u.Value; break;
			case "armor": Armor += u.Value; break;
			case "crit": Crit = Mathf.Min(0.85f, Crit + u.Value); break;
			case "regen": Regeneration += u.Value; break;
			case "pickup": PickupRange += u.Value; Cash += u.Cash; break;
		}
		ClampStats();
		PendingUpgrades -= 1;
		Sound("purchase");
		if (PendingUpgrades > 0) OpenUpgrade();
		else
		{
			SetPhase("shop");
			Ui.ShowShop();
		}
	}

	public void GenerateOffers()
	{
		var old = new List<Offer>();
		foreach (var o in Offers) old.Add(o.Clone());
		Offers.Clear();
		for (int i = 0; i < 4; i++)
		{
			if (LockedOffers.Contains(i) && i < old.Count && !old[i].Sold) Offers.Add(old[i]);
			else if (Rng.Randf() < 0.40f)
			{
				// 高价道具从第 4 波起出现,波次越高越常见
				float premiumChance = Wave < 4 ? 0f : Mathf.Min(0.5f, 0.05f + 0.05f * Wave);
				bool premium = Rng.Randf() < premiumChance;
				var pool = new List<int>();
				for (int k = 0; k < Balance.Items.Count; k++)
					if ((Balance.Items[k].Price >= 80) == premium && Items.GetValueOrDefault(k, 0) < MaxItemCopies) pool.Add(k);
				if (pool.Count == 0)
					for (int k = 0; k < Balance.Items.Count; k++) pool.Add(k);
				int index = pool[Rng.RandiRange(0, pool.Count - 1)];
				Offers.Add(new Offer { IsItem = true, Item = index, Tier = 1, Price = (int)(Balance.Items[index].Price * PriceScale() * (1f + 0.4f * Items.GetValueOrDefault(index, 0)) * PriceMult) });
			}
			else
			{
				int type = Rng.RandiRange(0, Balance.Weapons.Count - 1);
				int tier = RollShopTier();
				Offers.Add(new Offer { IsItem = false, Type = type, Tier = tier, Price = (int)(Balance.Weapons[type].Price * PriceScale() * TierPriceMult[tier - 1] * PriceMult) });
			}
		}
	}

	// 阶数越高价格越贵;出售按同一档价格的一半
	public static readonly float[] TierPriceMult = { 1f, 2.2f, 4.5f, 9f };

	int RollShopTier()
	{
		float r = Rng.Randf();
		if (Wave >= 12) return r < 0.12f ? 4 : r < 0.32f ? 3 : r < 0.62f ? 2 : 1;
		if (Wave >= 7) return r < 0.15f ? 3 : r < 0.45f ? 2 : 1;
		if (Wave >= 3) return r < 0.22f ? 2 : 1;
		return 1;
	}

	public const int MaxItemCopies = 6;

	// 商店价格随波次上涨:第 20 波约为第 1 波的 4.4 倍
	public float PriceScale() => 1f + (Wave - 1) * 0.18f;

	// 花钱把已有武器直接升一阶(没有同名武器可合成时的金币出口)
	public int UpgradeCost(int index)
	{
		if (index < 0 || index >= Weapons.Count || Weapons[index].Tier >= 4) return -1;
		var w = Weapons[index];
		return (int)(Balance.Weapons[w.Type].Price * TierPriceMult[w.Tier] * PriceScale() * PriceMult);
	}

	public bool UpgradeWeapon(int index)
	{
		int cost = UpgradeCost(index);
		if (Phase != "shop" || cost < 0 || Cash < cost) return false;
		Cash -= cost;
		Weapons[index].Tier += 1;
		Sound("purchase");
		Ui.ShowShop();
		return true;
	}

	public int WeaponSellPrice(Weapon w) => (int)(Balance.Weapons[w.Type].Price * TierPriceMult[w.Tier - 1] * 0.5f);

	// 把背包里两把同名同阶的武器合成一把高一阶的
	int MergePartner(int index)
	{
		if (index < 0 || index >= Weapons.Count || Weapons[index].Tier >= 4) return -1;
		for (int j = 0; j < Weapons.Count; j++)
			if (j != index && Weapons[j].Type == Weapons[index].Type && Weapons[j].Tier == Weapons[index].Tier) return j;
		return -1;
	}

	public bool CanMerge(int index) => MergePartner(index) >= 0;

	public void MergeWeapons(int index)
	{
		int other = MergePartner(index);
		if (Phase != "shop" || other < 0) return;
		Weapons[index].Tier += 1;
		Weapons.RemoveAt(other);
		Sound("purchase");
		Ui.ShowShop();
	}

	// 道具的属性变化描述，例如"攻击速度 +8%"。
	public List<ItemLine> ItemLines(ItemDef item)
	{
		var lines = new List<ItemLine>();
		void Add(string label, float? v, bool percent, bool oneDecimal = false)
		{
			if (v == null) return;
			float x = v.Value;
			string text = percent ? $"{U.RoundI(x * 100):+0;-0;+0}%" : (oneDecimal ? $"{x:+0.0;-0.0;+0.0}" : $"{(int)x:+0;-0;+0}");
			lines.Add(new ItemLine { Text = label + " " + text, Good = x > 0f });
		}
		Add("攻击速度", item.Atk, true);
		Add("伤害", item.Dmg, true);
		Add("暴击率", item.Crit, true);
		Add("移动速度", item.Spd, true);
		Add("最大生命", item.Hp, false);
		Add("护甲", item.Armor, false);
		Add("生命再生", item.Regen, false, true);
		Add("拾取范围", item.Pickup, false);
		return lines;
	}

	public void ApplyItem(int index)
	{
		var it = Balance.Items[index];
		if (it.Hp != null)
		{
			MaxHp += it.Hp.Value;
			Hp = Mathf.Min(MaxHp, Hp + Mathf.Max(it.Hp.Value, 0f));
		}
		if (it.Dmg != null) DamageMult += it.Dmg.Value;
		if (it.Atk != null) AttackSpeed = Mathf.Max(0.3f, AttackSpeed + it.Atk.Value);
		if (it.Spd != null) Speed *= 1f + it.Spd.Value;
		if (it.Armor != null) Armor += it.Armor.Value;
		if (it.Crit != null) Crit = Mathf.Clamp(Crit + it.Crit.Value, 0f, 0.85f);
		if (it.Regen != null) Regeneration += it.Regen.Value;
		if (it.Pickup != null) PickupRange += it.Pickup.Value;
		Items[index] = Items.GetValueOrDefault(index, 0) + 1;
		ClampStats();
	}

	// 属性上限:防止后期数值失控
	public const float MaxAttackSpeed = 3f, MaxMoveSpeed = 380f, MaxRegen = 8f, MaxArmor = 35f, MaxPickup = 400f, MaxDamageMult = 5f, MaxHpCap = 600f;

	public void ClampStats()
	{
		AttackSpeed = Mathf.Min(MaxAttackSpeed, AttackSpeed);
		Speed = Mathf.Min(MaxMoveSpeed, Speed);
		Regeneration = Mathf.Min(MaxRegen, Regeneration);
		Armor = Mathf.Min(MaxArmor, Armor);
		PickupRange = Mathf.Min(MaxPickup, PickupRange);
		DamageMult = Mathf.Min(MaxDamageMult, DamageMult);
		MaxHp = Mathf.Min(MaxHpCap, MaxHp);
		Hp = Mathf.Min(Hp, MaxHp);
	}

	public bool Purchase(int index)
	{
		if (Phase != "shop" || index < 0 || index >= Offers.Count) return false;
		var offer = Offers[index];
		if (offer.Sold || Cash < offer.Price) return false;
		if (offer.IsItem)
		{
			Cash -= offer.Price;
			ApplyItem(offer.Item);
			offer.Sold = true;
			LockedOffers.Remove(index);
			Sound("purchase");
			Ui.ShowShop();
			return true;
		}
		int mergeIndex = -1;
		for (int i = 0; i < Weapons.Count; i++)
		{
			if (Weapons[i].Type == offer.Type && Weapons[i].Tier == offer.Tier && offer.Tier < 4)
			{
				mergeIndex = i;
				break;
			}
		}
		if (Weapons.Count >= 6 && mergeIndex < 0)
		{
			Announce("武器已满 · 请先出售，或购买同阶武器合成");
			Ui.ShowShop();
			return false;
		}
		Cash -= offer.Price;
		if (mergeIndex >= 0) Weapons[mergeIndex].Tier += 1;
		else Weapons.Add(MakeWeapon(offer.Type, offer.Tier));
		offer.Sold = true;
		LockedOffers.Remove(index);
		Sound("purchase");
		Ui.ShowShop();
		return true;
	}

	public void SellWeapon(int index)
	{
		if (Phase != "shop" || Weapons.Count <= 1 || index < 0 || index >= Weapons.Count) return;
		var w = Weapons[index];
		Cash += WeaponSellPrice(w);
		Weapons.RemoveAt(index);
		Ui.ShowShop();
	}

	public void Reroll()
	{
		if (Phase != "shop" || Cash < RefreshCost) return;
		Cash -= RefreshCost;
		RefreshCost += 3 + Wave / 2 + RefreshCost / 10;
		Sound("reroll");
		GenerateOffers();
		Ui.ShowShop();
	}

	public void ToggleLock(int index)
	{
		if (LockedOffers.Contains(index)) LockedOffers.Remove(index);
		else LockedOffers.Add(index);
		Ui.ShowShop();
	}

	public void NextWave()
	{
		if (Phase != "shop") return;
		Wave += 1;
		StartWave();
	}

	public void EndRun(bool won)
	{
		SetPhase(won ? "win" : "gameover");
		if (!won) Sound("gameover");
		BestWave = Mathf.Max(BestWave, Wave);
		BestKills = Mathf.Max(BestKills, Kills);
		if (won) Victories++;
		SaveRecord();
		Ui.ShowResult(won);
	}

	public void ReturnTitle()
	{
		ClearWorld();
		SetPhase("title");
		Ui.ShowTitle();
	}

	void LoadRecord()
	{
		var cfg = new ConfigFile();
		if (cfg.Load("user://record.cfg") == Error.Ok)
		{
			BestWave = cfg.GetValue("record", "best_wave", 0).AsInt32();
			BestKills = cfg.GetValue("record", "best_kills", 0).AsInt32();
			Victories = cfg.GetValue("record", "victories", 0).AsInt32();
		}
	}

	void SaveRecord()
	{
		if (!SaveEnabled) return;
		var cfg = new ConfigFile();
		cfg.SetValue("record", "best_wave", BestWave);
		cfg.SetValue("record", "best_kills", BestKills);
		cfg.SetValue("record", "victories", Victories);
		cfg.Save("user://record.cfg");
	}

	public void Announce(string text)
	{
		Notice = text;
		NoticeTimer = 3f;
	}

	public void Sound(string name) => Media.Play(name);

	public void AddSpriteEffect(string name, Vector2 point, float size, float angle = 0f)
	{
		// 群体命中仍逐个计算伤害，视觉最多保留 40 个短时实例。
		if (spriteFx >= 40) return;
		float duration = Media.EffectBank[name].Duration;
		spriteFx++;
		Effects.Add(new Fx { Kind = FxKind.Sprite, Name = name, P = point, Size = size, Angle = angle, Color = Colors.White, Life = duration, Max = duration });
	}
}
