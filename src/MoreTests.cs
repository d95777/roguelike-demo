using System.Collections.Generic;
using Godot;

namespace RogueDemo;

// 原 GDScript 的 test_media / test_facing / test_ui 的 C# 版。
//   godot --headless --path . -- --test --mediatest | --facingtest | --uitest
public abstract partial class TestBase : Node
{
	public Main Game;
	protected int Passed;
	protected readonly List<string> Failures = new();

	protected void Check(bool ok, string message)
	{
		if (ok) Passed++;
		else
		{
			Failures.Add(message);
			GD.PrintErr("失败:" + message);
		}
	}

	protected async System.Threading.Tasks.Task Frames(int n = 1)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	protected abstract System.Threading.Tasks.Task Run();
	protected abstract string Title { get; }

	public override async void _Ready()
	{
		Game.SaveEnabled = false;
		await Frames(2);
		Game.SetProcess(false);
		await Run();
		GD.Print($"{Title}:通过 {Passed} 项,失败 {Failures.Count} 项");
		foreach (var f in Failures) GD.Print("  失败:" + f);
		GetTree().Quit(Failures.Count == 0 ? 0 : 1);
	}
}

public partial class MediaTest : TestBase
{
	protected override string Title => "影音接入测试";

	protected override async System.Threading.Tasks.Task Run()
	{
		var g = Game;
		g.Showcase = true;
		g.StartRun();
		var media = g.Media;
		foreach (var kv in media.Bank)
			foreach (var v in kv.Value.Variants)
				Check(v.Stream != null && v.Stream.GetLength() > 0, "音频可解码:" + kv.Key);
		foreach (var kv in media.EffectBank)
		{
			var fx = kv.Value;
			Check(fx.Texture != null && fx.Texture.GetWidth() == 2048, "特效图集可加载:" + kv.Key);
			Check(media.FrameIndex(kv.Key, fx.Duration + 0.02f) == fx.Frames - 1, "单次特效末帧边界:" + kv.Key);
			Check(media.FrameIndex(kv.Key, fx.Duration + 0.02f, true) == 0, "弹体循环帧边界:" + kv.Key);
		}
		ulong state = g.Rng.State;
		Check(media.Play("swing_blade"), "首个攻击音效播放");
		Check(!media.Play("swing_blade"), "同帧重复攻击音效限流");
		Check(media.Play("hit"), "攻击与命中独立声部");
		Check(media.Play("hurt"), "攻击时受伤提示不被抑制");
		Check(media.Play("upgrade"), "攻击时升级提示不被抑制");
		Check(media.Play("boss"), "首领提示独立声部");
		Check(g.Rng.State == state, "播放音效不消耗战斗随机数");
		g.SetPhase("paused");
		Check(media.Paused && media.Voices["attack"][0].StreamPaused, "暂停冻结正在播放的声音");
		Check(!media.Play("purchase"), "暂停期间不触发新音效");
		g.SetPhase("running");
		Check(!media.Voices["attack"][0].StreamPaused, "恢复战斗同步恢复声音");
		g.ToggleMute();
		bool stopped = true;
		foreach (var pool in media.Voices.Values)
			foreach (var voice in pool) stopped = stopped && !voice.Playing;
		Check(stopped && !media.Play("hurt"), "静音停止所有声部且不积压新声音");
		g.ToggleMute();
		Check(media.Play("hurt"), "取消静音后可重新播放");
		media.StopAll();
		for (int i = 0; i < 3; i++)
		{
			Check(media.Play("calc"), $"攻击声部可并发:{i + 1}");
			ulong until = Time.GetTicksMsec() + 100;
			while (Time.GetTicksMsec() < until) await Frames();
		}
		Check(!media.Play("calc"), "攻击声部饱和时不截断旧声音");
		Check(media.Play("hurt"), "攻击声部饱和不影响受伤提示");

		string[] cueNames = { "swing_light", "swing_blade", "stapler", "calc", "swing_paper", "zap", "zap" };
		for (int i = 0; i < 7; i++)
		{
			g.StartRun();
			g.SpawnTimer = 999f;
			g.Weapons = new List<Weapon> { g.MakeWeapon(i) };
			g.Weapons[0].Cd = 0f;
			var enemy = g.SpawnEnemy(4, g.PlayerPos + new Vector2(65, 0));
			enemy.Hp = 10000f;
			int before = media.Played.GetValueOrDefault(cueNames[i], 0);
			g.UpdateWeapons(0.01f);
			Check(media.Played.GetValueOrDefault(cueNames[i], 0) > before, $"武器 {i} 触发对应音效");
			for (int f = 0; f < 40 && !g.Effects.Exists(e => e.Kind == FxKind.Sprite); f++) g.UpdateWeapons(0.016f);
			if (i == 0 || i == 4 || i == 5) Check(g.Effects.Exists(e => e.Kind == FxKind.Sprite), $"武器 {i} 触发序列特效");
			if (i == 3)
			{
				Check(g.Projectiles.Count == 1 && g.Projectiles[0].Kind == WKind.Magic, "计算器使用循环飞行特效");
				var bolt = g.Projectiles[0];
				float flight = bolt.P.DistanceTo(enemy.Pos + new Vector2(0, -18)) / bolt.V.Length();
				g.UpdateProjectiles(flight + 0.01f);
				Check(g.Effects.Exists(e => e.Kind == FxKind.Sprite && Mathf.Abs(e.Size - 145f) < 0.01f), "计算器碰撞触发范围爆光");
			}
		}
		g.ReturnTitle();
		Check(media.MusicName == "menu", "标题界面播放菜单音乐");
		g.StartRun();
		g.SetPhase("running");
		Check(media.MusicName == "battle", "战斗播放战斗音乐");
		g.Wave = 5;
		g.SetPhase("running");
		Check(media.MusicName == "boss", "首领波切换首领音乐");
		g.Wave = 1;
		g.SetPhase("shop");
		Check(media.MusicName == "battle" && media.MusicDuck < 0f, "商店保持音乐并降低音量");
		g.SetPhase("gameover");
		Check(media.MusicName == "", "失败时停止音乐");
		g.StartRun();
		g.Weapons = new List<Weapon>();
		g.SpawnTimer = 999f;
		for (int i = 0; i < 60; i++) g.AddSpriteEffect("impact", new Vector2(100, 300), 44f);
		Check(g.Effects.Count == 40, "群体特效实例上限");
		g.Simulate(0.55f);
		Check(g.Effects.Count == 0, "一次性特效到期全部清除");
		g.AddSpriteEffect("slash", new Vector2(100, 300), 100f);
		g.ReturnTitle();
		Check(g.Effects.Count == 0, "返回首页清除战斗残留特效");
	}
}

public partial class FacingTest : TestBase
{
	protected override string Title => "朝向回归";

	void VerifyTravel(Actor node, float before, float now, string message)
	{
		float delta = now - before;
		float visual = node.FlipX * (node.NativeFacing > 0f ? 1f : -1f);
		Check(Mathf.Abs(delta) >= 0.0001f && Mathf.Sign(delta) == Mathf.Sign(visual), message);
	}

	static void PressKey(Key code, bool pressed)
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = pressed });
		Input.FlushBufferedEvents();
	}

	protected override async System.Threading.Tasks.Task Run()
	{
		var g = Game;
		for (int hero = 0; hero < 3; hero++)
		{
			g.HeroIndex = hero;
			g.StartRun();
			g.SpawnTimer = 999f;
			foreach (var code in new[] { Key.A, Key.D })
			{
				float before = g.PlayerPos.X;
				PressKey(code, true);
				await Frames();
				g.Simulate(0.1f);
				PressKey(code, false);
				VerifyTravel(g.Player, before, g.PlayerPos.X, $"修士 {hero} 键盘 {code}");
			}
		}
		for (int kind = 0; kind < 8; kind++)
		{
			foreach (int side in new[] { -1, 1 })
			{
				g.StartRun();
				if (kind == 7) g.Wave = 5;
				var e = g.SpawnEnemy(kind, g.PlayerPos + new Vector2(side * 150, 0));
				e.Cooldown = 999f;
				float before = e.Pos.X;
				g.UpdateEnemies(0.1f);
				VerifyTravel(e.Node, before, e.Pos.X, $"怪物 {kind} 方位 {side},含远程撤离");
			}
		}
		g.StartRun();
		var diagonal = g.SpawnEnemy(0, g.PlayerPos + new Vector2(12, -160));
		diagonal.Node.Face(1f);
		float b2 = diagonal.Pos.X;
		g.UpdateEnemies(0.1f);
		VerifyTravel(diagonal.Node, b2, diagonal.Pos.X, "斜向追击的小水平分量仍正确转身");
		g.StartRun();
		var charge = g.SpawnEnemy(3, g.PlayerPos - new Vector2(180, 0));
		charge.Dir = Vector2.Left;
		charge.Dash = 0.4f;
		float b3 = charge.Pos.X;
		g.UpdateEnemies(0.1f);
		VerifyTravel(charge.Node, b3, charge.Pos.X, "冲锋锁定方向不被目标位置反转");
	}
}

public partial class UiTest : TestBase
{
	protected override string Title => "鼠标界面测试";

	async System.Threading.Tasks.Task ClickAt(Vector2 p)
	{
		var vp = GetViewport();
		vp.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
		vp.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
		vp.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true);
		await Frames(2);
	}

	protected override async System.Threading.Tasks.Task Run()
	{
		var g = Game;
		g.Showcase = true;
		await ClickAt(new Vector2(411, 545));
		Check(g.HeroIndex == 1, "鼠标选择角色");
		await ClickAt(new Vector2(218, 685));
		Check(g.Phase == "pick", "开局先进入选择起始文具");
		await ClickAt(new Vector2(426, 568));
		Check(g.Phase == "running" && g.Weapons.Count == 1 && g.Weapons[0].Type == 3 && g.Weapons[0].Tier == 2, "鼠标选择会计专属文具,正式级开局");
		g.BeginRun();
		await Frames(2);
		g.ChooseStartWeapon(0);
		Check(g.Phase == "running" && g.Weapons.Count == 1 && g.Weapons[0].Type == 0 && g.Weapons[0].Tier == 1, "可改选其他文具,实习级开局");
		g.BeginRun();
		await Frames(2);
		g.ChooseStartWeapon(-1);
		Check(g.Phase == "running" && g.Weapons.Count == 1, "随机文具可开局");
		await ClickAt(new Vector2(1228, 31));
		Check(g.Phase == "paused", "鼠标暂停");
		await ClickAt(new Vector2(168, 206));
		Check(g.Muted, "鼠标静音");
		await ClickAt(new Vector2(168, 132));
		Check(g.Phase == "running", "鼠标继续游戏");
		g.Cash = 100;
		g.FinishWave();
		Check(g.Phase == "clear", "波次结束显示本波完成");
		g.SkipClear();
		await Frames(2);
		await ClickAt(new Vector2(118, 395));
		Check(g.Offers[0].Sold, "鼠标购买商品");
		await ClickAt(new Vector2(1129, 742));
		Check(g.Phase == "running" && g.Wave == 2, "鼠标进入下一波");
	}
}

// 站桩生存测试:玩家不动、只带起始文具,看每个角色撑到第几波(难度参考,不判定通过与否)。
public partial class IdleTest : TestBase
{
	protected override string Title => "站桩生存";

	protected override async System.Threading.Tasks.Task Run()
	{
		var g = Game;
		for (int hero = 0; hero < 3; hero++)
		{
			g.HeroIndex = hero;
			g.StartRun();
			g.ChooseStartWeapon(-1);
			g.BeginRun();
			g.ChooseStartWeapon(g.Balance.Heroes[hero].Weapon);
			g.Rng.Seed = 12345;
			int deathWave = 0;
			float survived = 0f;
			for (int wave = 1; wave <= 20 && deathWave == 0; wave++)
			{
				g.Wave = wave;
				g.StartWave();
				while (g.Phase == "running")
				{
					g.Simulate(0.033f);
					g.TickActors(0.033f);
					survived += 0.033f;
					if (survived > 3000f) break;
				}
				if (g.Phase == "gameover") deathWave = wave;
				else if (g.Phase == "clear" || g.Phase == "shop" || g.Phase == "upgrade") { g.SkipClear(); g.PendingUpgrades = 0; if (g.Phase == "upgrade" || g.Phase == "shop") g.SetPhase("running"); }
			}
			GD.Print($"角色 {hero} 站桩:{(deathWave == 0 ? "撑过 20 波" : "死于第 " + deathWave + " 波")},用时 {(int)survived} 秒");
		}
		Check(true, "站桩测试完成");
	}
}

// 数值估算:玩家不死、站在原地,每波结束自动买光商店、随机选技能,
// 记录每波的玩家强度、首领击杀用时和场上敌人数量,用来判断后期是否还有压力。
public partial class BalanceTest : TestBase
{
	protected override string Title => "数值估算";

	protected override async System.Threading.Tasks.Task Run()
	{
		var g = Game;
		g.HeroIndex = 0;
		g.StartRun();
		g.ChooseStartWeapon(0);
		g.BeginRun();
		g.ChooseStartWeapon(0);
		g.Rng.Seed = 777;
		for (int wave = 1; wave <= 20; wave++)
		{
			g.Wave = wave;
			g.StartWave();
			float bossTime = -1f, bossStart = -1f;
			int peak = 0, elites = 0;
			int guard = 0;
			while (g.Phase == "running" && guard++ < 20000)
			{
				if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--immortal") >= 0) g.Invulnerable = 999f;
				// 简易走位:远离附近的敌人,靠近边界时向场地中心偏移
				var flee = Vector2.Zero;
				foreach (var e in g.Enemies)
				{
					var away = g.PlayerPos - e.Pos;
					float dd = away.Length();
					if (dd < 380f && dd > 1f) flee += away / (dd * dd) * 60000f / (e.Type == 7 ? 1f : 2f);
				}
				flee += (Main.FIELD.GetCenter() - g.PlayerPos) * 0.0015f;
				if (flee.LengthSquared() > 1e-4f) g.PlayerPos = (g.PlayerPos + flee.Normalized() * g.Speed * 0.033f).Clamp(Main.FIELD.Position + new Vector2(30, 40), Main.FIELD.End - new Vector2(30, 30));
				g.Simulate(0.033f);
				g.TickActors(0.033f);
				peak = System.Math.Max(peak, g.Enemies.Count);
				if (g.BossSpawned && bossStart < 0f) bossStart = g.Elapsed;
				if (g.BossDefeated && bossTime < 0f) bossTime = g.Elapsed - bossStart;
			}
			foreach (var e in g.Enemies) if (e.Elite) elites++;
			if (g.Phase == "gameover") { GD.Print($"死于第 {wave} 波(职级 {g.Level},击败 {g.Kills})"); break; }
			GD.Print($"波次 {wave}:剩余奖金 {g.Cash} 职级 {g.Level} 伤害+{U.RoundI((g.DamageMult - 1f) * 100)}% 攻速+{U.RoundI((g.AttackSpeed - 1f) * 100)}% 生命 {(int)g.MaxHp} 护甲 {(int)g.Armor} 击败 {g.Kills} 峰值敌人 {peak}" + (bossTime >= 0f ? $" 首领用时 {bossTime:0.0}s" : ""));
			if (g.Phase == "victory") break;
			if (g.Phase == "clear") g.SkipClear();
			int safety = 0;
			while (g.Phase == "upgrade" && safety++ < 200) g.ChooseUpgrade(g.Rng.RandiRange(0, 3));
			if (g.Phase != "shop") break;
			for (int round = 0; round < 6; round++)
			{
				for (int i = 0; i < g.Offers.Count; i++) g.Purchase(i);
				if (g.Cash > g.RefreshCost + 60) g.Reroll();
			}
			// 武器满了就合成,钱还有剩就花钱升阶
			for (int i = 0; i < g.Weapons.Count; i++) if (g.CanMerge(i)) { g.MergeWeapons(i); break; }
			for (int i = 0; i < g.Weapons.Count; i++) g.UpgradeWeapon(i);
		}
		Check(true, "数值估算完成");
	}
}
