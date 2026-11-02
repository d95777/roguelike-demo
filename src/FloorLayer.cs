using Godot;

namespace RogueDemo;

// 地板层:画在所有角色之下,内容静态,只绘制一次。
// 主节点自己绘制的特效、武器、弹道因此能盖在角色上面。
public partial class FloorLayer : Node2D
{
	public Main Game;

	public override void _Draw()
	{
		Game?.DrawFloor(this);
	}
}

// 阴影层:所有角色的落地阴影在这里统一绘制,同一张贴图连续绘制会被合并成一次绘制调用。
public partial class ShadowLayer : Node2D
{
	public Main Game;
	Texture2D disc;
	static readonly Color Tint = new Color(0, 0, 0, 0.25f);

	public override void _Ready()
	{
		var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
		for (int y = 0; y < 64; y++)
		{
			for (int x = 0; x < 64; x++)
			{
				float d = new Vector2(x - 31.5f, y - 31.5f).Length();
				img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(32f - d, 0f, 1f)));
			}
		}
		disc = ImageTexture.CreateFromImage(img);
	}

	void Shadow(Vector2 p, float r) => DrawTextureRect(disc, new Rect2(p.X - r, p.Y + 2f - r * 0.35f, r * 2f, r * 0.7f), false, Tint);

	public override void _Draw()
	{
		if (Game == null) return;
		if (Game.Player != null && IsInstanceValid(Game.Player)) Shadow(Game.PlayerPos, Game.Player.OutlineRadius);
		foreach (var e in Game.Enemies)
			if (e.Hp > 0f) Shadow(e.Pos, e.Node.OutlineRadius);
	}
}

// 武器后层:轨道在人物身后(上半圈)的武器画在角色下面,避免盖住人物。
public partial class WeaponBackLayer : Node2D
{
	public Main Game;

	public override void _Draw()
	{
		if (Game?.Player != null && IsInstanceValid(Game.Player)) Game.DrawWeapons(this, true);
	}
}
