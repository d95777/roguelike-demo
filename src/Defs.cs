using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace RogueDemo;

public enum EKind { Chase, Ranged, Charge, Tank, Boss }
public enum WKind { Lance, Spear, Blade, Thunder, Bow, Magic, Baton }

public static class U
{
	public static int RoundI(double v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
	public static Vector2 Dir(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
	public static string Fmt(float v, int digits = 1) => v.ToString(digits == 1 ? "0.0" : "0.00");
	public static string Json(string path) => Godot.FileAccess.GetFileAsString(path);
}

public class HeroDef
{
	public string Name, Title, Model, Description;
	public float Hp, Speed, Damage;
	public int Weapon;
	// 角色特点
	public float XpMult = 1f, CashMult = 1f, PriceMult = 1f, Armor, Pickup = 88f, DashCooldown = 3f, DashInvuln = 0.28f, LevelDamage;
	public bool DashShockwave;
}

public class WeaponDef
{
	public string Name, Image, Description, ColorHex;
	public WKind Kind;
	public float Damage, Cooldown, Range, NativeAngleDeg;
	public int Price;
	public Color Color;
}

public class EnemyDef
{
	public string Name, Model;
	public float Hp, Speed, Damage, Height;
	public EKind Kind;
	public int Reward;
}

public class ItemDef
{
	public string Name, Icon;
	public int Price;
	// 属性变化；未出现的字段为 null
	public float? Atk, Dmg, Crit, Spd, Hp, Armor, Regen, Pickup;
}

public class Balance
{
	public int WaveCount, MaxEnemies;
	public float BaseWaveSeconds, ExtraSecondsPerWave, MaxWaveSeconds;
	public float HpQuad, BossBaseHp = 8000f, EnemyHpMult = 1f, EnemyDamageMult = 1f, EnemySpeedMult = 1f;
	public float SpawnRateBase = 1.2f, SpawnRatePerWave = 0.32f, HpGrowth = 0.17f;   // 每秒刷怪数、每波增量、怪物生命每波增长
	public float SpawnRate(int wave) => SpawnRateBase + SpawnRatePerWave * (wave - 1);
	public List<HeroDef> Heroes = new();
	public List<WeaponDef> Weapons = new();
	public List<EnemyDef> Enemies = new();
	public List<ItemDef> Items = new();

	static float F(JsonElement e, string key, float fallback = 0f) =>
		e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
	static float? FN(JsonElement e, string key) =>
		e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : null;
	static string S(JsonElement e, string key) =>
		e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

	public static Balance Load(string path)
	{
		using var doc = JsonDocument.Parse(U.Json(path));
		var r = doc.RootElement;
		var b = new Balance
		{
			WaveCount = (int)F(r, "wave_count"),
			MaxEnemies = (int)F(r, "max_enemies"),
			BaseWaveSeconds = F(r, "base_wave_seconds"),
			ExtraSecondsPerWave = F(r, "extra_seconds_per_wave"),
			MaxWaveSeconds = F(r, "max_wave_seconds"),
			SpawnRateBase = F(r, "spawn_rate_base", 1.2f),
			SpawnRatePerWave = F(r, "spawn_rate_per_wave", 0.32f),
			HpGrowth = F(r, "hp_growth", 0.17f),
			EnemyHpMult = F(r, "enemy_hp_mult", 1f),
			HpQuad = F(r, "hp_quad", 0f),
			BossBaseHp = F(r, "boss_base_hp", 8000f),
			EnemyDamageMult = F(r, "enemy_damage_mult", 1f),
			EnemySpeedMult = F(r, "enemy_speed_mult", 1f),
		};
		foreach (var h in r.GetProperty("heroes").EnumerateArray())
			b.Heroes.Add(new HeroDef { Name = S(h, "name"), Title = S(h, "title"), Model = S(h, "model"), Description = S(h, "description"), Hp = F(h, "hp"), Speed = F(h, "speed"), Damage = F(h, "damage"), Weapon = (int)F(h, "weapon"), XpMult = F(h, "xp_mult", 1f), CashMult = F(h, "cash_mult", 1f), PriceMult = F(h, "price_mult", 1f), Armor = F(h, "armor"), Pickup = F(h, "pickup", 88f), DashCooldown = F(h, "dash_cooldown", 3f), DashInvuln = F(h, "dash_invuln", 0.28f), LevelDamage = F(h, "level_damage"), DashShockwave = h.TryGetProperty("dash_shockwave", out var ds) && ds.ValueKind == JsonValueKind.True });
		foreach (var w in r.GetProperty("weapons").EnumerateArray())
		{
			var kind = S(w, "kind") switch { "lance" => WKind.Lance, "spear" => WKind.Spear, "blade" => WKind.Blade, "baton" => WKind.Baton, "thunder" => WKind.Thunder, "bow" => WKind.Bow, _ => WKind.Magic };
			b.Weapons.Add(new WeaponDef { Name = S(w, "name"), Kind = kind, Image = S(w, "image"), Description = S(w, "description"), ColorHex = S(w, "color"), Color = new Color(S(w, "color")), Damage = F(w, "damage"), Cooldown = F(w, "cooldown"), Range = F(w, "range"), Price = (int)F(w, "price"), NativeAngleDeg = F(w, "native_angle_deg") });
		}
		foreach (var e in r.GetProperty("enemies").EnumerateArray())
		{
			var kind = S(e, "kind") switch { "ranged" => EKind.Ranged, "charge" => EKind.Charge, "tank" => EKind.Tank, "boss" => EKind.Boss, _ => EKind.Chase };
			b.Enemies.Add(new EnemyDef { Name = S(e, "name"), Model = S(e, "model"), Hp = F(e, "hp"), Speed = F(e, "speed"), Damage = F(e, "damage"), Height = F(e, "height"), Kind = kind, Reward = (int)F(e, "reward") });
		}
		foreach (var i in r.GetProperty("items").EnumerateArray())
			b.Items.Add(new ItemDef { Name = S(i, "name"), Icon = S(i, "icon"), Price = (int)F(i, "price"), Atk = FN(i, "atk"), Dmg = FN(i, "dmg"), Crit = FN(i, "crit"), Spd = FN(i, "spd"), Hp = FN(i, "hp"), Armor = FN(i, "armor"), Regen = FN(i, "regen"), Pickup = FN(i, "pickup") });
		return b;
	}
}

// ---- 运行时数据 ----

public class Weapon
{
	public int Type, Tier;
	public float Cd, Angle, Flash, AimDistance = float.PositiveInfinity;
	public float LanceAngle;           // 铅笔发射时锁定的方向
	public float LanceDamage;
	public bool LanceCrit;
	public readonly List<int> LanceHit = new();   // 本次发射已命中的敌人,每个敌人只受一次伤害
}

public class Enemy
{
	public int Id, Type;
	public EnemyDef Def;
	public Actor Node;
	public Vector2 Pos;                // 逻辑位置，每帧末尾统一写回节点
	public float Hp, MaxHp, Speed, Damage, Radius, Cooldown, Windup, Dash, Hit, Puff;
	public Vector2 Dir;
	public EKind Kind;
	public bool Flashing;
	public float Stun, Slow, SlowMul = 0.5f;   // 麻痹剩余时间、减速剩余时间与速度倍率
	public int TintState;
	public int Pattern, PatternIdx;     // 首领当前招式与已放招次数
	public bool Elite;                  // 精英:体型更大、生命更高、有专属技能
	public float AbilityTimer, AuraTimer;
	public int Chain;                   // 精英冲锋怪剩余的连冲次数
}

public enum PSprite { None, Paper, Note, Ink, Orb }

public class Projectile
{
	public Vector2 P, V;
	public float Life, Damage, R, Size, Spin, Splash = 85f;
	public bool Enemy, Critical;
	public Color Color;
	public List<int> Hit = new();
	public int Pierce;
	public WKind Kind;                // 玩家弹道的武器种类；敌方弹道用 Lance 占位，靠 Enemy 区分
	public PSprite Sprite;
	public Texture2D Tex, Icon;
	public Vector2 RectSize;
}

public class Drop
{
	public Vector2 P;
	public int Value, Xp;
	public bool Heart, Collected;
}

public class Warning
{
	public int Type;
	public bool Elite;
	public Vector2 P;
	public float Time;
}

public class Offer
{
	public bool IsItem;
	public int Item, Type, Tier = 1, Price;
	public bool Sold;
	public Offer Clone() => (Offer)MemberwiseClone();
}

public class Upgrade
{
	public string Name, Desc, Stat, Glyph;
	public float Value;
	public int Tier, Cash;
}

public class UpgradeTemplate
{
	public string Name, Stat, Glyph;
	public float Base;
}

public enum FxKind { Sprite, Ring, Line, Slash, Scratch, Fling, Cut, Marker, Glyph, Puff, Scrap, Text, Confetti, Lance, Bolt }

public class Fx
{
	public FxKind Kind;
	public Vector2 P, End, V;
	public float R, Life, Max, Angle, W, Rot, Spin, Size;
	public int Count, N;
	public Color Color = Colors.White;
	public string Text, Name;
	public bool Full;
}

public class ItemLine
{
	public string Text;
	public bool Good;
}
