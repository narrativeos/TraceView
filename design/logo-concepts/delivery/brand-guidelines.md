# TraceView 品牌速览（Brand Sheet）

## 标志：Hex T

六边形轮廓继承自旧版 Caly 图标，由线框升级为**实心填充 + 负空间 T**；T 的右上角 45°
斜切呼应 PDF 页面的折角。一个图形同时承载：TraceView 的首字母、文档、以及旧品牌的形状资产。

- 主符号：`traceview-symbol-color.svg`（单一品牌蓝 #1478D2，evenodd 真实镂空）
- 反白：`traceview-symbol-white.svg`（深色背景 / 单色应用）
- 纯黑：`traceview-symbol-black.svg`（雕刻、单色打印）

## 组合锁定（Lockups）

| 用途 | 文件 |
|---|---|
| 横排（默认） | `traceview-horizontal-color.svg` / `-white.svg` |
| 竖排（窄空间） | `traceview-stacked-color.svg` / `-white.svg` |
| 纯字标 | `traceview-wordmark-color.svg` / `-black.svg` / `-white.svg` |

字标为几何构造的单线圆头字形（无外部字体依赖；笔画为 stroke 形式，交付印刷前可展开为轮廓）。
符号与字标的光学高度对齐：字标大写高度 ≈ 符号宽度的 33%。

## 色彩

| 角色 | 色值 | 用法 |
|---|---|---|
| TraceView 蓝 | `#1478D2` | 符号、App 图标底、主强调色 |
| 墨蓝 Ink | `#123A5C` | 字标、正文强调 |
| 反白 | `#FFFFFF` | 深色背景上的标志 |

背景对比不足时优先使用反白版本。

## App 图标

- 母版：`traceview-app-icon.svg`（512 画布，圆角 104，蓝色圆角方块 + 白色镂空 Hex T，
  白色为有意的正形叠色，非假镂空）
- macOS：`TraceView.icns`（16–1024 全尺寸）
- Windows：`traceview-app.ico`（16/32/48/256）
- 位图：`appicon-{16,32,64,128,256,512,1024}.png`
- Web/PWA：`favicon.ico`、`favicon-16/32/48.png`、`favicon.svg`、`apple-touch-icon.png`、
  `icon-192.png`、`icon-512.png`、`maskable-512.png`、`site.webmanifest`

## 最小尺寸与净空

- 符号最小 16 px（App 图标 / favicon 下限）；带字标横排最小宽度 120 px。
- 净空 ≥ 符号高度的 1/4，任何元素不得进入该范围。
- 16 px 场景一律使用符号或 App 图标版本，不使用带字标锁定。

## 注意事项

- 不要给标志加投影、渐变、描边或旋转。
- 不要拉伸变形；锁定文件按等比缩放。
- 深色照片背景上使用反白版本。
- 商标未做检索；发布前建议做一次反向图像搜索与商标数据库初筛。
