# 加班求生

Godot 4.7 (C# / .NET 8) 制作的 2D 生存肉鸽 Demo,办公室末日主题。玩家走位,文具武器自动攻击,二十波怪潮之间在商店购买、合成、升阶武器和道具。

本仓库包含游戏代码、数据表和游戏所用素材:

- `src/` C# 源码
- `data/` 数值与配置(`balance.json` 可直接调整波次时长、刷怪、怪物强度、武器与道具)
- `scenes/`、`project.godot`、`export_presets.cfg` 工程配置
- `assets/` 美术(角色、怪物、武器、道具、界面、特效)与音效、背景音乐

美术图片与音频素材在 `assets/`,克隆后即可运行。

## 运行

```
dotnet build RogueDemo.csproj
godot --path .
```

测试:`godot --headless --path . -- --test --selftest`(另有 `--mediatest`、`--facingtest`、`--uitest`、`--balancetest`)。

## 许可

- 代码:[MIT](LICENSE),可自由使用、修改、商用。
- 美术图片:CC BY 4.0,可自由使用、修改、商用,需署名作者 d95777。
- 音效与背景音乐:CC0,无需署名。

详见 [assets/LICENSE.md](assets/LICENSE.md)。
