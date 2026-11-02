using System.Collections.Generic;
using Godot;

namespace RogueDemo;

// 逻辑回归测试(替代原先的 GDScript 测试)。用法:
//   godot --headless --path . -- --selftest
// 失败时进程退出码为 1。
public partial class SelfTest : Node
{
	public Main Game;
	int passed;
	readonly List<string> failures = new();

	void Check(bool ok, string message)
	{
		if (ok) passed++;
		else
		{
			failures.Add(message);
			GD.PrintErr("失败:" + message);
		}
	}

	static bool Near(float a, float b, float eps = 0.001f) => Mathf.Abs(a - b) <= eps;

	// 清场:一个满血玩家,无敌人,波次时间足够长
	void Fresh(int hero = 0)
	{
		Game.HeroIndex = hero;
		Game.StartRun();
		Game.Invulnerable = 999f;
		Game.SpawnTimer = 999f;
		Game.WaveTime = 9999f;
	}

	Enemy Dummy(int type, Vector2 p, float hp = 1e6f)
	{
		var e = Game.SpawnEnemy(type, p);
		e.Hp = hp;
		e.MaxHp = hp;
		e.Cooldown = 1e9f;
		return e;
	}

	public override async void _Ready()
	{
		Game.SaveEnabled = false;
		Game.Rng.Seed = 73021;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Game.SetProcess(false);
		RunAll();
		GD.Print($"通过 {passed} 项,失败 {failures.Count} 项");
		foreach (var f in failures) GD.Print("  失败:" + f);
		GetTree().Quit(failures.Count == 0 ? 0 : 1);
	}

	void RunAll()
	{
		var b = Game.Balance;
		Check(Game.Phase == "title", "启动进入首页");
		Check(b.Heroes.Count == 3 && b.Weapons.Count == 7 && b.Enemies.Count == 8, "数据表数量:3 角色 / 7 武器 / 8 怪物");

		// --- 开局 ---
		for (int i = 0; i < 3; i++)
		{
			Game.HeroIndex = i;
			Game.StartRun();
			Check(Game.Weapons[0].Type == b.Heroes[i].Weapon, $"角色 {i} 初始武器");
			Check(Game.Hp == Game.MaxHp, $"角色 {i} 满血开局");
			Check(Game.Player != null && Game.Player.Texture != null, $"角色 {i} 形象加载");
		}
		Fresh();
		Game.Wave = 5;      // 首领的生命公式从第 5 波起才为正
		for (int i = 0; i < 8; i++)
		{
			var e = Game.SpawnEnemy(i, new Vector2(160 + i * 100, 260));
			Check(e.Node.Texture != null && e.Hp > 0f, $"怪物 {i} 形象加载");
		}
		Check(Near(Game.SpawnEnemy(7, new Vector2(300, 300)).MaxHp, b.BossBaseHp), "首领一波时按基础生命");

		// --- 角色特点 ---
		Fresh(0);
		float dmg0 = Game.DamageMult;
		Game.CollectDrop(new Drop { Value = 0, Xp = 10 });
		Check(Game.Level == 2 && Near(Game.DamageMult, dmg0 + 0.03f), "实习生每升一级伤害 +3%");
		Fresh(0);
		Game.CollectDrop(new Drop { Value = 0, Xp = 8 });
		Check(Game.Level == 2, "实习生经验 +30%:8 点经验就能升级(需 10)");
		Fresh(1);
		Check(Game.PickupRange == 128f && Game.PriceMult < 1f, "会计拾取范围与商店折扣");
		Game.CollectDrop(new Drop { Value = 10, Xp = 0 });
		Game.CollectDrop(new Drop { Value = 10, Xp = 0 });
		Check(Game.Cash == 27, "会计奖金 +35%(两个 10 奖金共 27,小数累积)");
		Fresh(2);
		Check(Game.Armor == 4f, "保安自带护甲 4");
		// 警棍电击:麻痹停住不动、之后减速;首领不会被麻痹
		Fresh(2);
		Game.Weapons = new List<Weapon> { Game.MakeWeapon(6, 1) };
		Game.Weapons[0].Cd = 0f;
		var shocked = Dummy(0, Game.PlayerPos + new Vector2(80, 0));
		shocked.Hp = 1e6f;
		Game.UpdateWeapons(0.01f);
		Check(shocked.Stun > 0f && shocked.Slow > shocked.Stun, "警棍命中使敌人麻痹并随后减速");
		float stillX = shocked.Pos.X;
		Game.UpdateEnemies(0.1f);
		Check(Near(shocked.Pos.X, stillX), "麻痹期间敌人原地不动");
		shocked.Stun = 0f;
		float slowFrom = shocked.Pos.X;
		Game.UpdateEnemies(0.1f);
		float slowMoved = Mathf.Abs(shocked.Pos.X - slowFrom);
		Check(slowMoved > 0f && slowMoved < shocked.Speed * 0.1f * 0.6f, "麻痹结束后减速为约一半速度");
		Fresh(2);
		Game.Wave = 5;
		Game.Weapons = new List<Weapon> { Game.MakeWeapon(6, 1) };
		Game.Weapons[0].Cd = 0f;
		var big = Dummy(7, Game.PlayerPos + new Vector2(80, 0));
		big.Hp = big.MaxHp = 1e6f;
		Game.UpdateWeapons(0.01f);
		Check(big.Stun == 0f && big.Slow > 0f && big.SlowMul > 0.5f, "首领不被麻痹,只受较轻减速");
		var shove = Dummy(0, Game.PlayerPos + new Vector2(80, 0));
		float gap = shove.Pos.DistanceTo(Game.PlayerPos);
		Game.DashCooldown = 0f;
		Game._UnhandledKeyInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
		Check(Near(Game.DashCooldown, 1.8f) && Game.Invulnerable >= 0.45f, "保安闪避冷却 1.8 秒、无敌更久");
		Check(shove.Pos.DistanceTo(Game.PlayerPos) > gap + 20f, "保安闪避撞开周围敌人");
		Fresh(0);

		// --- 战场边界与受伤 ---
		Fresh();
		Game.PlayerPos = new Vector2(5000, 5000);
		Game.Simulate(0.01f);
		Check(Main.FIELD.HasPoint(Game.PlayerPos), "人物不能越过战场边界");
		Game.Invulnerable = 0f;
		float hp = Game.Hp;
		Game.HurtPlayer(10f);
		Check(Game.Hp < hp, "碰撞伤害扣血");
		hp = Game.Hp;
		Game.HurtPlayer(10f);
		Check(Game.Hp == hp, "受击无敌保护");
		Game.Armor = 10f;
		Game.Invulnerable = 0f;
		hp = Game.Hp;
		Game.HurtPlayer(20f);
		Check(Near(hp - Game.Hp, 20f * 100f / 160f), "护甲按公式减伤");

		// --- 闪避 ---
		Fresh();
		Game.DashCooldown = 0f;
		Game._UnhandledKeyInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
		Check(Game.DashTimer > 0f && Game.DashCooldown == 3f, "闪避启动及冷却");

		// --- 拾取、升级与商店 ---
		Fresh();
		Game.CollectDrop(new Drop { Value = 20, Xp = 100 });
		Check(Game.Cash == 20 && Game.PendingUpgrades > 0, "拾取与连续升级累计");
		Game.FinishWave();
		Check(Game.Phase == "clear", "波次结束先显示本波完成提示");
		Game.SkipClear();
		Check(Game.Phase == "upgrade", "提示结束后进入升级");
		Check(Game.Upgrades.Count == 4, "升级给出 4 个选项");
		var stats = new HashSet<string>();
		foreach (var u in Game.Upgrades) stats.Add(u.Stat);
		Check(stats.Count == 4, "4 个选项互不重复");
		int guard = 0;
		while (Game.Phase == "upgrade" && guard++ < 50) Game.ChooseUpgrade(0);
		Check(Game.Phase == "shop", "升级后进入商店");

		Game.Cash = 500;
		Game.Weapons = new List<Weapon> { Game.MakeWeapon(0) };
		Game.Offers = new List<Offer>
		{
			new() { Type = 0, Tier = 1, Price = 25 }, new() { Type = 2, Tier = 1, Price = 25 },
			new() { Type = 3, Tier = 1, Price = 25 }, new() { Type = 5, Tier = 1, Price = 25 },
		};
		Check(Game.Purchase(0), "购买成功");
		Check(Game.Weapons.Count == 1 && Game.Weapons[0].Tier == 2, "同名同阶武器自动合成");
		int money = Game.Cash;
		Check(!Game.Purchase(0) && Game.Cash == money, "已售商品不能重复购买");
		Game.Cash = 0;
		Check(!Game.Purchase(1), "余额不足不能购买");
		Game.Cash = 500;
		Game.Weapons = new List<Weapon>();
		for (int i = 0; i < 6; i++) Game.Weapons.Add(Game.MakeWeapon(i, 4));
		Check(!Game.Purchase(1) && Game.Weapons.Count == 6, "六器上限生效");
		Game.Weapons = new List<Weapon> { Game.MakeWeapon(0), Game.MakeWeapon(1) };
		Game.SellWeapon(1);
		Check(Game.Weapons.Count == 1, "出售武器");
		Game.SellWeapon(0);
		Check(Game.Weapons.Count == 1, "最后一件武器不能出售");

		// 刷新与锁定
		Game.Cash = 100;
		Game.RefreshCost = 5;
		Game.GenerateOffers();
		Game.ToggleLock(2);
		var locked = Game.Offers[2];
		Game.Reroll();
		Check(ReferenceEquals(Game.Offers[2].GetType(), typeof(Offer)) && Game.Offers[2].Item == locked.Item && Game.Offers[2].Type == locked.Type && Game.Offers[2].Price == locked.Price, "锁定的商品刷新后保留");
		Check(Game.Cash == 95 && Game.RefreshCost == 8, "刷新扣费并涨价");

		// --- 升级倍率 ---
		var tmpl = new UpgradeTemplate { Name = "测试", Stat = "damage", Base = 0.12f, Glyph = "攻" };
		float[] mult = { 1f, 1.6f, 2.3f, 3.2f };
		for (int t = 1; t <= 4; t++) Check(Near(Game.MakeUpgrade(tmpl, t).Value, 0.12f * mult[t - 1]), $"升级稀有度 {t} 倍率");

		// --- 道具 ---
		Fresh();
		for (int i = 0; i < b.Items.Count; i++)
		{
			float dmg = Game.DamageMult, atk = Game.AttackSpeed, spd = Game.Speed, mhp = Game.MaxHp, arm = Game.Armor, reg = Game.Regeneration, pick = Game.PickupRange;
			var it = b.Items[i];
			Game.ApplyItem(i);
			Check(Game.Items[i] == 1, $"道具 {it.Name} 计入持有");
			if (it.Dmg != null) Check(Near(Game.DamageMult, dmg + it.Dmg.Value), $"道具 {it.Name} 伤害");
			if (it.Atk != null) Check(Game.AttackSpeed >= 0.3f, $"道具 {it.Name} 攻速不低于下限");
			if (it.Spd != null) Check(Near(Game.Speed, spd * (1f + it.Spd.Value), 0.01f), $"道具 {it.Name} 移速");
			if (it.Hp != null) Check(Near(Game.MaxHp, mhp + it.Hp.Value), $"道具 {it.Name} 生命");
			if (it.Armor != null) Check(Near(Game.Armor, arm + it.Armor.Value), $"道具 {it.Name} 护甲");
			if (it.Regen != null) Check(Near(Game.Regeneration, reg + it.Regen.Value), $"道具 {it.Name} 再生");
			if (it.Pickup != null) Check(Near(Game.PickupRange, pick + it.Pickup.Value), $"道具 {it.Name} 拾取");
			Check(Game.ItemLines(it).Count > 0, $"道具 {it.Name} 有属性描述");
		}
		Game.Crit = 0.8f;
		for (int i = 0; i < b.Items.Count; i++)
			if (b.Items[i].Crit != null) Game.ApplyItem(i);
		Check(Game.Crit <= 0.85f, "暴击率上限 85%");

		// --- 武器:各类型都能开火并造成伤害 ---
		for (int type = 0; type < 6; type++)
		{
			Fresh();
			Game.Weapons = new List<Weapon> { Game.MakeWeapon(type, 1) };
			Game.Weapons[0].Cd = 0f;
			var target = Dummy(0, Game.PlayerPos + new Vector2(60, 0), 1e6f);
			float before = target.Hp;
			for (int fr = 0; fr < 40; fr++) Game.UpdateWeapons(0.016f);      // 铅笔要飞出去才造成伤害
			Game.UpdateProjectiles(0.0f);
			bool fired = target.Hp < before || Game.Projectiles.Count > 0;
			Check(fired, $"武器 {b.Weapons[type].Name} 能开火");
			Check(Game.Weapons[0].Cd > 0f, $"武器 {b.Weapons[type].Name} 进入冷却");
		}
		// 射程外不开火
		Fresh();
		Game.Weapons = new List<Weapon> { Game.MakeWeapon(0) };
		Game.Weapons[0].Cd = 0f;
		var far = Dummy(0, Game.PlayerPos + new Vector2(2000, 0));
		float farHp = far.Hp;
		Game.UpdateWeapons(0.016f);
		Check(far.Hp == farHp, "射程外不攻击");
		// 订书机阶数与弹数
		Fresh();
		Game.Weapons = new List<Weapon> { Game.MakeWeapon(2, 4) };
		Game.Weapons[0].Cd = 0f;
		Dummy(0, Game.PlayerPos + new Vector2(120, 0));
		Game.UpdateWeapons(0.016f);
		int stapler = 0;
		foreach (var p in Game.Projectiles)
			if (!p.Enemy) stapler++;
		Check(b.Weapons[2].Kind != WKind.Bow || stapler == 4, "四阶订书机一次射 4 颗");
		// 近战伤害随阶数提升
		float dmgT1 = 0f, dmgT3 = 0f;
		foreach (int tier in new[] { 1, 3 })
		{
			Fresh();
			Game.Crit = 0f;
			Game.Weapons = new List<Weapon> { Game.MakeWeapon(0, tier) };
			Game.Weapons[0].Cd = 0f;
			var t2 = Dummy(0, Game.PlayerPos + new Vector2(50, 0));
			for (int fr = 0; fr < 40; fr++) Game.UpdateWeapons(0.016f);
			if (tier == 1) dmgT1 = t2.MaxHp - t2.Hp; else dmgT3 = t2.MaxHp - t2.Hp;
		}
		Check(dmgT3 > dmgT1 && dmgT1 > 0f, "武器阶数提升伤害");

		// --- 击杀、掉落与上限 ---
		Fresh();
		var victim = Dummy(0, Game.PlayerPos + new Vector2(300, 0), 5f);
		Game.HitEnemy(victim, 100f);
		Check(victim.Hp <= 0f && Game.Kills == 1, "击杀计数");
		Check(Game.Drops.Count >= 1, "击杀产生掉落");
		Game.Drops.Clear();
		int cash0 = Game.Cash;
		for (int i = 0; i < Main.DROP_CAP; i++) Game.Drops.Add(new Drop { Value = 1, Xp = 0 });
		var v2 = Dummy(0, Game.PlayerPos + new Vector2(320, 0), 5f);
		Game.HitEnemy(v2, 100f);
		Check(Game.Drops.Count <= Main.DROP_CAP + 1 && Game.Cash > cash0, "掉落上限:超出直接结算");
		Check(Game.Effects.Count <= Main.FX_TEXT_CAP + 20, "特效数量受上限约束");

		// --- 首领 ---
		Fresh();
		Game.Wave = 5;
		var boss = Dummy(7, Game.PlayerPos + new Vector2(300, 0), 10f);
		Game.HitEnemy(boss, 1000f);
		Check(Game.BossDefeated, "击败首领标记");

		// --- 敌人移动与分离 ---
		Fresh();
		var chaser = Game.SpawnEnemy(0, Game.PlayerPos + new Vector2(400, 0));
		chaser.Cooldown = 1e9f;
		float d0 = chaser.Pos.DistanceTo(Game.PlayerPos);
		Game.UpdateEnemies(0.5f);
		Check(chaser.Pos.DistanceTo(Game.PlayerPos) < d0, "追击型怪物向玩家移动");
		Fresh();
		var s1 = Dummy(0, new Vector2(1000, 800));
		var s2 = Dummy(0, new Vector2(1002, 800));
		float gap0 = s1.Pos.DistanceTo(s2.Pos);
		for (int i = 0; i < 4; i++) Game.SeparateEnemies();
		Check(s1.Pos.DistanceTo(s2.Pos) > gap0, "重叠的怪物被推开");

		// --- 敌人弹幕伤害玩家 ---
		Fresh();
		Game.Invulnerable = 0f;
		hp = Game.Hp;
		Game.Projectiles.Add(new Projectile { P = Game.PlayerPos + new Vector2(0, -20), V = Vector2.Zero, Life = 5f, Damage = 12f, Enemy = true, R = 7f, Sprite = PSprite.Paper, Size = 66f });
		Game.UpdateProjectiles(0.016f);
		Check(Game.Hp < hp, "敌方弹幕命中扣血");

		// --- 暂停与恢复 ---
		Fresh();
		Game.PreviousPhase = "running";
		Game.SetPhase("paused");
		Game.Resume();
		Check(Game.Phase == "running", "暂停后恢复回到战斗");

		// --- 结算 ---
		Fresh();
		Game.Wave = b.WaveCount;
		Game.Elapsed = 1e6f;
		Game.FinishWave();
		Check(Game.Phase == "victory", "最后一波结束先进入通关演出");
		Game.Simulate(0.0f);
		for (int i = 0; i < 400 && Game.Phase == "victory"; i++) Game.AdvanceVictory(0.016f);
		Check(Game.Phase == "win", "通关演出结束后进入结算");
		Check(Game.Victories >= 1, "通关次数累计");
		Fresh();
		Game.Hp = 1f;
		Game.Invulnerable = 0f;
		Game.HurtPlayer(500f);
		Check(Game.Phase == "gameover", "生命归零进入失败");

		// --- 朝向 ---
		Fresh();
		var f = Game.SpawnEnemy(0, new Vector2(500, 500));
		f.Node.Face(1f);
		float right = f.Node.FlipX;
		f.Node.Face(-1f);
		Check(f.Node.FlipX == -right, "朝向翻转");
		f.Node.Face(0f);
		Check(f.Node.FlipX == -right, "无方向输入时保持朝向");

		// --- 音效 ---
		Check(Game.Media.Bank.Count >= 10, "音效库已载入");
		Game.Media.Muted = true;
		Check(!Game.Media.Play("hit"), "静音时不播放");
		Game.Media.Muted = false;

		// --- 商店:背包合成、价格随阶数上涨、高价道具与警棍进入商店 ---
		Fresh();
		Game.Weapons = new List<Weapon> { Game.MakeWeapon(0, 1), Game.MakeWeapon(0, 1), Game.MakeWeapon(1, 1) };
		Game.SetPhase("shop");
		Check(Game.CanMerge(0) && !Game.CanMerge(2), "背包内同名同阶武器可合成");
		Game.MergeWeapons(0);
		Check(Game.Weapons.Count == 2 && Game.Weapons[0].Tier == 2, "合成后少一把且升一阶");
		Check(Game.WeaponSellPrice(Game.Weapons[0]) > Game.WeaponSellPrice(Game.MakeWeapon(0, 1)) * 2 - 1, "高阶武器出售价更高");
		Game.Wave = 1;
		Game.Cash = 500;
		int cost = Game.UpgradeCost(1);
		Check(cost > 0 && Game.UpgradeWeapon(1) && Game.Weapons[1].Tier == 2 && Game.Cash == 500 - cost, "花钱可以直接升阶");
		Game.Wave = 20;
		Check(Game.UpgradeCost(1) > cost * 3, "升阶价格随波次上涨");
		Game.Wave = 14;
		bool sawBaton = false, sawPremium = false, sawTier4 = false, tierPriceUp = true;
		for (int n = 0; n < 400; n++)
		{
			Game.GenerateOffers();
			foreach (var o in Game.Offers)
			{
				if (o.IsItem) sawPremium |= Game.Balance.Items[o.Item].Price >= 80;
				else
				{
					sawBaton |= o.Type == 6;
					sawTier4 |= o.Tier == 4;
					if (o.Tier > 1 && o.Price < (int)(Game.Balance.Weapons[o.Type].Price * Main.TierPriceMult[o.Tier - 1])) tierPriceUp = false;
				}
			}
		}
		Check(sawBaton, "警棍会出现在商店");
		Check(sawPremium, "高价道具会出现在商店");
		Check(sawTier4, "后期商店出现四阶武器");
		Check(tierPriceUp, "高阶武器价格随阶数上涨");

		// --- 长跑稳定性:300 敌人推进 600 帧不报错 ---
		Fresh();
		Game.Weapons = new List<Weapon>();
		for (int i = 0; i < 6; i++) Game.Weapons.Add(Game.MakeWeapon(i, 4));
		for (int i = 0; i < 300; i++) Game.SpawnEnemy(i % 7, Game.PlayerPos + U.Dir(i) * (200f + i));
		for (int i = 0; i < 600; i++)
		{
			Game.Invulnerable = 999f;
			Game.Simulate(0.016f);
			Game.TickActors(0.016f);
			if (Game.Phase != "running") break;
		}
		Check(Game.Phase == "running" || Game.Phase == "shop" || Game.Phase == "upgrade", "高压场景推进 600 帧不崩溃");
		Check(Game.Effects.Count <= Main.FX_TEXT_CAP + 400, "高压场景特效数量有界");
	}
}
