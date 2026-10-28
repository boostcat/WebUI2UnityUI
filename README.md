# WebUI2UnityUI

## 这是什么

把 AI 生成的 Web 网页原型一键转成Unity UGUI并自动完成命名和结构，极速搭建UI。

通过AI 写 Web 界面，浏览器里看效果，拖进 Unity 直接出界面。

---

## 怎么安装

**通过Package Manager安装**

1. Unity 菜单 `Window -> Package Manager`
2. 左上角 `+` -> `Add package from git URL...`
3. 填入：`https://github.com/boostcat/WebUI2UnityUI.git`

---

## 怎么使用

1. **生成**：把 `Documentation/AI生成WebUI原型规范文档.md` 发给任意大模型（豆包 / WorkBuddy / Claude / CodeX 等），说一句「帮我生成一个赛车游戏胜利的结算界面」，拿到一个 `.html`。
2. **确认**：双击打开 html，确认浏览器里的效果 OK 了，把它拖进 Unity 的 `Assets`。
3. **烘焙**：菜单 `Tools -> WebUI转UnityUI工具` → 拖入 `.html` → 点「执行烘焙生成」。

界面不满意？让 AI 改改这一个文件再来一次即可。

---

## 控件支持

| 声明 | Unity 组件 | 附加属性 |
| :--- | :--- | :--- |
| `data-u-type="div"` | Image（无背景色时自动剔除 Raycast） | 尺寸、坐标、背景色 |
| `data-u-type="image"` | Image | 尺寸、坐标、背景色 |
| `data-u-type="text"` | TextMeshProUGUI | 字号、颜色、对齐 |
| `data-u-type="button"` | Button + Image + TMP | 内部文本样式 |
| `data-u-type="input"` | TMP_InputField | `placeholder` 占位文本 |
| `data-u-type="scroll"` | ScrollRect + Mask | `data-u-dir="v/h"` |
| `data-u-type="toggle"` | Toggle + Image + TMP（可多选） | `data-u-checked` |
| `data-u-type="radio"` | Toggle（圆形）+ ToggleGroup（互斥） | `data-u-checked`，同组须放同一 `div` 内 |
| `data-u-type="slider"` | Slider + Image | `data-u-value`（0~1） |
| `data-u-type="dropdown"` | TMP_Dropdown + ScrollRect | 内部 `<option>` 选项 |

---

## License

[MIT](LICENSE.md) — 可自由商用、修改、二次分发，保留版权声明即可。