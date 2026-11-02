using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace RogueDemo;

// 只负责展示。所有碰撞与战斗数据由主场景管理。
// 一个角色只有一个节点:精灵本身。位置、朝向翻转、挤压摇摆合成一个变换,一帧只写一次;
// 程序动画由主场景统一调用 Tick,不再逐节点走 _Process。
public partial class Actor : Sprite2D
{
	public float BaseScale = 1f;
	public float Facing = 1f;
	public float NativeFacing = -1f;
	public float OutlineRadius = 14f;
	public string Model = "";
	public bool Moving;
	float clock;
	public float Flash;
	public static int AnimStride = 1;          // 1=每帧更新;2=隔帧更新(人多时由主场景设置)
	readonly int animPhase = (int)(GD.Randi() % 4);
	public bool Smooth;                        // 玩家自己始终每帧更新动画
	bool flashShown;
	// 步态参数:移动节奏、挤压幅度、弹跳高度(像素)、左右摇摆(弧度)
	float paceMove = 13f, squashMove = 0.07f, hop = 4f, sway = 0.09f;
	public float Lean;
	// 状态:蓄力前摇、冲刺拉伸、死亡动画
	public bool Alert, Lunge, Dying;
	float deathT, deathLen = 0.22f;

	// 逻辑位置(脚底),由主场景每帧写入
	public Vector2 Pos;
	// 受击闪白等着色与整体透明度;最终颜色在 Tick 里合成
	public Color Tint = Colors.White;
	public float Alpha = 1f;
	Vector2 curSize = Vector2.One;
	float curTilt;
	Vector2 curOffset;
	Color shownColor = Colors.White;
	float deathAlpha = 1f;
	bool deathFlash;

	// 朝向翻转系数:面朝方向乘以素材原始朝向
	public float FlipX => Facing * NativeFacing;

	static Dictionary<string, float> nativeFacings;
	public static float NativeFacingOf(string id)
	{
		if (nativeFacings == null)
		{
			nativeFacings = new();
			using var doc = JsonDocument.Parse(U.Json("res://data/animations.json"));
			foreach (var p in doc.RootElement.EnumerateObject())
				nativeFacings[p.Name] = p.Value.TryGetProperty("native_facing", out var v) ? v.GetSingle() : 1f;
		}
		return nativeFacings.TryGetValue(id, out var f) ? f : 1f;
	}

	public void Setup(string id, float height)
	{
		Model = id;
		NativeFacing = NativeFacingOf(id);
		Texture = GD.Load<Texture2D>($"res://assets/actors/{id}.png");
		Centered = false;
		BaseScale = height / Texture.GetHeight();
		// 节点原点落在脚底,缩放与摇摆都以脚为支点
		Offset = new Vector2(-Texture.GetWidth() * 0.5f, -Texture.GetHeight());
		OutlineRadius = height * 0.20f;
		curSize = Vector2.One * BaseScale;
		SetProcess(false);
		Apply();
	}

	public void Place(Vector2 p)
	{
		Pos = p;
		Apply();
	}

	// 合成变换:先按朝向水平翻转,再叠加摇摆旋转和挤压缩放
	void Apply()
	{
		float f = FlipX;
		float c = Mathf.Cos(curTilt), s = Mathf.Sin(curTilt);
		var xAxis = new Vector2(f * c * curSize.X, s * curSize.X);
		var yAxis = new Vector2(-f * s * curSize.Y, c * curSize.Y);
		Transform = new Transform2D(xAxis, yAxis, Pos + new Vector2(f * curOffset.X, curOffset.Y));
		var color = new Color(Tint.R * (flashShown ? 2.2f : 1f), Tint.G * (flashShown ? 2.2f : 1f), Tint.B * (flashShown ? 2.2f : 1f), Tint.A * Alpha * deathAlpha);
		if (deathFlash) color = new Color(2.2f, 2.2f, 2.2f, color.A);
		if (color != shownColor)
		{
			shownColor = color;
			Modulate = color;
		}
	}

	// 返回 false 表示角色已经播完死亡动画并被释放
	public bool Tick(float delta)
	{
		clock += delta;
		if (Dying)
		{
			deathT += delta;
			float k = Mathf.Min(deathT / deathLen, 1f);
			curSize = new Vector2(1f + 0.55f * k, Mathf.Max(1f - 1.05f * k, 0.04f)) * BaseScale;
			curTilt = k * 1.1f;
			curOffset = Vector2.Zero;
			deathFlash = k < 0.25f;
			deathAlpha = 1f - k * k;
			Apply();
			if (k >= 1f)
			{
				QueueFree();
				return false;
			}
			return true;
		}
		Flash = Mathf.Max(Flash - delta, 0f);
		if (!(AnimStride > 1 && !Smooth && (Engine.GetProcessFrames() + (ulong)animPhase) % (ulong)AnimStride != 0))
		{
			float pace = Moving ? paceMove : 3.2f;
			float wave = Mathf.Sin(clock * pace);
			float squash = Moving ? squashMove : 0.025f;
			var size = new Vector2(1f - wave * squash * 0.6f, 1f + wave * squash);
			float tilt = Mathf.Sin(clock * pace * 0.5f) * (Moving ? sway : 0f) + (Moving ? Lean : 0f);
			var offset = new Vector2(0f, -Mathf.Abs(wave) * (Moving ? hop : 0f));
			if (Alert)
			{
				// 蓄力:身体鼓起并高频颤抖,提示即将攻击
				size *= 1.10f + 0.05f * Mathf.Sin(clock * 40f);
				offset.X = Mathf.Sin(clock * 75f) * 2.5f;
			}
			else if (Lunge)
			{
				size = new Vector2(1.32f, 0.78f);
				tilt = 0f;
			}
			curSize = size * BaseScale;
			curTilt = tilt;
			curOffset = offset;
			flashShown = Flash > 0f;
		}
		Apply();
		return true;
	}

	// 按怪物类型设置步态:追击小步快跳,重甲沉重慢踩,远程轻飘,首领缓慢
	public void SetGait(EKind kind, float speed)
	{
		switch (kind)
		{
			case EKind.Chase: paceMove = Mathf.Clamp(6f + speed * 0.11f, 10f, 19f); squashMove = 0.08f; hop = 4f; sway = 0.12f; break;
			case EKind.Ranged: paceMove = 9f; squashMove = 0.04f; hop = 6f; sway = 0.04f; break;
			case EKind.Charge: paceMove = 12f; squashMove = 0.07f; hop = 4f; sway = 0.10f; break;
			case EKind.Tank: paceMove = 7f; squashMove = 0.11f; hop = 3f; sway = 0.05f; break;
			case EKind.Boss: paceMove = 5.5f; squashMove = 0.07f; hop = 5f; sway = 0.03f; break;
		}
	}

	public void Die(bool longer = false)
	{
		Dying = true;
		deathLen = longer ? 0.6f : 0.22f;
	}

	public void HitFlash() => Flash = 0.09f;

	public void Locomotion(bool isMoving) => Moving = isMoving;

	public void Face(float x)
	{
		if (Mathf.Abs(x) > 0.01f) Facing = x > 0f ? 1f : -1f;
	}
}
