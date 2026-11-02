using Godot;

namespace RogueDemo;

public partial class Main
{
	static readonly Color[] TierGlow = { new Color(0, 0, 0, 0), new Color(0.72f, 0.59f, 1.0f, 0.55f), new Color(1.0f, 0.54f, 0.35f, 0.6f), new Color(1.0f, 0.83f, 0.3f, 0.7f) };
	static readonly float[] TierScale = { 1.0f, 1.1f, 1.2f, 1.3f };

	public void DrawFloor(CanvasItem c)
	{
		// 静态内容，只在启动时绘制一次;镜头移动由相机负责。
		c.DrawRect(FIELD.Grow(3000), new Color("141a22"));
		c.DrawRect(FIELD.Grow(15), new Color("2a2f38"));
		c.DrawRect(FIELD.Grow(6), new Color("9aa3b0"), false, 2f);
		c.DrawTextureRect(floorTexture, FIELD, false);
		c.DrawRect(FIELD.Grow(10), new Color("7b8594"), false, 5f);
		foreach (float cx in new[] { FIELD.Position.X + 60, FIELD.End.X - 60 })
			foreach (float cy in new[] { FIELD.Position.Y + 60, FIELD.End.Y - 60 })
				c.DrawArc(new Vector2(cx, cy), 16f, 0f, Mathf.Tau, 32, new Color(0.75f, 0.78f, 0.85f, 0.35f), 1.5f, true);
	}

	public override void _Draw()
	{
		if (Phase == "title")
		{
			DrawRect(new Rect2(0, 0, 1280, 800), new Color("141a22"));
			DrawTextureRect(Textures["sky"], new Rect2(0, 0, 1280, 800), false, new Color(0.86f, 0.88f, 0.92f));
			DrawTextureRect(Textures["far"], new Rect2(0, -55, 1280, 670), false, new Color(0.36f, 0.48f, 0.40f, 0.65f));
			DrawTextureRect(Textures["mid"], new Rect2(0, 30, 1280, 780), false, new Color(0.42f, 0.54f, 0.40f, 0.65f));
			for (int i = 0; i < 25; i++)
			{
				var p = new Vector2((i * 137f + ambientClock * 8f) % 1280f, 200f + (i * 67f + Mathf.Sin(ambientClock + i) * 8f) % 520f);
				DrawCircle(p, 1.5f, new Color(0.8f, 0.91f, 0.65f, 0.35f));
			}
			return;
		}
		foreach (var d in Drops)
		{
			if (!d.Heart)
			{
				DrawTextureRect(dropTex[0], new Rect2(d.P - new Vector2(16, 16), new Vector2(32, 32)), false);
				continue;
			}
			// 爱心:上下浮动、轻微缩放,下方有红色光晕和落地阴影
			float beat = 1f + 0.12f * Mathf.Sin(TotalTime * 6f + d.P.X * 0.05f);
			float bob = Mathf.Sin(TotalTime * 3f + d.P.Y * 0.05f) * 4f;
			var hc = d.P + new Vector2(0, -26 + bob);
			DrawSetTransform(d.P + new Vector2(0, 4), 0f, new Vector2(1f, 0.4f));
			DrawCircle(Vector2.Zero, 13f, new Color(0, 0, 0, 0.3f));
			DrawSetTransform(Vector2.Zero);
			DrawTextureRect(glowTex, new Rect2(hc - new Vector2(52, 52), new Vector2(104, 104)), false, new Color(1f, 0.25f, 0.3f, 0.55f));
			float hsz = 54f * beat;
			DrawTextureRect(dropTex[1], new Rect2(hc - new Vector2(hsz, hsz) / 2f, new Vector2(hsz, hsz)), false);
		}
		foreach (var w in Warnings)
		{
			float k = Mathf.Clamp(w.Time, 0f, 1f);
			float pulse = Mathf.Abs(Mathf.Sin(w.Time * 10f));
			var wc = new Color(0.95f, 0.35f, 0.28f);
			DrawCircle(w.P, 26f, new Color(wc, 0.10f + 0.12f * pulse));
			DrawArc(w.P, 12f + 22f * k, 0f, Mathf.Tau, 32, new Color(wc, 0.5f + 0.4f * pulse), 2.5f, true);
			DrawLine(w.P + new Vector2(-7, -7), w.P + new Vector2(7, 7), new Color(wc, 0.9f), 3f, true);
			DrawLine(w.P + new Vector2(7, -7), w.P + new Vector2(-7, 7), new Color(wc, 0.9f), 3f, true);
		}
		foreach (var e in Enemies)
		{
			if (e.Hp <= 0f) continue;
			var p = e.Pos;
			if (e.Windup > 0f) DrawTelegraph(e, p);
				if (e.Type == 4 && Wave >= 4) DrawArc(p, 170f, 0f, Mathf.Tau, 40, new Color(0.4f, 1f, 0.5f, 0.16f), 2f, true);
				if (e.Elite)
				{
					float epulse = 0.5f + 0.5f * Mathf.Sin(TotalTime * 5f + e.Id);
					DrawArc(p + new Vector2(0, -4), e.Def.Height * 0.62f, 0f, Mathf.Tau, 36, new Color(1f, 0.55f, 0.1f, 0.55f + 0.35f * epulse), 3.5f, true);
					DrawArc(p + new Vector2(0, -4), e.Def.Height * 0.62f + 6f, 0f, Mathf.Tau, 36, new Color(1f, 0.55f, 0.1f, 0.2f), 6f, true);
				}
				if (e.Stun > 0f)
				{
					// 麻痹:身上不断迸出细小电花
					int ss = (int)(TotalTime * 20f);
					for (int k = 0; k < 3; k++)
					{
						float a0 = ((ss * 31 + k * 97 + e.Id * 13) % 628) / 100f;
						var c0 = p + new Vector2(0, -e.Def.Height * 0.5f) + U.Dir(a0) * (e.Def.Height * 0.32f);
						var c1 = c0 + U.Dir(a0 + 1.1f) * 13f + U.Dir(a0 - 0.7f) * 7f;
						DrawLine(c0, c1, new Color(0.6f, 0.95f, 1f, 0.95f), 2.5f, true);
					}
				}
			if ((e.Hp < e.MaxHp || e.Elite) && e.Type != 7)
			{
				// 血条画在头顶,带深色描边,不会被脚下的阴影或别的怪物挡住
				float hh = e.Def.Height * (e.Elite ? 1.35f : 1f);
				float barW = Mathf.Max(34f, hh * 0.55f);
				float barY = p.Y - hh - 12f;
				float ratio = Mathf.Clamp(e.Hp / e.MaxHp, 0f, 1f);
				DrawRect(new Rect2(p.X - barW / 2f - 1.5f, barY - 1.5f, barW + 3f, 8f), new Color(0.04f, 0.04f, 0.06f, 0.85f));
				DrawRect(new Rect2(p.X - barW / 2f, barY, barW, 5f), new Color(0.25f, 0.1f, 0.1f, 0.9f));
				DrawRect(new Rect2(p.X - barW / 2f, barY, barW * ratio, 5f), ratio > 0.5f ? new Color("e8a24a") : new Color("e0533f"));
				if (e.Elite && Ui != null && Ui.Font != null)
					{
						DrawStringOutline(Ui.Font, new Vector2(p.X - 24f, barY - 4f), "精英", HorizontalAlignment.Center, 48, 17, 5, new Color(0.1f, 0.04f, 0.02f));
						DrawString(Ui.Font, new Vector2(p.X - 24f, barY - 4f), "精英", HorizontalAlignment.Center, 48, 17, new Color(1f, 0.62f, 0.15f));
					}
			}
		}
		if (Player != null && IsInstanceValid(Player))
		{
			DrawArc(PlayerPos, 22f, 0f, Mathf.Tau, 48, new Color(0.81f, 0.91f, 0.6f, 0.65f), 1.2f, true);
			DrawWeapons(this, false);
		}
		int projectileCount = Projectiles.Count;
		foreach (var b in Projectiles)
		{
			var p = b.P;
			var c = b.Color;
			if (b.Enemy && b.Sprite != PSprite.None)
			{
				if (projectileCount <= BatchThreshold) DrawEnemyProjectile(b, projectileCount);
				continue;
			}
			if (!b.Enemy && b.Kind == WKind.Bow)
			{
				DrawArrow(p, b.V.Angle(), 54f);
				continue;
			}
			if (!b.Enemy && b.Kind == WKind.Magic)
			{
				var calc = b.Icon;
				if (calc != null)
				{
					float csize = 50f;
					var cdims = calc.GetSize();
					float cfit = csize / Mathf.Max(cdims.X, cdims.Y);
					var crect = cdims * cfit;
					float cspin = (1.7f - b.Life) * 10f;
					DrawSetTransform(p + new Vector2(0, csize * 0.5f), 0f, new Vector2(1f, 0.4f));
					DrawCircle(Vector2.Zero, csize * 0.34f, new Color(0, 0, 0, 0.25f));
					DrawSetTransform(Vector2.Zero);
					for (int k = 3; k > 0; k--)
					{
						DrawSetTransform(p - b.V.Normalized() * k * 13f, cspin - k * 0.3f);
						DrawTextureRect(calc, new Rect2(-crect * 0.5f, crect), false, new Color(1, 1, 1, 0.18f));
					}
					DrawSetTransform(p, cspin);
					DrawTextureRect(calc, new Rect2(-crect * 0.5f, crect), false);
					DrawSetTransform(Vector2.Zero);
				}
				else Media.DrawEffect(this, "fire", p, 84f, b.V.Angle() + Mathf.Pi, 1.7f - b.Life, true);
				continue;
			}
			DrawLine(p - b.V.Normalized() * 18f, p, new Color(c, 0.4f), b.R, true);
			DrawCircle(p, b.R, c);
			DrawCircle(p, b.R * 0.42f, new Color("fff6d8"));
		}
		foreach (var e in Effects) DrawEffect(e);
	}

	// 轨道上半圈(在人物身后)的武器画在后层,其余画在前层
	bool WeaponBehind(int i) => Mathf.Sin(-Mathf.Pi * 0.90f + i * Mathf.Tau / Mathf.Max(Weapons.Count, 1)) < -0.2f;

	public void DrawWeapons(CanvasItem c, bool back)
	{
		for (int i = 0; i < Weapons.Count; i++)
		{
			var w = Weapons[i];
			bool thrusting = Balance.Weapons[w.Type].Kind == WKind.Lance && w.Flash > 0f;
			if ((WeaponBehind(i) && !thrusting) != back) continue;
			var data = Balance.Weapons[w.Type];
			var tex = Textures[data.Image];
			var p = WeaponPosition(i);
			// 近战类攻击瞬间,武器朝目标猛地戳一下
			float rot = WeaponRotation(i);
			float thrustGrow = 1f;
			if (thrusting)
			{
				p = LanceCenter(i, 1f - w.Flash / LanceFlash, out thrustGrow, out _);
				rot = w.LanceAngle - Mathf.DegToRad(data.NativeAngleDeg);
			}
			else if (w.Flash > 0f && (data.Kind == WKind.Spear || data.Kind == WKind.Blade))
				p += U.Dir(w.Angle) * 18f * Mathf.Sin(Mathf.Pi * (1f - w.Flash / 0.2f));
			if (w.Flash > 0f && data.Kind == WKind.Baton)
			{
				// 警棍:从一侧大幅抡到另一侧,同时向前探出
				float t = 1f - w.Flash / 0.2f;
				rot = w.Angle - Mathf.DegToRad(data.NativeAngleDeg) + Mathf.Lerp(-1.3f, 1.3f, t);
				p += U.Dir(w.Angle) * 26f * Mathf.Sin(Mathf.Pi * t);
			}
			c.DrawSetTransform(p, rot);
			float tierScale = TierScale[w.Tier - 1];
			var size = new Vector2(50f, 50f * tex.GetHeight() / tex.GetWidth()) * tierScale * thrustGrow * (data.Kind == WKind.Lance ? PencilScale : (data.Kind == WKind.Baton ? 1.3f : 1f));
			var tierGlow = TierGlow[w.Tier - 1];
			if (w.Tier > 1)
			{
				float pulse = 1f + 0.05f * Mathf.Sin(TotalTime * 6f + i);
				for (int g = 0; g < 3; g++)
				{
					float grow = 1f + (0.10f + 0.07f * g) * pulse;
					c.DrawTextureRect(tex, new Rect2(-size * grow / 2f, size * grow), false, new Color(tierGlow, tierGlow.A * (0.5f - 0.14f * g)));
				}
			}
			c.DrawTextureRect(tex, new Rect2(-size / 2f, size), false, w.Flash > 0f ? new Color(1.6f, 1.6f, 1.25f) : Colors.White);
			c.DrawSetTransform(Vector2.Zero);
			// 出箭后短暂空弓,恢复搭箭;与飞行箭使用同一方向和箭尖支点。
			if (data.Kind == WKind.Bow && w.Flash <= 0.08f) DrawArrowOn(c, WeaponMuzzle(i), w.Angle, 46f);
		}
	}

	static string SpriteKey(PSprite s) => s switch { PSprite.Paper => "proj_paper", PSprite.Note => "proj_note", PSprite.Ink => "proj_ink", _ => "proj_orb" };

	void DrawEffect(Fx e)
	{
		var color = e.Color;
		color.A *= Mathf.Clamp(e.Life / e.Max, 0f, 1f);
		switch (e.Kind)
		{
			case FxKind.Sprite:
				Media.DrawEffect(this, e.Name, e.P, e.Size, e.Angle, e.Max - e.Life);
				break;
			case FxKind.Ring:
				DrawArc(e.P, e.R * (1f - e.Life / e.Max) * 0.75f + e.R * 0.25f, 0f, Mathf.Tau, 40, color, 2f, true);
				break;
			case FxKind.Line:
				DrawLine(e.P, e.End, new Color(color, color.A * 0.2f), 12f, true);
				DrawLine(e.P, e.End, color, 2.5f, true);
				break;
			case FxKind.Slash:
			{
				float sweep = e.Full ? Mathf.Pi : 1.05f;
				DrawArc(e.P, e.R, e.Angle - sweep, e.Angle + sweep, 36, color, 5f, true);
				DrawArc(e.P, e.R - 8f, e.Angle - sweep * 0.8f, e.Angle + sweep * 0.8f, 30, new Color(color, color.A * 0.3f), 10f, true);
				break;
			}
			case FxKind.Scratch:
			{
				float sage = 1f - e.Life / e.Max;
				var sdir = U.Dir(e.Angle);
				var sside = new Vector2(-sdir.Y, sdir.X);
				float sfade = Mathf.Clamp(e.Life / e.Max * 1.6f, 0f, 1f);
				for (int k = 0; k < e.Count; k++)
				{
					float off = (k - (e.Count - 1) * 0.5f) * 0.30f;
					var d3 = sdir.Rotated(off);
					float reach = e.R * Mathf.Min(1f, sage * 3.2f);
					var pts = new Vector2[6];
					for (int j = 0; j < 6; j++)
					{
						float f = j / 5f;
						float jag = Mathf.Sin(k * 7.3f + j * 2.9f) * 7f * (j > 0 ? 1f : 0f);
						pts[j] = e.P + d3 * (18f + reach * f) + new Vector2(-d3.Y, d3.X) * jag;
					}
					DrawPolyline(pts, new Color(0.12f, 0.12f, 0.16f, sfade), 7f, true);
					DrawPolyline(pts, new Color(0.45f, 0.45f, 0.52f, sfade), 3f, true);
				}
				for (int k = 0; k < 5; k++)
				{
					var chip = e.P + sdir * (e.R * 0.55f * Mathf.Min(1f, sage * 2.5f)) + sside * (k - 2) * 13f + new Vector2(0, sage * 18f);
					DrawRect(new Rect2(chip - new Vector2(4, 3), new Vector2(8, 6)), new Color(0.93f, 0.78f, 0.45f, sfade));
				}
				break;
			}
			case FxKind.Fling:
			{
				float fage = 1f - e.Life / e.Max;
				float feased = 1f - Mathf.Pow(1f - fage, 3f);
				float ffade = Mathf.Clamp(e.Life / e.Max * 2.2f, 0f, 1f);
				for (int k = 0; k < e.N; k++)
				{
					float fa = Mathf.Tau * k / e.N + 0.3f;
					var fpos = e.P + U.Dir(fa) * e.R * (0.2f + 0.8f * feased);
					DrawSetTransform(fpos, fage * 9f + k);
					DrawRect(new Rect2(-10, -13, 20, 26), new Color(0.97f, 0.97f, 1.0f, ffade));
					DrawRect(new Rect2(-10, -13, 20, 26), new Color(0.1f, 0.09f, 0.14f, ffade), false, 3f);
					DrawLine(new Vector2(-5, -5), new Vector2(5, -5), new Color(0.55f, 0.4f, 0.85f, ffade), 2.5f);
					DrawLine(new Vector2(-5, 2), new Vector2(5, 2), new Color(0.55f, 0.4f, 0.85f, ffade), 2.5f);
					DrawSetTransform(Vector2.Zero);
				}
				break;
			}
			case FxKind.Cut:
			{
				float cage = 1f - e.Life / e.Max;
				var cd = (e.End - e.P).Normalized();
				var cperp = new Vector2(-cd.Y, cd.X);
				float cwidth = e.W * (1f - cage);
				var cmid = e.P.Lerp(e.End, 0.5f);
				float cfade = Mathf.Clamp(e.Life / e.Max * 1.8f, 0f, 1f);
				var body = new[] { e.P + cperp * cwidth * 0.15f, cmid + cperp * cwidth, e.End, cmid - cperp * cwidth, e.P - cperp * cwidth * 0.15f };
				DrawColoredPolygon(body, new Color(0.96f, 0.98f, 1.0f, cfade));
				DrawPolyline(new[] { body[0], body[1], body[2], body[3], body[4], body[0] }, new Color(0.12f, 0.14f, 0.2f, cfade), 3f, true);
				for (int k = 0; k < 5; k++)
				{
					float sa = cd.Angle() + (k - 2) * 0.55f;
					DrawLine(e.End, e.End + U.Dir(sa) * (14f + 10f * cage), new Color(1.0f, 0.95f, 0.6f, cfade), 3.5f, true);
				}
				break;
			}
			case FxKind.Bolt:
			{
				// 电弧:沿线折成锯齿,每 1/30 秒换一次抖动形状,看起来在噼啪闪动
				float bage = 1f - e.Life / e.Max;
				float bfade = Mathf.Clamp(e.Life / e.Max * 1.6f, 0f, 1f);
				var bd = e.End - e.P;
				float blen = bd.Length();
				if (blen < 1f) break;
				var bdir = bd / blen;
				var bperp = new Vector2(-bdir.Y, bdir.X);
				int bseg = Mathf.Clamp((int)(blen / 22f), 3, 14);
				int bseed = e.N * 131 + (int)(bage * 30f) * 17;
				var pts = new Vector2[bseg + 1];
				for (int k = 0; k <= bseg; k++)
				{
					float jitter = k == 0 || k == bseg ? 0f : ((((uint)(bseed + k * 7919) * 2654435761u) >> 8) % 1000 / 500f - 1f) * 13f;
					pts[k] = e.P + bdir * (blen * k / bseg) + bperp * jitter;
				}
				DrawPolyline(pts, new Color(0.35f, 0.7f, 1f, 0.35f * bfade), 12f, true);
				DrawPolyline(pts, new Color(0.55f, 0.9f, 1f, 0.9f * bfade), 5f, true);
				DrawPolyline(pts, new Color(1f, 1f, 1f, bfade), 2f, true);
				DrawCircle(e.End, 9f * (1f - bage * 0.5f), new Color(0.6f, 0.92f, 1f, 0.6f * bfade));
				break;
			}
			case FxKind.Marker:
			{
				float mage = 1f - e.Life / e.Max;
				float mfade = Mathf.Clamp(e.Life / e.Max * 1.5f, 0f, 1f);
				float mgrow = Mathf.Min(1f, mage * 5f);
				var mend = e.P.Lerp(e.End, mgrow);
				DrawLine(e.P, mend, new Color(1.0f, 0.92f, 0.2f, 0.5f * mfade), 22f, true);
				DrawLine(e.P, mend, new Color(1.0f, 0.98f, 0.6f, 0.75f * mfade), 9f, true);
				DrawCircle(e.P, 11f, new Color(1.0f, 0.92f, 0.2f, 0.5f * mfade));
				DrawCircle(mend, 16f * (0.8f + 0.5f * mage), new Color(1.0f, 0.92f, 0.2f, 0.55f * mfade));
				break;
			}
			case FxKind.Glyph:
			{
				float gage = 1f - e.Life / e.Max;
				var gpos = e.P + e.V * (1f - Mathf.Pow(1f - gage, 2f)) + new Vector2(0, -30f * gage);
				float gfade = Mathf.Clamp(e.Life / e.Max * 2f, 0f, 1f);
				if (Ui != null && Ui.Font != null)
				{
					DrawStringOutline(Ui.Font, gpos + new Vector2(-12, 12), e.Text, HorizontalAlignment.Center, 24, 40, 10, new Color(0.1f, 0.06f, 0.04f, gfade));
					DrawString(Ui.Font, gpos + new Vector2(-12, 12), e.Text, HorizontalAlignment.Center, 24, 40, new Color(1.0f, 0.78f, 0.3f, gfade));
				}
				break;
			}
			case FxKind.Puff:
			{
				float age = 1f - e.Life / e.Max;
				DrawCircle(e.P + new Vector2(0, -14f * age), e.R * (0.6f + age), new Color(0.84f, 0.8f, 0.74f, 0.38f * (1f - age)));
				break;
			}
			case FxKind.Scrap:
			{
				float age2 = e.Max - e.Life;
				var pos = e.P + e.V * age2 + new Vector2(0, 320f * age2 * age2);
				DrawSetTransform(pos, e.Rot + age2 * e.Spin);
				float fade = Mathf.Clamp(e.Life / e.Max * 2f, 0f, 1f);
				DrawRect(new Rect2(-11, -8, 22, 16), new Color(0.96f, 0.96f, 1.0f, fade));
				DrawRect(new Rect2(-11, -8, 22, 16), new Color(0.1f, 0.09f, 0.14f, fade), false, 3f);
				DrawSetTransform(Vector2.Zero);
				break;
			}
			case FxKind.Lance:
			{
				// 铅笔长枪的残影:跟随武器本体刺出的轨迹,不再另画一支铅笔
				if (e.N >= Weapons.Count) break;
				float lage = 1f - e.Life / e.Max;
				var center = LanceCenter(e.N, lage, out _, out float llen);
				var ldir = U.Dir(e.Angle);
				var lorigin = e.P + ldir * 20f;
				var tip = center + ldir * (llen * 0.5f);
				float lfade = Mathf.Clamp(e.Life / e.Max * 1.8f, 0f, 1f) * (lage < 0.6f ? 1f : Mathf.Clamp((1f - lage) / 0.4f, 0f, 1f));
				DrawLine(lorigin, tip, new Color(1.0f, 0.9f, 0.45f, 0.28f * lfade), 14f + 4f * e.Count, true);
				DrawLine(lorigin, tip, new Color(1.0f, 1.0f, 0.9f, 0.5f * lfade), 4f, true);
				DrawCircle(tip, 7f + 2f * e.Count, new Color(1.0f, 0.95f, 0.6f, 0.5f * lfade));
				break;
			}
			case FxKind.Confetti:
			{
				float cage2 = e.Max - e.Life;
				DrawSetTransform(e.P + e.V * cage2, e.Rot + cage2 * e.Spin);
				DrawRect(new Rect2(-7, -4, 14, 8), new Color(e.Color, Mathf.Clamp(e.Life / e.Max * 2f, 0f, 1f)));
				DrawSetTransform(Vector2.Zero);
				break;
			}
			case FxKind.Text:
				if (Ui != null && Ui.Font != null) DrawString(Ui.Font, e.P, e.Text, HorizontalAlignment.Center, -1, 26, color);
				break;
		}
	}

	void DrawTelegraph(Enemy e, Vector2 p)
	{
		// 怪物技能前摇：不同类型有不同的危险提示，强度随前摇推进增加。
		var kind = e.Def.Kind;
		float prog = Mathf.Clamp(1f - e.Windup / 0.7f, 0f, 1f);
		float pulse = 0.5f + 0.5f * Mathf.Sin(TotalTime * 30f);
		var body = p + new Vector2(0, -18);
		var danger = new Color(1.0f, 0.28f, 0.22f);
		switch (kind)
		{
			// 普通怪物的冲锋与远程攻击不画弹道/落点提示,只保留头顶的“!”;精英和首领才有范围预警
			case EKind.Charge:
			{
				if (!e.Elite) break;
				var d = e.Dir;
				var side = new Vector2(-d.Y, d.X);
				float reach = ChargeSpeed * ChargeTime, half = 30f;
				DrawColoredPolygon(new[] { p + side * half, p + d * reach + side * half, p + d * reach - side * half, p - side * half }, new Color(danger, 0.14f + 0.28f * prog));
				DrawLine(p + side * half, p + d * reach + side * half, new Color(danger, 0.6f + 0.3f * pulse), 4.5f, true);
				DrawLine(p - side * half, p + d * reach - side * half, new Color(danger, 0.6f + 0.3f * pulse), 4.5f, true);
				break;
			}
			case EKind.Ranged:
			{
				if (!e.Elite) break;
				for (int i = -2; i <= 2; i++)
					DrawDashedLine(body, body + e.Dir.Rotated(i * 0.22f) * 340f, new Color(danger, 0.35f + 0.5f * prog), 4f, 12f);
				break;
			}
			case EKind.Boss:
			{
				float baseAngle = (TotalTime + e.Windup) * 0.4f;
				if (e.Pattern == 2)
				{
					var fd = e.Dir;
					var edgeL = fd.Rotated(-0.62f) * 380f;
					var edgeR = fd.Rotated(0.62f) * 380f;
					DrawColoredPolygon(new[] { body, body + edgeL, body + fd * 400f, body + edgeR }, new Color(danger, 0.08f + 0.22f * prog));
					DrawLine(body, body + edgeL, new Color(danger, 0.6f + 0.3f * pulse), 4.5f, true);
					DrawLine(body, body + edgeR, new Color(danger, 0.6f + 0.3f * pulse), 4.5f, true);
					break;
				}
				DrawCircle(body, 64f, new Color(danger, 0.06f + 0.14f * prog));
				DrawArc(body, 52f + 8f * pulse, 0f, Mathf.Tau, 48, new Color(danger, 0.7f + 0.3f * pulse), 5f, true);
				for (int i = 0; i < 12; i++)
				{
					var v = U.Dir(baseAngle + Mathf.Tau * i / 12f);
					DrawLine(body + v * 58f, body + v * (84f + 120f * prog), new Color(danger, 0.35f + 0.55f * prog), 6.5f, true);
				}
				break;
			}
			default:
				if (e.Elite)
				{
					DrawCircle(p, 150f, new Color(danger, 0.06f + 0.16f * prog));
					DrawArc(p, 150f * (0.35f + 0.65f * prog), 0f, Mathf.Tau, 48, new Color(danger, 0.7f + 0.3f * pulse), 4f, true);
				}
				else DrawArc(p, 30f, 0f, Mathf.Tau, 32, new Color("f1a87f"), 2f, true);
				break;
		}
		if (Ui != null && Ui.Font != null)
		{
			float height = e.Def.Height;
			float bob = Mathf.Sin(TotalTime * 14f) * 3f;
			var mark = p + new Vector2(-15, -height - 6f + bob);
			DrawStringOutline(Ui.Font, mark, "!", HorizontalAlignment.Center, 30, 72, 14, new Color(0.1f, 0.05f, 0.08f));
			DrawString(Ui.Font, mark, "!", HorizontalAlignment.Center, 30, 72, new Color(1.0f, 0.82f, 0.3f));
		}
	}

	void DrawEnemyProjectile(Projectile b, int projectileCount)
	{
		// 贴图与尺寸只在第一次绘制时计算并缓存在弹道上，之后每帧直接取用
		if (b.Tex == null)
		{
			b.Tex = Textures[SpriteKey(b.Sprite)];
			var dims = b.Tex.GetSize();
			b.RectSize = dims * (b.Size / Mathf.Max(dims.X, dims.Y));
		}
		var tex = b.Tex;
		float size = b.Size;
		var rectSize = b.RectSize;
		float spin = b.Spin + (4f - b.Life) * 7f;
		var p = b.P;
		// 红色危险光晕：贴图一次绘制，保证在复杂地面上也能一眼看到
		float glowR = size * 0.8f;
		DrawTextureRect(glowTex, new Rect2(p - new Vector2(glowR, glowR), new Vector2(glowR * 2f, glowR * 2f)), false, new Color(1.0f, 0.25f, 0.2f, 0.5f));
		// 弹道很多时降低细节：去掉落地阴影和拖尾，避免满屏弹幕拖垮帧率
		if (projectileCount <= 80)
		{
			var dir = b.V.Normalized();
			DrawSetTransform(p + new Vector2(0, size * 0.45f), 0f, new Vector2(1f, 0.4f));
			DrawCircle(Vector2.Zero, size * 0.34f, new Color(0, 0, 0, 0.28f));
			for (int k = 3; k > 0; k--)
			{
				DrawSetTransform(p - dir * k * size * 0.22f, spin - k * 0.35f);
				DrawTextureRect(tex, new Rect2(-rectSize * 0.5f * (1f - 0.14f * k), rectSize * (1f - 0.14f * k)), false, new Color(1, 0.7f, 0.7f, 0.22f));
			}
		}
		DrawSetTransform(p, spin);
		DrawTextureRect(tex, new Rect2(-rectSize * 0.5f, rectSize), false);
		DrawSetTransform(Vector2.Zero);
	}

	void DrawArrow(Vector2 tip, float angle, float length) => DrawArrowOn(this, tip, angle, length);

	void DrawArrowOn(CanvasItem c, Vector2 tip, float angle, float length)
	{
		var texture = Textures["arrow"];
		float height = length * texture.GetHeight() / texture.GetWidth();
		// 原始箭头朝左;位置始终代表箭尖，与弹道碰撞检测使用同一点。
		c.DrawSetTransform(tip, angle + Mathf.Pi);
		c.DrawTextureRect(texture, new Rect2(0, -height / 2f, length, height), false);
		c.DrawSetTransform(Vector2.Zero);
	}
}
