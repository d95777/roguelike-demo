using System.Collections.Generic;
using Godot;

namespace RogueDemo;

// 弹幕很多时(>80)改用 MultiMesh 批量绘制:每种贴图一次绘制调用,不再逐颗弹幕提交命令。
// 弹幕少时仍走 _Draw 的完整细节(拖尾、落地阴影)。
public partial class Main
{
	public const int BatchThreshold = 80;
	static readonly string[] BatchTex = { "proj_paper", "proj_note", "proj_ink", "proj_orb" };
	MultiMesh glowMesh;
	readonly MultiMesh[] spriteMesh = new MultiMesh[4];
	float[] glowBuf = new float[0];
	readonly float[][] spriteBuf = { new float[0], new float[0], new float[0], new float[0] };
	readonly int[] spriteCount = new int[4];

	MultiMesh MakeBatch(Texture2D texture, bool colors, int z)
	{
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseColors = colors, Mesh = new QuadMesh { Size = Vector2.One } };
		AddChild(new MultiMeshInstance2D { Multimesh = mm, Texture = texture, ZIndex = z });
		return mm;
	}

	void BuildBulletBatches()
	{
		// 与 Actors 同层(z=-1)、加在其后,保证盖在角色上、在主节点绘制的特效和武器之下
		glowMesh = MakeBatch(glowTex, true, -1);
		for (int i = 0; i < 4; i++)
			spriteMesh[i] = MakeBatch(Textures.GetValueOrDefault(BatchTex[i]), false, -1);
	}

	static void Grow(MultiMesh mm, ref float[] buf, int need, int stride)
	{
		if (mm.InstanceCount >= need) return;
		int cap = System.Math.Max(256, mm.InstanceCount);
		while (cap < need) cap *= 2;
		mm.InstanceCount = cap;
		buf = new float[cap * stride];
	}

	static void Put(float[] buf, int i, int stride, Vector2 pos, float rot, Vector2 size)
	{
		float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
		int o = i * stride;
		buf[o] = c * size.X; buf[o + 1] = -s * size.Y; buf[o + 2] = 0f; buf[o + 3] = pos.X;
		buf[o + 4] = s * size.X; buf[o + 5] = c * size.Y; buf[o + 6] = 0f; buf[o + 7] = pos.Y;
	}

	public void UpdateBulletBatches()
	{
		if (glowMesh == null) return;
		int total = Projectiles.Count;
		if (total <= BatchThreshold)
		{
			glowMesh.VisibleInstanceCount = 0;
			foreach (var mm in spriteMesh) mm.VisibleInstanceCount = 0;
			return;
		}
		System.Array.Clear(spriteCount, 0, 4);
		int glowN = 0;
		foreach (var b in Projectiles)
		{
			if (!b.Enemy || b.Sprite == PSprite.None) continue;
			spriteCount[(int)b.Sprite - 1]++;
			glowN++;
		}
		Grow(glowMesh, ref glowBuf, glowN, 12);
		for (int k = 0; k < 4; k++) Grow(spriteMesh[k], ref spriteBuf[k], spriteCount[k], 8);
		System.Array.Clear(spriteCount, 0, 4);
		int g = 0;
		foreach (var b in Projectiles)
		{
			if (!b.Enemy || b.Sprite == PSprite.None) continue;
			int k = (int)b.Sprite - 1;
			var tex = Textures[BatchTex[k]];
			if (b.Tex == null)
			{
				b.Tex = tex;
				var dims = tex.GetSize();
				b.RectSize = dims * (b.Size / Mathf.Max(dims.X, dims.Y));
			}
			float spin = b.Spin + (4f - b.Life) * 7f;
			Put(spriteBuf[k], spriteCount[k]++, 8, b.P, spin, b.RectSize);
			float glowD = b.Size * 1.6f;
			Put(glowBuf, g, 12, b.P, 0f, new Vector2(glowD, glowD));
			// 带颜色时每个实例 12 个浮点:8 个变换分量 + 4 个颜色分量
			int o = g * 12;
			glowBuf[o + 8] = 1.0f; glowBuf[o + 9] = 0.25f; glowBuf[o + 10] = 0.2f; glowBuf[o + 11] = 0.5f;
			g++;
		}
		if (glowBuf.Length > 0) glowMesh.Buffer = glowBuf;
		glowMesh.VisibleInstanceCount = glowN;
		for (int k = 0; k < 4; k++)
		{
			if (spriteBuf[k].Length > 0) spriteMesh[k].Buffer = spriteBuf[k];
			spriteMesh[k].VisibleInstanceCount = spriteCount[k];
		}
	}
}
