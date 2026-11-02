using System;
using System.Collections.Generic;
using Godot;

namespace RogueDemo;

public partial class Interface : CanvasLayer
{
	public Main Game;
	public Control Root;
	public SystemFont Font;
	public Label HpLabel, WaveLabel, TimeLabel, CashLabel, HintLabel, NoticeLabel, BossLabel, DashLabel, PendingLabel;
	public ProgressBar HpBar, XpBar, BossBar;
	public Label BannerTitleLabel, BannerSubLabel;
	Control perfBox;
	readonly List<Label> perfValues = new();
	bool perfVisible = true;
	float perfTimer;
	// 录像时 Engine 帧率是编码速度,由 Recorder 换成游戏本身每帧的耗时折算
	public System.Func<int> FpsSource;
	static readonly Color CREAM = new Color("e1e4ea");
	static readonly Color MUTED = new Color("a4a8ae");
	static readonly Color JADE = new Color("f2b04c");
	static readonly Color GOLD = new Color("e8c487");
	static readonly Color PANEL = new Color("1c1f25");
	static readonly string[] TierNames = { "实习级", "正式级", "资深级", "主管级" };
	static readonly Color[] TierColors = { CREAM, new Color("b996ff"), new Color("ff8a7a"), new Color("ffd45e") };
	readonly Dictionary<string, Texture2D> iconCache = new();

	public override void _Ready()
	{
		Font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "Noto Sans CJK SC", "sans-serif" } };
		var theme = new Theme { DefaultFont = Font, DefaultFontSize = 16 };
		Root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = theme };
		Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(Root);
		// 每行一个名称标签（左对齐）和一个数值标签（右对齐）,固定行距，数字变化时位置不跳动
		perfBox = new Control { Position = new Vector2(1100, 54), Size = new Vector2(166, 142), MouseFilter = Control.MouseFilterEnum.Ignore, Theme = theme };
		AddChild(perfBox);
		var rows = new[] { "FPS", "帧间隔", "敌人", "弹幕", "敌方弹幕", "特效", "掉落" };
		for (int i = 0; i < rows.Length; i++)
		{
			for (int side = 0; side < 2; side++)
			{
				var l = new Label { Position = new Vector2(0, i * 20), Size = new Vector2(166, 20), MouseFilter = Control.MouseFilterEnum.Ignore };
				l.AddThemeFontSizeOverride("font_size", 14);
				l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
				l.AddThemeConstantOverride("outline_size", 6);
				if (side == 1)
				{
					l.HorizontalAlignment = HorizontalAlignment.Right;
					perfValues.Add(l);
				}
				else
				{
					l.Text = rows[i];
					l.AddThemeColorOverride("font_color", new Color("d8dce4"));
				}
				perfBox.AddChild(l);
			}
		}
	}

	// 性能叠加层：帧率、帧耗时、敌人数量、弹幕数量。F3 开关。每 0.25 秒刷新一次，避免本身拖慢帧率。
	public void TogglePerf()
	{
		perfVisible = !perfVisible;
		perfBox.Visible = perfVisible;
	}

	public void UpdatePerf(float delta)
	{
		perfBox.Visible = perfVisible && (Game.Phase == "running" || Game.Phase == "clear" || Game.Phase == "victory");
		if (!perfBox.Visible) return;
		perfTimer -= delta;
		if (perfTimer > 0f) return;
		perfTimer = 0.25f;
		int fps = FpsSource?.Invoke() ?? (int)Engine.GetFramesPerSecond();
		int enemyBullets = 0;
		foreach (var b in Game.Projectiles)
			if (b.Enemy) enemyBullets++;
		var values = new[] { $"{fps}", $"{1000f / Mathf.Max(1f, fps):0.0} ms", $"{Game.Enemies.Count}", $"{Game.Projectiles.Count}", $"{enemyBullets}", $"{Game.Effects.Count}", $"{Game.Drops.Count}" };
		var tone = fps >= 55 ? new Color("7dff7d") : (fps >= 40 ? new Color("ffd45e") : new Color("ff6a5a"));
		for (int i = 0; i < perfValues.Count; i++)
		{
			perfValues[i].Text = values[i];
			perfValues[i].AddThemeColorOverride("font_color", tone);
		}
	}

	public void Clear()
	{
		foreach (var child in Root.GetChildren())
		{
			Root.RemoveChild(child);
			child.QueueFree();
		}
		HpLabel = null;
		NoticeLabel = null;
		BossBar = null;
		tipBox = null;
		tipLabel = null;
	}

	// ---- 悬停提示:在不会挡住按钮的区域上盖一层透明感应区,鼠标移入时显示说明 ----
	Panel tipBox;
	Label tipLabel;

	void Tip(Rect2 rect, string text)
	{
		if (string.IsNullOrEmpty(text)) return;
		var area = new Control { Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Pass };
		area.MouseEntered += () => ShowTip(text, rect);
		area.MouseExited += HideTip;
		Root.AddChild(area);
	}

	void ShowTip(string text, Rect2 anchor)
	{
		if (tipBox == null || !GodotObject.IsInstanceValid(tipBox))
		{
			tipBox = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 100 };
			var st = new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.09f, 0.97f), BorderColor = GOLD };
			st.SetBorderWidthAll(2);
			st.SetCornerRadiusAll(8);
			tipBox.AddThemeStyleboxOverride("panel", st);
			tipLabel = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(12, 8), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			tipLabel.AddThemeFontSizeOverride("font_size", 16);
			tipLabel.AddThemeColorOverride("font_color", CREAM);
			tipBox.AddChild(tipLabel);
			Root.AddChild(tipBox);
		}
		const float width = 300f;
		tipLabel.Text = text;
		tipLabel.Size = new Vector2(width, 20f);
		var measured = Font != null ? Font.GetMultilineStringSize(text, HorizontalAlignment.Left, width, 16) : new Vector2(width, 60f);
		tipLabel.Size = new Vector2(width, measured.Y + 4f);
		var size = new Vector2(width + 24f, measured.Y + 24f);
		float x = Mathf.Clamp(anchor.Position.X, 8f, 1280f - size.X - 8f);
		float y = anchor.End.Y + 8f;
		if (y + size.Y > 792f) y = Mathf.Max(8f, anchor.Position.Y - size.Y - 8f);
		tipBox.Position = new Vector2(x, y);
		tipBox.Size = size;
		tipBox.Visible = true;
		Root.MoveChild(tipBox, Root.GetChildCount() - 1);
	}

	public void PreviewTip(bool stat)
	{
		if (stat) ShowTip(StatTip("暴击率"), new Rect2(1016, 285, 226, 30));
		else ShowTip(ItemTip(0, 2), new Rect2(18, 666, 48, 48));
	}

	void HideTip()
	{
		if (tipBox != null && GodotObject.IsInstanceValid(tipBox)) tipBox.Visible = false;
	}

	static readonly Dictionary<string, string> StatTips = new()
	{
		["目前职级"] = "击败怪物获得经验,经验满了升一级。每升一级,波次结束后可以选一项技能。\n上限:没有等级上限。",
		["最大生命值"] = "生命上限,归零则本局失败。每一波开始时恢复 12% 生命。\n上限:600。",
		["生命再生"] = "每秒自动恢复的生命值。\n上限:每秒 8 点。",
		["伤害加成"] = "所有武器伤害的百分比加成,越高每一击越疼。\n上限:+400%。",
		["攻击速度"] = "武器冷却缩短,出手更频繁。百分比越高,攻击越快。\n上限:+200%;被道具拖慢时最低降到 -70%。",
		["暴击率"] = "每次攻击有这个概率造成两倍伤害。\n上限:85%。",
		["护甲"] = "降低受到的所有伤害,数值越高减伤越多,但越往后收益越小。\n上限:35 点(减伤约 68%)。",
		["移动速度"] = "每秒移动的像素数,越快越容易躲开怪物和弹幕。\n上限:380。",
		["拾取范围"] = "金币和回血爱心被自动吸过来的距离。\n上限:400。",
		["已击败"] = "本局累计击败的怪物数量。",
	};

	string StatTip(string name)
	{
		string text = StatTips.GetValueOrDefault(name, "");
		if (name == "暴击率") text += $"\n当前 {U.RoundI(Game.Crit * 100)}%。";
		if (name == "护甲")
		{
			float reduction = Game.Armor * 6f / (100f + Game.Armor * 6f);
			text += $"\n当前减伤约 {U.RoundI(reduction * 100)}%。";
		}
		return text;
	}

	string WeaponTip(Weapon w)
	{
		var d = Game.Balance.Weapons[w.Type];
		float dmg = d.Damage * Game.DamageMult * (1f + (w.Tier - 1) * 0.62f);
		string text = $"{d.Name} · {TierNames[w.Tier - 1]}\n{d.Description}\n伤害 {(int)dmg}   冷却 {U.Fmt(d.Cooldown, 2)} 秒   范围 {(int)d.Range}";
		text += w.Tier < 4 ? "\n持有两把同名同阶的,可以在商店里免费合成升一阶;也可以花钱直接“升阶”。" : "\n已是最高阶。";
		return text;
	}

	string ItemTip(int index, int count)
	{
		var it = Game.Balance.Items[index];
		string text = it.Name + (count > 1 ? $"  ×{count}" : "");
		foreach (var line in Game.ItemLines(it)) text += "\n" + line.Text + (count > 1 ? $"(每件)" : "");
		if (count > 1) text += "\n效果按数量叠加。";
		if (count >= Main.MaxItemCopies) text += "\n已达持有上限,商店不再出售。";
		return text;
	}

	StyleBoxFlat Style(Color bg, Color? border = null, int radius = 10)
	{
		var s = new StyleBoxFlat { BgColor = bg, BorderColor = border ?? new Color("484b51") };
		s.SetBorderWidthAll(1);
		s.SetCornerRadiusAll(radius);
		return s;
	}

	Panel Pnl(Rect2 rect, Color? bg = null, Color? border = null)
	{
		var p = new Panel { Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("panel", Style(bg ?? PANEL, border));
		Root.AddChild(p);
		return p;
	}

	Label Lbl(string text, Rect2 rect, int size = 16, Color? color = null)
	{
		var l = new Label { Text = text, Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", color ?? CREAM);
		Root.AddChild(l);
		return l;
	}

	static Label Center(Label l)
	{
		l.HorizontalAlignment = HorizontalAlignment.Center;
		return l;
	}

	static Label Right(Label l)
	{
		l.HorizontalAlignment = HorizontalAlignment.Right;
		return l;
	}

	Label Outlined(Label l, bool center = false)
	{
		l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		l.AddThemeConstantOverride("outline_size", 5);
		if (center) l.HorizontalAlignment = HorizontalAlignment.Center;
		return l;
	}

	Button Btn(string text, Rect2 rect, Action callback, bool primary = false, bool disabled = false)
	{
		var b = new Button { Text = text, Position = rect.Position, Size = rect.Size, Disabled = disabled, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
		b.AddThemeFontSizeOverride("font_size", 17);
		b.AddThemeColorOverride("font_color", primary ? new Color("22252a") : CREAM);
		b.AddThemeColorOverride("font_hover_color", primary ? new Color("191c21") : Colors.White);
		b.AddThemeColorOverride("font_pressed_color", primary ? new Color("191c21") : CREAM);
		b.AddThemeColorOverride("font_disabled_color", new Color("6e7177"));
		b.AddThemeStyleboxOverride("normal", Style(primary ? JADE : new Color("2c3036"), primary ? JADE : new Color("555a63")));
		b.AddThemeStyleboxOverride("hover", Style(primary ? new Color("c5cddb") : new Color("3c4047"), GOLD));
		b.AddThemeStyleboxOverride("pressed", Style(primary ? new Color("98a3b7") : new Color("44484f"), GOLD));
		b.AddThemeStyleboxOverride("focus", Style(new Color(0f, 0f, 0f, 0f), GOLD));
		b.AddThemeStyleboxOverride("disabled", Style(new Color("25282d"), new Color("3a3d42")));
		b.Pressed += () => Game.Sound("click");
		b.MouseEntered += () => { if (!b.Disabled) Game.Sound("hover"); };
		b.Pressed += callback;
		Root.AddChild(b);
		return b;
	}

	TextureRect Pic(Texture2D texture, Rect2 rect, Color? color = null)
	{
		var image = new TextureRect { Texture = texture, Position = rect.Position, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = rect.Size, Modulate = color ?? Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore };
		Root.AddChild(image);
		image.SetDeferred(Control.PropertyName.Size, rect.Size);
		return image;
	}

	ProgressBar Bar(Rect2 rect, Color color)
	{
		var p = new ProgressBar { Position = rect.Position, ShowPercentage = false, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("background", Style(new Color("1a1c1f"), new Color("484c52"), 4));
		p.AddThemeStyleboxOverride("fill", Style(color, color, 4));
		Root.AddChild(p);
		p.SetDeferred(Control.PropertyName.Size, rect.Size);
		return p;
	}

	void Dim(float alpha = 0.85f)
	{
		Root.AddChild(new ColorRect { Color = new Color(0.045f, 0.05f, 0.065f, alpha), Size = new Vector2(1280, 800) });
	}

	public void ShowTitle()
	{
		Clear();
		Pnl(new Rect2(42, 34, 1196, 730), new Color(0.044f, 0.052f, 0.066f, 0.76f), new Color(0.56f, 0.608f, 0.69f, 0.23f));
		Lbl("肉 鸽 D e m o   /   办 公 室 生 存 战", new Rect2(78, 61, 600, 35), 15, JADE);
		Lbl("加班求生", new Rect2(70, 134, 670, 100), 74, CREAM);
		Lbl("一人上岗，万物皆敌。", new Rect2(78, 250, 600, 46), 27, GOLD);
		Lbl("自由走位  ·  七件文具  ·  二十轮加班", new Rect2(80, 312, 650, 35), 20, MUTED);
		Lbl("击退失控的办公用品，选好装备，熬过每一轮加班。", new Rect2(80, 354, 665, 32), 16, MUTED);
		Pnl(new Rect2(800, 130, 382, 450), new Color(0.142f, 0.155f, 0.178f, 0.45f), new Color(0.562f, 0.612f, 0.698f, 0.3f));
		var hero = Game.Balance.Heroes[Game.HeroIndex];
		Lbl("今日值班  /  " + hero.Title, new Rect2(825, 151, 335, 28), 14, JADE);
		Pic(GD.Load<Texture2D>($"res://assets/portraits/{hero.Model}.png"), new Rect2(839, 203, 294, 302));
		Lbl(hero.Name, new Rect2(847, 509, 240, 40), 30, CREAM);
		for (int i = 0; i < Game.Balance.Heroes.Count; i++)
		{
			int idx = i;
			var h = Game.Balance.Heroes[i];
			float x = 80 + i * 223;
			bool selected = i == Game.HeroIndex;
			Pnl(new Rect2(x, 427, 205, 153), selected ? new Color("35393f") : new Color("1e2127"), selected ? JADE : new Color("484b51"));
			Pic(GD.Load<Texture2D>($"res://assets/portraits/{h.Model}.png"), new Rect2(x + 8, 440, 67, 78));
			Lbl(h.Name, new Rect2(x + 87, 444, 110, 30), 22, selected ? JADE : CREAM);
			Lbl(h.Title, new Rect2(x + 86, 480, 115, 25), 13, MUTED);
			Btn(selected ? "已选角色" : "选择角色", new Rect2(x + 13, 529, 179, 34), () => { Game.HeroIndex = idx; ShowTitle(); }, false, selected);
		}
		Lbl(hero.Description.Replace("\n", "   "), new Rect2(80, 594, 1000, 30), 15, MUTED);
		Btn("打卡上班    →", new Rect2(80, 654, 319, 62), Game.BeginRun, true);
		Btn("退出游戏", new Rect2(423, 654, 164, 62), () => Game.GetTree().Quit());
		Lbl($"最高坚持  {Game.BestWave:00} / 20    ·    下班  {Game.Victories} 次", new Rect2(816, 640, 370, 30), 16, GOLD);
		Lbl("WASD / 方向键移动　空格闪避\n自动攻击　Esc 暂停　F11 全屏", new Rect2(815, 678, 370, 56), 14, MUTED);
		Lbl("AI 美术资源  ·  单机离线运行", new Rect2(80, 732, 650, 21), 12, new Color("878a90"));
	}

	public void ShowHud()
	{
		Clear();
		// 战斗界面参照《土豆兄弟》：左上角紧凑状态、左侧竖排武器、顶部居中波次与倒计时，不使用通栏。
		Pnl(new Rect2(8, 8, 262, 104), new Color(0.048f, 0.053f, 0.062f, 0.55f), new Color(0f, 0f, 0f, 0f));
		HpBar = Bar(new Rect2(16, 16, 246, 26), new Color("d1493f"));
		HpLabel = Outlined(Lbl("", new Rect2(16, 17, 246, 24), 15), true);
		XpBar = Bar(new Rect2(16, 48, 246, 22), new Color("62b84a"));
		HintLabel = Outlined(Lbl("", new Rect2(16, 48, 246, 22), 14), true);
		CashLabel = Outlined(Lbl("", new Rect2(18, 76, 240, 32), 24, GOLD));
		PendingLabel = Outlined(Lbl("", new Rect2(130, 82, 132, 24), 14, new Color("ffd45e")), true);
		Pnl(new Rect2(8, 120, 66, 6 * 52 + 10), new Color(0.048f, 0.053f, 0.062f, 0.55f), new Color(0f, 0f, 0f, 0f));
		for (int i = 0; i < 6; i++)
		{
			float y = 126 + i * 52;
			bool filled = i < Game.Weapons.Count;
			Pnl(new Rect2(14, y, 54, 46), new Color("262a30"), filled && Game.Weapons[i].Tier > 1 ? TierColors[Game.Weapons[i].Tier - 1] : new Color("484d55"));
			if (filled)
			{
				var w = Game.Weapons[i];
				var data = Game.Balance.Weapons[w.Type];
				Pic(Game.Textures[data.Image], new Rect2(18, y + 3, 46, 26));
				Center(Lbl(TierNames[w.Tier - 1], new Rect2(14, y + 28, 54, 16), 12, TierColors[w.Tier - 1]));
				Tip(new Rect2(14, y, 54, 46), WeaponTip(w));
			}
			else Lbl("＋", new Rect2(31, y + 10, 24, 26), 18, new Color("585d65"));
		}
		WaveLabel = Outlined(Lbl("", new Rect2(490, 10, 300, 34), 26), true);
		TimeLabel = Outlined(Lbl("", new Rect2(490, 42, 300, 50), 40, GOLD), true);
		Btn("暂停  Ⅱ", new Rect2(1190, 14, 76, 34), () => { Game.PreviousPhase = "running"; Game.SetPhase("paused"); ShowPause(); });
		DashLabel = Outlined(Lbl("", new Rect2(490, 762, 300, 24), 15, JADE), true);
		Right(Outlined(Lbl("WASD 移动  /  自动攻击  ·  Esc 暂停", new Rect2(880, 768, 380, 20), 12)));
		NoticeLabel = Outlined(Lbl("", new Rect2(220, 98, 840, 29), 17, GOLD), true);
		BossLabel = Outlined(Lbl("复印机首领", new Rect2(414, 128, 450, 25), 15, new Color("f1b194")), true);
		BossBar = Bar(new Rect2(415, 156, 450, 7), new Color("d79473"));
		// 每一波开始的大横幅
		BannerTitleLabel = Outlined(Lbl("", new Rect2(140, 200, 1000, 100), 80, GOLD), true);
		BannerSubLabel = Outlined(Lbl("", new Rect2(140, 306, 1000, 44), 28), true);
		UpdateHud();
	}

	public void UpdateHud()
	{
		if (Game.Phase != "running" || HpLabel == null) return;
		if (BannerTitleLabel != null)
		{
			float t = Game.BannerTimer;
			float age = Main.BannerLen - t;
			float a = t > 0f ? Mathf.Clamp(Mathf.Min(age / 0.25f, t / 0.7f), 0f, 1f) : 0f;
			BannerTitleLabel.Text = Game.BannerTitle;
			BannerSubLabel.Text = Game.BannerSub;
			BannerTitleLabel.Modulate = new Color(1, 1, 1, a);
			BannerSubLabel.Modulate = new Color(1, 1, 1, a);
		}
		HpLabel.Text = $"{Mathf.CeilToInt(Game.Hp)} / {(int)Game.MaxHp}";
		HpBar.Value = Game.Hp / Game.MaxHp * 100f;
		XpBar.Value = (float)Game.Xp / Game.NextXp * 100f;
		WaveLabel.Text = Game.Wave == Game.Balance.WaveCount ? $"第 {Game.Wave:00} / 20 波 · 最终波" : $"第 {Game.Wave:00} / 20 波";
		int left = (int)Mathf.Max(0f, Game.WaveTime - Game.Elapsed);
		TimeLabel.Text = $"{left / 60:00}:{left % 60:00}";
		CashLabel.Text = $"奖金  {Game.Cash}";
		PendingLabel.Text = Game.PendingUpgrades > 0 ? $"待选技能 ×{Game.PendingUpgrades}" : "";
		HintLabel.Text = $"职级 {Game.Level}   ·   清理 {Game.Kills}";
		DashLabel.Text = Game.DashCooldown <= 0f ? "空格闪避  ·  已就绪" : $"闪避冷却  {Game.DashCooldown:0.0} 秒";
		NoticeLabel.Text = Game.NoticeTimer > 0f ? Game.Notice : "";
		Enemy boss = null;
		foreach (var e in Game.Enemies)
		{
			if (e.Type == 7 && e.Hp > 0f)
			{
				boss = e;
				break;
			}
		}
		BossBar.Visible = boss != null;
		BossLabel.Visible = boss != null;
		if (boss != null)
		{
			BossBar.Value = boss.Hp / boss.MaxHp * 100f;
			if (Game.Elapsed >= Game.WaveTime) NoticeLabel.Text = "击败复印机首领，即可完成本波";
		}
	}

	Texture2D UiIcon(string iconName)
	{
		if (iconName == "") return null;
		if (iconCache.TryGetValue(iconName, out var cached)) return cached;
		string path = $"res://assets/ui/{iconName}.png";
		var tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		iconCache[iconName] = tex;
		return tex;
	}

	void StatRow(string name, string value, float x, float y, float w, Color? color = null, string icon = "", bool tip = false)
	{
		if (tip) Tip(new Rect2(x - 6, y - 3, w + 12, 30), StatTip(name));
		var tex = UiIcon(icon);
		if (tex != null)
		{
			Pic(tex, new Rect2(x, y - 1, 26, 26));
			Lbl(name, new Rect2(x + 34, y, w * 0.6f, 24), 15, MUTED);
		}
		else Lbl(name, new Rect2(x, y, w * 0.6f, 24), 15, MUTED);
		Right(Lbl(value, new Rect2(x + w * 0.4f, y, w * 0.6f, 24), 15, color ?? CREAM));
	}

	void StatsPanel()
	{
		Pnl(new Rect2(996, 12, 266, 676), new Color(0.06f, 0.065f, 0.08f, 1f), new Color("414346"));
		Center(Lbl("属性", new Rect2(996, 22, 266, 40), 26));
		var rows = new (string, string, string)[]
		{
			("目前职级", $"{Game.Level}", "Stat_Level"),
			("最大生命值", $"{(int)Game.MaxHp}", "Stat_Hp"),
			("生命再生", U.Fmt(Game.Regeneration), "Stat_Regen"),
			("伤害加成", $"{U.RoundI((Game.DamageMult - 1f) * 100):+0;-0;+0}%", "Stat_Damage"),
			("攻击速度", $"{U.RoundI((Game.AttackSpeed - 1f) * 100):+0;-0;+0}%", "Stat_Attack"),
			("暴击率", $"{U.RoundI(Game.Crit * 100)}%", "Stat_Crit"),
			("护甲", $"{(int)Game.Armor}", "Stat_Armor"),
			("移动速度", $"{(int)Game.Speed}", "Stat_Move"),
			("拾取范围", $"{(int)Game.PickupRange}", "Stat_Pickup"),
			("已击败", $"{Game.Kills}", "Stat_Kills"),
		};
		for (int i = 0; i < rows.Length; i++)
			StatRow(rows[i].Item1, rows[i].Item2, 1016, 80 + i * 34, 226, i > 0 ? new Color("7dff7d") : CREAM, rows[i].Item3, true);
	}

	void ItemIcon(Texture2D texture, string name, Rect2 rect, Color? tint = null)
	{
		// 有图标用图标;还没有素材时退化为名称首字，保证界面可用。
		if (texture != null) Pic(texture, rect, tint);
		else Center(Lbl(name.Substring(0, 1), rect, (int)(rect.Size.Y * 0.7f), GOLD));
	}

	public void ShowShop()
	{
		// 布局参照《土豆兄弟》：标题与奖金在顶部，商品卡下方是锁定按钮，右侧属性面板，右下角出发。
		Clear();
		Dim(0.95f);
		Lbl($"商店（第 {Game.Wave} 波）", new Rect2(18, 16, 420, 48), 32);
		Center(Lbl($"奖金  {Game.Cash}", new Rect2(440, 10, 320, 60), 40, GOLD));
		Btn($"刷新 · {Game.RefreshCost} 奖金", new Rect2(770, 12, 208, 56), Game.Reroll, false, Game.Cash < Game.RefreshCost);
		var cardBg = new Color(0.075f, 0.08f, 0.095f, 1f);
		for (int i = 0; i < Game.Offers.Count; i++)
		{
			int idx = i;
			var offer = Game.Offers[i];
			float x = 18 + i * 240;
			float y = 100;
			bool merge = false;
			if (offer.IsItem)
			{
				var it = Game.Balance.Items[offer.Item];
				bool rare = it.Price >= 80;
				Pnl(new Rect2(x, y, 230, 330), rare ? new Color(0.13f, 0.09f, 0.2f, 1f) : cardBg, rare ? new Color("b57cff") : new Color("e0a64a"));
				ItemIcon(Game.Textures.GetValueOrDefault(it.Icon), it.Name, new Rect2(x + 12, y + 12, 56, 56), offer.Sold ? new Color(1, 1, 1, 0.3f) : Colors.White);
				Lbl(it.Name, new Rect2(x + 84, y + 12, 140, 32), 22, new Color("ffd45e"));
				Lbl(rare ? "稀有道具" : "道具", new Rect2(x + 84, y + 42, 140, 22), 13, rare ? new Color("c9a2ff") : MUTED);
				var lines = Game.ItemLines(it);
				for (int k = 0; k < lines.Count; k++)
					Lbl(lines[k].Text, new Rect2(x + 16, y + 96 + k * 32, 200, 28), 18, lines[k].Good ? new Color("7dff7d") : new Color("ff7a6e"));
				int have = Game.Items.GetValueOrDefault(offer.Item, 0);
				if (have > 0) Lbl($"已持有 ×{have}", new Rect2(x + 16, y + 232, 200, 24), 14, MUTED);
			}
			else
			{
				var w = Game.Balance.Weapons[offer.Type];
				var color = TierColors[offer.Tier - 1];
				Pnl(new Rect2(x, y, 230, 330), cardBg, offer.Tier > 1 ? color : new Color("585d66"));
				Pic(Game.Textures[w.Image], new Rect2(x + 12, y + 12, 64, 52), offer.Sold ? new Color(1, 1, 1, 0.3f) : Colors.White);
				Lbl(w.Name, new Rect2(x + 84, y + 12, 140, 32), 22, color);
				Lbl(TierNames[offer.Tier - 1] + "  /  文具", new Rect2(x + 84, y + 42, 140, 22), 13, MUTED);
				var desc = Lbl(w.Description, new Rect2(x + 16, y + 76, 200, 48), 14);
				desc.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
				StatRow("基础伤害", $"{(int)(w.Damage * (1 + (offer.Tier - 1) * 0.62f))}", x + 16, y + 140, 198, new Color("7dff7d"));
				StatRow("冷却", $"{U.Fmt(w.Cooldown, 2)} 秒", x + 16, y + 168, 198);
				StatRow("范围", $"{(int)w.Range}", x + 16, y + 196, 198);
				foreach (var owned in Game.Weapons)
					if (owned.Type == offer.Type && owned.Tier == offer.Tier && offer.Tier < 4) merge = true;
			}
			if (Game.LockedOffers.Contains(i)) Lbl("已锁定", new Rect2(x + 150, y + 232, 70, 24), 14, GOLD);
			string caption = offer.Sold ? "已购入" : (merge ? $"合成 · {offer.Price}" : $"{offer.Price} 奖金");
			bool blocked = offer.Sold || Game.Cash < offer.Price || (!offer.IsItem && Game.Weapons.Count >= 6 && !merge);
			var buyBtn = Btn(caption, new Rect2(x + 15, y + 272, 200, 46), () => Game.Purchase(idx), !offer.Sold, blocked);
			if (merge && !offer.Sold)
			{
				// 可合成:绿色粗边框 + 呼吸光晕 + 顶部标签 + 绿色按钮,一眼能认出来
				var go = new Color("5dff8a");
				var glow = new Panel { Position = new Vector2(x - 7, y - 7), Size = new Vector2(244, 344), MouseFilter = Control.MouseFilterEnum.Ignore };
				var gs = new StyleBoxFlat { BgColor = new Color(go, 0.10f), BorderColor = new Color(go, 0.55f) };
				gs.SetBorderWidthAll(5);
				gs.SetCornerRadiusAll(14);
				glow.AddThemeStyleboxOverride("panel", gs);
				Root.AddChild(glow);
				var pulse = glow.CreateTween().SetLoops();
				pulse.TweenProperty(glow, "modulate:a", 0.3f, 0.5f);
				pulse.TweenProperty(glow, "modulate:a", 1f, 0.5f);
				var edge = new Panel { Position = new Vector2(x, y), Size = new Vector2(230, 330), MouseFilter = Control.MouseFilterEnum.Ignore };
				var es = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = go };
				es.SetBorderWidthAll(3);
				es.SetCornerRadiusAll(10);
				edge.AddThemeStyleboxOverride("panel", es);
				Root.AddChild(edge);
				var tag = Pnl(new Rect2(x + 55, y - 17, 120, 30), go, go);
				var tagText = Lbl("▲ 可合成升阶", new Rect2(x + 55, y - 15, 120, 26), 15, new Color("0d2616"));
				tagText.HorizontalAlignment = HorizontalAlignment.Center;
				foreach (var st in new[] { "normal", "hover", "pressed" })
				{
					var bs = new StyleBoxFlat { BgColor = st == "hover" ? new Color("7dffa4") : (st == "pressed" ? new Color("3fd96b") : go), BorderColor = Colors.White };
					bs.SetBorderWidthAll(2);
					bs.SetCornerRadiusAll(10);
					buyBtn.AddThemeStyleboxOverride(st, bs);
				}
				buyBtn.AddThemeColorOverride("font_color", new Color("0d2616"));
				buyBtn.AddThemeColorOverride("font_hover_color", new Color("0d2616"));
				buyBtn.AddThemeColorOverride("font_pressed_color", new Color("0d2616"));
			}
			Btn(Game.LockedOffers.Contains(i) ? "解锁" : "锁定", new Rect2(x, y + 340, 230, 38), () => Game.ToggleLock(idx), false, offer.Sold);
		}
		StatsPanel();
		OwnedPanels(true);
		string hint = "锁定的商品会保留到下一轮。开工时恢复 12% 生命。";
		bool warn = Game.NoticeTimer > 0f && Game.Notice.StartsWith("武器已满");
		Lbl(warn ? Game.Notice : hint, new Rect2(18, 774, 820, 26), warn ? 15 : 13, warn ? GOLD : MUTED);
		Btn(Game.Wave + 1 == Game.Balance.WaveCount ? "出发（最终波）" : $"出发（第 {Game.Wave + 1} 波）", new Rect2(996, 708, 266, 68), Game.NextWave, true);
	}

	// 已持有的武器与道具(商店里可出售,暂停和结算里只读)
	void OwnedPanels(bool sell)
	{
		var owned = new Color(0.075f, 0.08f, 0.095f, 1f);
		Lbl($"武器（{Game.Weapons.Count} / 6）", new Rect2(18, 486, 420, 30), 21);
		if (sell) Right(Lbl("购买同名同阶文具自动合成，最高四阶", new Rect2(290, 492, 680, 24), 13, MUTED));
		for (int i = 0; i < 6; i++)
		{
			int idx = i;
			float x = 18 + i * 160;
			Pnl(new Rect2(x, 520, 150, 106), owned, new Color("424852"));
			if (i < Game.Weapons.Count)
			{
				var own = Game.Weapons[i];
				var data = Game.Balance.Weapons[own.Type];
				Pic(Game.Textures[data.Image], new Rect2(x + 10, 524, 130, 36));
				Tip(new Rect2(x, 520, 150, 66), WeaponTip(own));
				Lbl(data.Name + "  " + TierNames[own.Tier - 1], new Rect2(x + 8, 562, 140, 22), 14, TierColors[own.Tier - 1]);
				int price = Game.WeaponSellPrice(own);
				if (sell && Game.CanMerge(idx))
				{
					var mb = Btn("合成", new Rect2(x + 10, 588, 66, 30), () => Game.MergeWeapons(idx), true);
					Btn($"卖 +{price}", new Rect2(x + 80, 588, 60, 30), () => Game.SellWeapon(idx), false, Game.Weapons.Count <= 1);
					var go = new Color("5dff8a");
					foreach (var st in new[] { "normal", "hover", "pressed" })
					{
						var bs = new StyleBoxFlat { BgColor = st == "hover" ? new Color("7dffa4") : go, BorderColor = Colors.White };
						bs.SetBorderWidthAll(2);
						bs.SetCornerRadiusAll(8);
						mb.AddThemeStyleboxOverride(st, bs);
					}
					mb.AddThemeColorOverride("font_color", new Color("0d2616"));
					mb.AddThemeColorOverride("font_hover_color", new Color("0d2616"));
				}
				else if (sell)
				{
					int upCost = Game.UpgradeCost(idx);
					if (upCost >= 0)
					{
						Btn($"升阶 {upCost}", new Rect2(x + 8, 588, 86, 30), () => Game.UpgradeWeapon(idx), false, Game.Cash < upCost);
						Btn($"卖 +{price}", new Rect2(x + 98, 588, 44, 30), () => Game.SellWeapon(idx), false, Game.Weapons.Count <= 1);
					}
					else Btn($"出售 +{price}", new Rect2(x + 10, 588, 130, 30), () => Game.SellWeapon(idx), false, Game.Weapons.Count <= 1);
				}
			}
			else Center(Lbl("空余格子", new Rect2(x, 560, 150, 28), 15, new Color("60765b")));
		}
		// 道具区：已持有的道具与数量
		Lbl("道具", new Rect2(18, 634, 200, 28), 21);
		int shown = 0;
		foreach (var kv in Game.Items)
		{
			var it = Game.Balance.Items[kv.Key];
			// 一行放满就换行,避免盖住右下角的按钮
			float x = 18 + (shown % 17) * 54;
			float iy = 666 + (shown / 17) * 54;
			Pnl(new Rect2(x, iy, 48, 48), owned, new Color("e0a64a"));
			ItemIcon(Game.Textures.GetValueOrDefault(it.Icon), it.Name, new Rect2(x + 4, iy + 2, 40, 36));
			Center(Lbl($"×{kv.Value}", new Rect2(x, iy + 32, 48, 16), 12));
			Tip(new Rect2(x, iy, 48, 48), ItemTip(kv.Key, kv.Value));
			shown++;
		}
		if (shown == 0) Lbl("还没有道具", new Rect2(18, 676, 300, 24), 14, MUTED);
	}

	static readonly Dictionary<string, string> UpgradeIcons = new()
	{
		["hp"] = "Upgrade_Gym", ["damage"] = "Upgrade_Trophy", ["attack"] = "Upgrade_Coffee", ["speed"] = "Upgrade_Sneaker",
		["armor"] = "Upgrade_Vest", ["crit"] = "Upgrade_Focus", ["regen"] = "Upgrade_Nap", ["pickup"] = "Upgrade_Bonus",
	};

	public void ShowUpgrades()
	{
		Clear();
		Dim(0.95f);
		Lbl($"升级！职级 {Game.Level}", new Rect2(18, 16, 420, 48), 32);
		Center(Lbl($"还有 {Game.PendingUpgrades} 次技能选择", new Rect2(440, 10, 320, 60), 28, GOLD));
		for (int i = 0; i < Game.Upgrades.Count; i++)
		{
			int idx = i;
			var u = Game.Upgrades[i];
			float x = 18 + i * 240;
			float y = 100;
			var color = TierColors[u.Tier - 1];
			Pnl(new Rect2(x, y, 230, 330), new Color(0.075f, 0.08f, 0.095f, 1f), u.Tier > 1 ? color : new Color("595c61"));
			var iconTex = UiIcon(UpgradeIcons.GetValueOrDefault(u.Stat, ""));
			if (iconTex != null) Pic(iconTex, new Rect2(x + 55, y + 16, 120, 88));
			else Center(Lbl(u.Glyph, new Rect2(x, y + 16, 230, 84), 60, color));
			Center(Lbl(u.Name, new Rect2(x, y + 108, 230, 40), 26, color));
			Center(Lbl(TierNames[u.Tier - 1], new Rect2(x, y + 148, 230, 22), 13, MUTED));
			var desc = Lbl(u.Desc, new Rect2(x + 18, y + 184, 194, 70), 16);
			desc.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
			desc.CustomMinimumSize = new Vector2(194, 0);
			desc.Size = new Vector2(194, 70);
			Btn("选择", new Rect2(x + 15, y + 272, 200, 46), () => Game.ChooseUpgrade(idx), true);
		}
		StatsPanel();
		Lbl("技能只在本局生效，可与文具自由搭配。选完所有升级后进入商店。", new Rect2(18, 700, 820, 24), 13, MUTED);
	}

	// 开局选择起始文具:角色专属文具带"专属"标记并以正式级开局,也可以改选其他文具
	public void ShowPickWeapon()
	{
		Clear();
		Dim(0.95f);
		var hero = Game.Balance.Heroes[Game.HeroIndex];
		Btn("←  返回", new Rect2(22, 22, 160, 38), Game.ReturnTitle);
		// 左:角色与特点;右:悬停文具的详情;下:文具一览,第一格为随机
		var panelBg = new Color(0.11f, 0.125f, 0.15f, 1f);
		Pnl(new Rect2(262, 100, 510, 340), panelBg, new Color("3d424b"));
		Center(Lbl(hero.Name, new Rect2(262, 106, 510, 60), 44));
		Pnl(new Rect2(278, 176, 180, 190), new Color("2b2d33"), new Color("111214"));
		Pic(GD.Load<Texture2D>($"res://assets/portraits/{hero.Model}.png"), new Rect2(284, 182, 168, 178));
		Lbl(hero.Title, new Rect2(278, 376, 180, 28), 18, GOLD);
		var traits = hero.Description.Split('\n');
		if (traits.Length > 1)
		{
			int row = 0;
			foreach (var t in traits[1].Split('·'))
			{
				string line = t.Trim();
				if (line == "") continue;
				Lbl("• " + line, new Rect2(476, 190 + row * 36, 290, 30), 17, new Color("7dff7d"));
				row++;
			}
		}
		Pnl(new Rect2(781, 100, 290, 340), new Color(0.03f, 0.035f, 0.045f, 1f), new Color("3d424b"));
		var icon = Pic(Game.Textures[Game.Balance.Weapons[0].Image], new Rect2(793, 114, 72, 56));
		var nameL = Lbl("", new Rect2(878, 112, 190, 34), 24);
		var tierL = Lbl("", new Rect2(878, 146, 190, 24), 15, GOLD);
		var statL = Lbl("", new Rect2(793, 186, 270, 110), 17);
		var descL = Lbl("", new Rect2(793, 306, 270, 120), 15, MUTED);
		descL.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
		descL.CustomMinimumSize = new Vector2(270, 0);
		descL.Size = new Vector2(270, 120);
		void Detail(int type)
		{
			if (type < 0)
			{
				icon.Texture = null;
				nameL.Text = "随机文具";
				tierL.Text = "实习级";
				statL.Text = "";
				descL.Text = "随机抽取一件文具上岗,听天由命。";
				return;
			}
			var w = Game.Balance.Weapons[type];
			bool signature = type == hero.Weapon;
			int tier = signature ? 2 : 1;
			icon.Texture = Game.Textures[w.Image];
			nameL.Text = w.Name;
			tierL.Text = signature ? $"{hero.Name}专属 · {TierNames[tier - 1]}" : TierNames[0];
			statL.Text = $"伤害：{(int)(w.Damage * (1 + (tier - 1) * 0.62f))}\n冷却：{U.Fmt(w.Cooldown, 2)} 秒\n范围：{(int)w.Range}";
			descL.Text = w.Description;
		}
		int count = Game.Balance.Weapons.Count;
		for (int i = -1; i < count; i++)
		{
			int type = i;
			float x = 114 + (i + 1) * 70;
			bool signature = i == hero.Weapon;
			var b = Btn("", new Rect2(x, 536, 64, 64), () => Game.ChooseStartWeapon(type), false);
			b.AddThemeStyleboxOverride("normal", Style(signature ? new Color("4a4326") : new Color("23262c"), signature ? GOLD : new Color("3b3f47"), 6));
			b.AddThemeStyleboxOverride("hover", Style(new Color("e1e4ea"), GOLD, 6));
			b.MouseEntered += () => Detail(type);
			if (i < 0) Center(Lbl("？", new Rect2(x, 540, 64, 56), 40, new Color("c0392b")));
			else Pic(Game.Textures[Game.Balance.Weapons[i].Image], new Rect2(x + 4, 544, 56, 44));
			if (signature) Lbl("★", new Rect2(x + 3, 536, 20, 20), 14, GOLD);
		}
		Detail(hero.Weapon);
		Lbl("★ 为角色专属文具,正式级开局。点击文具开始上岗。", new Rect2(114, 616, 800, 26), 14, MUTED);
	}

	public void ShowWaveClear()
	{
		Clear();
		Outlined(Lbl($"第 {Game.Wave} 波完成！", new Rect2(140, 220, 1000, 100), 80, GOLD), true);
		string extra = Game.PendingUpgrades > 0 ? $"　·　待选技能 ×{Game.PendingUpgrades}" : "";
		Outlined(Lbl($"本波清理 {Game.WaveKills}　·　奖金 +{Game.ClearBonus}{extra}", new Rect2(140, 330, 1000, 44), 28), true);
	}

	public void ShowVictory()
	{
		Clear();
		Outlined(Lbl("准时下班！", new Rect2(140, 230, 1000, 130), 100, GOLD), true);
		Outlined(Lbl("二十轮加班，全部熬过去了", new Rect2(140, 380, 1000, 50), 30), true);
	}

	public void ShowPause()
	{
		// 与商店、升级界面同一套版式:左上标题,左侧按钮,右侧属性,底部是持有的武器与道具
		Clear();
		Dim(0.95f);
		Lbl("暂停", new Rect2(18, 16, 420, 48), 32);
		Center(Lbl("怪物静止，等你回来。", new Rect2(440, 22, 320, 40), 22, MUTED));
		Btn("继续加班", new Rect2(18, 100, 300, 64), Game.Resume, true);
		Btn(Game.Muted ? "音效：关闭" : "音效：开启", new Rect2(18, 180, 300, 52), Game.ToggleMute);
		Btn("返回首页 · 结束本局", new Rect2(18, 248, 300, 52), Game.ReturnTitle);
		Lbl("WASD / 方向键：移动\n空格：闪避（短暂无敌）\nEsc / P：暂停　M：静音　F11：全屏", new Rect2(360, 104, 600, 110), 17, MUTED);
		Lbl($"本局：第 {Game.Wave:00} / 20 波　·　清理 {Game.Kills}　·　职级 {Game.Level}", new Rect2(360, 236, 600, 30), 17, GOLD);
		StatsPanel();
		OwnedPanels(false);
	}

	public void ShowResult(bool won)
	{
		Clear();
		Dim(0.95f);
		Lbl(won ? "准时下班" : "明天继续", new Rect2(18, 12, 600, 64), 48, won ? GOLD : CREAM);
		Lbl(won ? "二十轮加班，终于熬完。怪物散尽，办公室恢复安静。" : "今日加班，暂告一段。整理文具，明天再战。", new Rect2(18, 80, 900, 30), 18, MUTED);
		var cardBg = new Color(0.075f, 0.08f, 0.095f, 1f);
		var items = new[] { ("坚持波次", $"{Game.Wave:00} / 20"), ("清理数量", $"{Game.Kills}"), ("职级", $"{Game.Level}"), ("总伤害", $"{Game.RunDamage}") };
		for (int i = 0; i < 4; i++)
		{
			float x = 18 + i * 240;
			Pnl(new Rect2(x, 130, 230, 150), cardBg, won && i == 0 ? GOLD : new Color("585d66"));
			Lbl(items[i].Item1, new Rect2(x + 18, 148, 194, 28), 17, MUTED);
			Lbl(items[i].Item2, new Rect2(x + 18, 190, 194, 64), 44, GOLD);
		}
		int t = (int)Game.TotalTime;
		Lbl($"加班时长 {t / 60:00}:{t % 60:00}　·　最佳清理 {Game.BestKills}　·　最高坚持 {Game.BestWave:00} / 20　·　下班 {Game.Victories} 次", new Rect2(18, 300, 960, 30), 17, MUTED);
		StatsPanel();
		OwnedPanels(false);
		Btn("返回首页", new Rect2(996, 628, 266, 64), Game.ReturnTitle);
		Btn("再来一轮", new Rect2(996, 708, 266, 68), Game.BeginRun, true);
	}
}
