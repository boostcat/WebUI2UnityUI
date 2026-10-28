using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor.SceneManagement;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions;

[System.Serializable]
public class UIDataNode
{
    public string name;
    public string type;
    public string dir;
    public float value;
    public bool isChecked;
    public List<string> options;
    public float x;
    public float y;
    public float width;
    public float height;
    public string color;
    public string fontColor;
    public int fontSize;
    public string textAlign;
    public string text;
    public List<UIDataNode> children;
}

public class WebUI2UnityUIEditor : EditorWindow
{
    // HTML 输入（Unity 内置烘焙，无需浏览器，唯一输入模式）
    private TextAsset htmlAsset;
    private string rawHtmlSource = "";
    private Vector2 htmlScrollPosition;

    private Canvas targetCanvas;

    // 烘焙时可指定的 TMP 覆盖字体；留空则所有 TMP_Text 使用项目默认字体
    private TMP_FontAsset overrideFont;

    private readonly Vector2 REFERENCE_RESOLUTION = new Vector2(1920, 1080);

    [MenuItem("Tools/WebUI转UnityUI工具")]
    public static void ShowWindow()
    {
        GetWindow<WebUI2UnityUIEditor>("WebUI转UnityUI工具");
    }

    private void OnEnable()
    {
        // 打开面板时默认搜索并指定场景中的 Canvas；找不到则自动创建一个默认 Canvas
        if (targetCanvas == null)
        {
            targetCanvas = Object.FindObjectOfType<Canvas>();
        }

        if (targetCanvas == null)
        {
            targetCanvas = CreateDefaultCanvas();
        }
    }

    /// <summary>
    /// 场景中不存在 Canvas 时，自动创建一个默认 Canvas 并引用：
    /// Render Mode = Screen Space - Overlay
    /// UI Scale Mode = Scale With Screen Size，Reference Resolution = 1920 x 1080
    /// </summary>
    private Canvas CreateDefaultCanvas()
    {
        if (EditorApplication.isPlaying) return null;

        GameObject canvasGo = new GameObject("Canvas");

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) canvasGo.layer = uiLayer;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.referenceResolution = REFERENCE_RESOLUTION;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Default Canvas");
        if (canvasGo.scene.IsValid()) EditorSceneManager.MarkSceneDirty(canvasGo.scene);

        // UI 交互（按钮/输入框等）依赖 EventSystem，场景中没有时一并创建
        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(eventSystemGo, "Create EventSystem");
        }

        Debug.Log("[WebUI2UnityUI] 场景中未找到 Canvas，已自动创建默认 Canvas：Screen Space - Overlay / Scale With Screen Size / 1920x1080。");

        return canvas;
    }

    private void OnGUI()
    {
        targetCanvas = (Canvas)EditorGUILayout.ObjectField("目标 Canvas", targetCanvas, typeof(Canvas), true);
        GUILayout.Space(10);

        DrawHtmlModeUI();

        GUILayout.Space(20);

        // TMP 字体覆盖：留空则所有 TMP_Text 使用项目默认字体
        overrideFont = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP 字体 (留空=默认)", overrideFont, typeof(TMP_FontAsset), false);
        GUILayout.Space(10);

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
        if (GUILayout.Button("执行烘焙生成", GUILayout.Height(40)))
        {
            ExecuteBake();
        }

        GUI.backgroundColor = Color.white;
    }

    private void DrawHtmlModeUI()
    {
        EditorGUILayout.HelpBox(
            "直接粘贴 AI 生成的 HTML（带 data-u-type / data-u-name）。\n" +
            "布局采用「约束堆叠」模型：容器用 data-u-layout=\"v|h\" 配合 data-u-gap / data-u-pad，子节点给显式 width/height；\n" +
            "也可用 left / top 绝对定位覆盖。无需浏览器，Unity 内完成全部烘焙。",
            MessageType.Info);

        htmlAsset = (TextAsset)EditorGUILayout.ObjectField("或拖入 .html 文件", htmlAsset, typeof(TextAsset), false);

        GUILayout.Label("或直接粘贴 HTML 源码:", EditorStyles.label);
        htmlScrollPosition = GUILayout.BeginScrollView(htmlScrollPosition, GUILayout.Height(220));
        rawHtmlSource = EditorGUILayout.TextArea(rawHtmlSource, GUILayout.ExpandHeight(true));
        GUILayout.EndScrollView();
    }

    private void ExecuteBake()
    {
        if (targetCanvas == null)
        {
            Debug.LogError("[WebUI2UnityUI] 烘焙中断: 未指定目标 Canvas，无法确定 UI 挂载点。");
            return;
        }

        string html = htmlAsset != null ? htmlAsset.text : rawHtmlSource;
        if (string.IsNullOrWhiteSpace(html))
        {
            Debug.LogError("[WebUI2UnityUI] 烘焙中断: HTML 内容为空，请粘贴 HTML 或拖入 .html 文件。");
            return;
        }

        UIDataNode rootNode = ParseHtmlToNodeTree(html);
        if (rootNode == null)
        {
            Debug.LogError("[WebUI2UnityUI] 烘焙中断: 未能从 HTML 中找到带 data-u-type 与 data-u-name 的根节点。");
            return;
        }

        ConfigureCanvasScaler(targetCanvas);

        // 根节点直接挂到 Canvas：以 Canvas 实际尺寸作为“父级尺寸”参与布局折算
        RectTransform canvasRect = targetCanvas.GetComponent<RectTransform>();
        float canvasW = canvasRect != null ? canvasRect.rect.width : REFERENCE_RESOLUTION.x;
        float canvasH = canvasRect != null ? canvasRect.rect.height : REFERENCE_RESOLUTION.y;

        GameObject rootGo = CreateUINode(rootNode, targetCanvas.transform, 0f, 0f, canvasW, canvasH);

        // 生成完成后：把 UI 根物体（非 Canvas）的 anchoredPosition 归零。
        // 由于锚点已是中心中，归零即让根节点居中对齐到 Canvas 中心。
        RectTransform rootRect = rootGo.GetComponent<RectTransform>();
        if (rootRect != null) rootRect.anchoredPosition = Vector2.zero;

        Undo.RegisterCreatedObjectUndo(rootGo, "Bake UI Prototype");
        Selection.activeGameObject = rootGo;

        Debug.Log($"[WebUI2UnityUI] 烘焙完成: 成功生成 UI 树 [{rootGo.name}]，文本对齐与智能换行已应用。");
    }

    // ============================================================
    //  HTML 解析 + 约束堆叠布局求解（零外部依赖，全部在 Unity 内完成）
    // ============================================================

    private class HtmlNode
    {
        public string tag = "";
        public Dictionary<string, string> attrs = new Dictionary<string, string>();
        public List<HtmlNode> children = new List<HtmlNode>();
        public string directText = "";
    }

    private static readonly HashSet<string> VoidElements = new HashSet<string>
    {
        "input", "img", "br", "hr", "meta", "link", "area", "base",
        "col", "embed", "source", "track", "wbr", "param"
    };

    private UIDataNode ParseHtmlToNodeTree(string html)
    {
        List<HtmlNode> topNodes = ParseHtml(html);
        HtmlNode rootHtml = FindRoot(topNodes);
        if (rootHtml == null) return null;

        // 根节点没有父容器可继承，尺寸只能来自自身 style
        Dictionary<string, string> rootStyle = ParseStyle(GetAttr(rootHtml, "style", ""));
        float rw = ParseLength(rootStyle, "width") ?? 0f;
        float rh = ParseLength(rootStyle, "height") ?? 0f;
        return BuildChild(rootHtml, 0f, 0f, rw, rh);
    }

    private List<HtmlNode> ParseHtml(string html)
    {
        // 去除 HTML 注释，避免注释文本被当作节点文本
        html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);

        var root = new HtmlNode { tag = "#root" };
        var stack = new Stack<HtmlNode>();
        stack.Push(root);

        // 同时匹配标签与标签外文本；属性值内的引号不影响匹配
        var tokenRegex = new Regex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(\/?)>|([^<]+)");

        foreach (Match m in tokenRegex.Matches(html))
        {
            if (m.Groups[5].Success)
            {
                stack.Peek().directText += DecodeEntities(m.Groups[5].Value);
                continue;
            }

            bool closing = m.Groups[1].Value == "/";
            string tag = m.Groups[2].Value.ToLower();
            string attrStr = m.Groups[3].Value;
            bool selfClose = m.Groups[4].Value == "/";

            if (closing)
            {
                if (stack.Count > 1 && stack.Peek().tag == tag)
                {
                    stack.Pop();
                }
                continue;
            }

            var node = new HtmlNode { tag = tag };
            node.attrs = ParseAttrs(attrStr);
            stack.Peek().children.Add(node);

            bool isVoid = VoidElements.Contains(tag);
            if (!selfClose && !isVoid)
            {
                stack.Push(node);
            }
        }

        return root.children;
    }

    private Dictionary<string, string> ParseAttrs(string attrStr)
    {
        var attrs = new Dictionary<string, string>();
        var attrRegex = new Regex(@"([a-zA-Z0-9_\-]+)\s*=\s*(""([^""]*)""|'([^']*)')");
        foreach (Match m in attrRegex.Matches(attrStr))
        {
            string key = m.Groups[1].Value.ToLower();
            string val = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
            attrs[key] = val;
        }
        return attrs;
    }

    private string DecodeEntities(string text)
    {
        return text
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");
    }

    private HtmlNode FindRoot(List<HtmlNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.attrs.ContainsKey("data-u-type") && n.attrs.ContainsKey("data-u-name")) return n;
            var deeper = FindRoot(n.children);
            if (deeper != null) return deeper;
        }
        return null;
    }

    /// <summary>
    /// 扁平化：返回所有「带 data-u-type 且带 data-u-name」的后代节点（文档顺序），
    /// 中途遇到的未标注包裹元素视为透明层（其标注后代直接上提参与布局）。
    /// </summary>
    private List<HtmlNode> Flatten(HtmlNode n)
    {
        var res = new List<HtmlNode>();
        foreach (var c in n.children)
        {
            if (c.attrs.ContainsKey("data-u-type") && c.attrs.ContainsKey("data-u-name"))
            {
                res.Add(c);
            }
            else
            {
                res.AddRange(Flatten(c));
            }
        }
        return res;
    }

    private UIDataNode BuildChild(HtmlNode h, float absX, float absY, float assignedW, float assignedH)
    {
        string type = GetAttr(h, "data-u-type", "div");
        string name = GetAttr(h, "data-u-name", "Node");
        Dictionary<string, string> style = ParseStyle(GetAttr(h, "style", ""));

        // 容器尺寸以「父容器实际分配给它的尺寸」为准，而不是自身 style 里写的值。
        // 否则嵌套容器省略 width/height 时（靠父级撑满），内层子节点会被算成 0 宽/0 高。
        float width = assignedW;
        float height = assignedH;

        string dir = GetAttr(h, "data-u-dir", "v");
        float value = ParseFloat(GetAttr(h, "data-u-value", "0.5")) ?? 0.5f;
        bool isChecked = GetAttr(h, "data-u-checked", "false") == "true";

        string text = (h.directText ?? "").Trim();
        if (type == "input")
        {
            text = GetAttr(h, "value", "") ?? "";
            if (string.IsNullOrEmpty(text)) text = GetAttr(h, "placeholder", "") ?? "";
        }

        var options = new List<string>();
        if (type == "dropdown")
        {
            foreach (var c in h.children)
            {
                if (c.tag == "option")
                {
                    string opt = (c.directText ?? "").Trim();
                    if (!string.IsNullOrEmpty(opt)) options.Add(opt);
                }
            }
        }

        var node = new UIDataNode
        {
            name = name,
            type = type,
            dir = dir,
            value = value,
            isChecked = isChecked,
            options = options,
            x = absX,
            y = absY,
            width = width,
            height = height,
            color = CssColorToHex(style.ContainsKey("background-color") ? style["background-color"] : ""),
            fontColor = CssColorToHex(style.ContainsKey("color") ? style["color"] : ""),
            fontSize = Mathf.RoundToInt(ParseLength(style, "font-size") ?? 0f),
            textAlign = style.ContainsKey("text-align") ? style["text-align"] : "center",
            text = text,
            children = new List<UIDataNode>()
        };

        // ---- 作为容器，对其子节点执行约束堆叠布局 ----
        float pad = ParseLength(GetAttr(h, "data-u-pad", ""), "") ?? 0f;
        float gap = ParseLength(GetAttr(h, "data-u-gap", ""), "") ?? 0f;
        string layout = GetAttr(h, "data-u-layout", "").ToLower();
        bool isV = layout == "v" || layout == "vertical";
        bool isH = layout == "h" || layout == "horizontal";

        float innerW = Mathf.Max(0f, width - 2f * pad);
        float innerH = Mathf.Max(0f, height - 2f * pad);

        float cursorX = pad;
        float cursorY = pad;

        foreach (var childHtml in Flatten(h))
        {
            Dictionary<string, string> cStyle = ParseStyle(GetAttr(childHtml, "style", ""));
            float? childLeft = ParseLength(cStyle, "left");
            float? childTop = ParseLength(cStyle, "top");

            float childW = ParseLength(cStyle, "width")
                ?? (isV ? innerW : 100f);
            float childH = ParseLength(cStyle, "height")
                ?? (isH ? innerH : 40f);

            float cx, cy;
            if (childLeft.HasValue && childTop.HasValue)
            {
                cx = absX + childLeft.Value;
                cy = absY + childTop.Value;
            }
            else if (isV)
            {
                cx = absX + pad;
                cy = absY + cursorY;
                cursorY += childH + gap;
            }
            else if (isH)
            {
                cx = absX + cursorX;
                cy = absY + pad;
                cursorX += childW + gap;
            }
            else
            {
                // 父级非堆叠容器，且无显式 left/top：退化为左上角内边距定位
                cx = absX + pad;
                cy = absY + pad;
            }

            UIDataNode childNode = BuildChild(childHtml, cx, cy, childW, childH);
            node.children.Add(childNode);
        }

        return node;
    }

    private string GetAttr(HtmlNode h, string key, string fallback)
    {
        return h.attrs.TryGetValue(key.ToLower(), out string v) ? v : fallback;
    }

    private Dictionary<string, string> ParseStyle(string style)
    {
        var dict = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(style)) return dict;
        foreach (var part in style.Split(';'))
        {
            int idx = part.IndexOf(':');
            if (idx <= 0) continue;
            string k = part.Substring(0, idx).Trim().ToLower();
            string v = part.Substring(idx + 1).Trim().ToLower();
            if (!string.IsNullOrEmpty(k)) dict[k] = v;
        }
        return dict;
    }

    private float? ParseLength(Dictionary<string, string> style, string key)
    {
        if (style == null || !style.ContainsKey(key)) return null;
        return ParseLength(style[key], key);
    }

    /// <summary>
    /// 解析 CSS 长度（去除 px / % 等单位，仅保留数值）。传入 key 仅为兼容重载。
    /// </summary>
    private float? ParseLength(string raw, string key)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string s = raw.Trim().ToLower();
        // 去掉 px / pt / em 等单位；百分比按数值近似（极少用于几何）
        s = Regex.Replace(s, @"(px|pt|em|rem|vh|vw|%)", "");
        if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
        {
            return v;
        }
        return null;
    }

    private float? ParseFloat(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (float.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
        {
            return v;
        }
        return null;
    }

    private static readonly Dictionary<string, string> NamedColors = new Dictionary<string, string>
    {
        { "transparent", "#FFFFFF00" },
        { "white", "#FFFFFFFF" }, { "black", "#000000FF" },
        { "red", "#FF0000FF" }, { "green", "#008000FF" }, { "blue", "#0000FFFF" },
        { "gray", "#808080FF" }, { "grey", "#808080FF" },
        { "yellow", "#FFFF00FF" }, { "orange", "#FFA500FF" },
        { "cyan", "#00FFFFFF" }, { "magenta", "#FF00FFFF" },
        { "purple", "#800080FF" }, { "brown", "#A52A2AFF" },
        { "pink", "#FFC0CBFF" }, { "gold", "#FFD700FF" },
        { "silver", "#C0C0C0FF" }, { "lime", "#00FF00FF" },
        { "navy", "#000080FF" }, { "teal", "#008080FF" }
    };

    /// <summary>
    /// 将 CSS 颜色（#rgb / #rrggbb / #rrggbbaa / rgb() / rgba() / 颜色名）统一转为 #RRGGBBAA 十六进制。
    /// 空值返回空字符串，交由下游 ApplyComponentByType 使用默认色。
    /// </summary>
    private string CssColorToHex(string css)
    {
        if (string.IsNullOrWhiteSpace(css)) return "";
        css = css.Trim().ToLower();

        if (NamedColors.TryGetValue(css, out string named)) return named;

        if (css.StartsWith("#"))
        {
            string hex = css.Substring(1);
            if (hex.Length == 3)
            {
                hex = new string(new char[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
                return "#" + hex + "FF";
            }
            if (hex.Length == 4)
            {
                hex = new string(new char[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], hex[3], hex[3] });
                return "#" + hex;
            }
            if (hex.Length == 6) return "#" + hex + "FF";
            if (hex.Length == 8) return "#" + hex;
            return "#FFFFFFFF";
        }

        var m = Regex.Match(css, @"rgba?\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:,\s*([\d.]+))?\)");
        if (m.Success)
        {
            int r = int.Parse(m.Groups[1].Value);
            int g = int.Parse(m.Groups[2].Value);
            int b = int.Parse(m.Groups[3].Value);
            string a = "FF";
            if (m.Groups[4].Success)
            {
                int ai = Mathf.RoundToInt(float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) * 255f);
                a = ("0" + ai.ToString("X")).Substring(Mathf.Max(0, ("0" + ai.ToString("X")).Length - 2));
            }
            return $"#{r:X2}{g:X2}{b:X2}{a}";
        }

        return "#FFFFFFFF";
    }

    // ============================================================
    //  UGUI 生成（与原来一致，仅消费 UIDataNode 树）
    // ============================================================

    private void ConfigureCanvasScaler(Canvas canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = REFERENCE_RESOLUTION;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private GameObject CreateUINode(UIDataNode nodeData, Transform parent, float parentAbsX, float parentAbsY, float parentWidth, float parentHeight)
    {
        GameObject go = new GameObject(nodeData.name);
        go.transform.SetParent(parent, false);

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) go.layer = uiLayer;

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        float localX = nodeData.x - parentAbsX;
        float localY = nodeData.y - parentAbsY;

        // 保持与原（左上锚点）布局一致：让节点左上角落在父级左上角偏移 (localX, -localY) 处。
        // 在中心锚点/枢轴下，anchoredPosition 表示节点中心相对父级中心，需按父级尺寸折算。
        float ax = localX + (nodeData.width - parentWidth) / 2f;
        float ay = -localY - (nodeData.height - parentHeight) / 2f;
        rect.anchoredPosition = new Vector2(ax, ay);
        rect.sizeDelta = new Vector2(nodeData.width, nodeData.height);

        Transform childrenContainer = ApplyComponentByType(go, nodeData);

        if (nodeData.children != null && nodeData.children.Count > 0)
        {
            foreach (var childNode in nodeData.children)
            {
                CreateUINode(childNode, childrenContainer, nodeData.x, nodeData.y, nodeData.width, nodeData.height);
            }
        }

        return go;
    }

    private Transform ApplyComponentByType(GameObject go, UIDataNode nodeData)
    {
        Color bgColor = ParseHexColor(nodeData.color, Color.white);
        Color fontColor = ParseHexColor(nodeData.fontColor, Color.black);
        int fontSize = nodeData.fontSize > 0 ? nodeData.fontSize : 24;

        TextAlignmentOptions alignment = ParseTextAlign(nodeData.textAlign);
        bool isMultiLine = nodeData.height > (fontSize * 1.5f);

        switch (nodeData.type.ToLower())
        {
            case "div":
            case "image":
                Image img = go.AddComponent<Image>();
                img.color = bgColor;
                if (img.color.a <= 0.01f) img.raycastTarget = false;
                return go.transform;

            case "text":
                TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
                SetTMPFont(txt);
                txt.text = nodeData.text;
                txt.color = fontColor;
                txt.fontSize = fontSize;
                txt.alignment = alignment;
                txt.enableWordWrapping = isMultiLine;
                txt.overflowMode = isMultiLine ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
                txt.raycastTarget = false;
                return go.transform;

            case "button":
                Image btnImg = go.AddComponent<Image>();
                btnImg.color = bgColor;
                Button btn = go.AddComponent<Button>();
                btn.targetGraphic = btnImg;

                GameObject btnTxtGo = CreateChildRect(go, "Text (TMP)", Vector2.zero, Vector2.one);
                TextMeshProUGUI btnTxt = btnTxtGo.AddComponent<TextMeshProUGUI>();
                SetTMPFont(btnTxt);
                btnTxt.text = nodeData.text;
                btnTxt.color = fontColor;
                btnTxt.fontSize = fontSize;
                btnTxt.alignment = alignment;
                btnTxt.enableWordWrapping = false;
                btnTxt.overflowMode = TextOverflowModes.Overflow;
                btnTxt.raycastTarget = false;
                return go.transform;

            case "input":
                Image inputBg = go.AddComponent<Image>();
                inputBg.color = bgColor;
                TMP_InputField inputField = go.AddComponent<TMP_InputField>();
                inputField.targetGraphic = inputBg;

                GameObject textAreaGo = CreateChildRect(go, "Text Area", Vector2.zero, Vector2.one, new Vector2(10, 5), new Vector2(-10, -5));
                textAreaGo.AddComponent<RectMask2D>();

                GameObject phGo = CreateChildRect(textAreaGo, "Placeholder", Vector2.zero, Vector2.one);
                TextMeshProUGUI phTxt = phGo.AddComponent<TextMeshProUGUI>();
                SetTMPFont(phTxt);
                phTxt.text = nodeData.text;
                Color phColor = fontColor;
                phColor.a = 0.5f;
                phTxt.color = phColor;
                phTxt.fontSize = fontSize;
                phTxt.alignment = alignment;
                phTxt.enableWordWrapping = false;
                phTxt.raycastTarget = false;

                GameObject textGo = CreateChildRect(textAreaGo, "Text", Vector2.zero, Vector2.one);
                TextMeshProUGUI inTxt = textGo.AddComponent<TextMeshProUGUI>();
                SetTMPFont(inTxt);
                inTxt.color = fontColor;
                inTxt.fontSize = fontSize;
                inTxt.alignment = alignment;
                inTxt.enableWordWrapping = false;
                inTxt.raycastTarget = false;

                inputField.textViewport = textAreaGo.GetComponent<RectTransform>();
                inputField.textComponent = inTxt;
                inputField.placeholder = phTxt;
                return go.transform;

            case "scroll":
                Image scrollBg = go.AddComponent<Image>();
                scrollBg.color = bgColor;
                if (scrollBg.color.a <= 0.01f) scrollBg.raycastTarget = false;

                ScrollRect scrollRect = go.AddComponent<ScrollRect>();
                bool isVertical = string.IsNullOrEmpty(nodeData.dir) || nodeData.dir.ToLower() == "v";
                scrollRect.horizontal = !isVertical;
                scrollRect.vertical = isVertical;

                GameObject viewportGo = CreateChildRect(go, "Viewport", Vector2.zero, Vector2.one);
                viewportGo.AddComponent<RectMask2D>();

                GameObject contentGo = CreateChildRect(viewportGo, "Content", new Vector2(0, 1), new Vector2(0, 1));
                RectTransform contentRect = contentGo.GetComponent<RectTransform>();
                contentRect.pivot = new Vector2(0, 1);
                contentRect.sizeDelta = new Vector2(nodeData.width, nodeData.height);

                scrollRect.viewport = viewportGo.GetComponent<RectTransform>();
                scrollRect.content = contentRect;
                return contentGo.transform;

            case "toggle":
                Toggle toggle = go.AddComponent<Toggle>();
                toggle.isOn = nodeData.isChecked;

                float boxSize = Mathf.Min(nodeData.height, 30f);
                GameObject tBgGo = CreateChildRect(go, "Background", new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                RectTransform tBgRect = tBgGo.GetComponent<RectTransform>();
                tBgRect.sizeDelta = new Vector2(boxSize, boxSize);
                tBgRect.anchoredPosition = new Vector2(boxSize / 2, 0);
                Image tBgImg = tBgGo.AddComponent<Image>();
                tBgImg.color = Color.white;

                GameObject checkGo = CreateChildRect(tBgGo, "Checkmark", Vector2.zero, Vector2.one);
                Image checkImg = checkGo.AddComponent<Image>();
                checkImg.color = Color.black;

                RectTransform checkRect = checkGo.GetComponent<RectTransform>();
                checkRect.offsetMin = new Vector2(4, 4);
                checkRect.offsetMax = new Vector2(-4, -4);

                GameObject tLblGo = CreateChildRect(go, "Label", Vector2.zero, Vector2.one);
                RectTransform tLblRect = tLblGo.GetComponent<RectTransform>();
                tLblRect.offsetMin = new Vector2(boxSize + 10, 0);
                TextMeshProUGUI tLblTxt = tLblGo.AddComponent<TextMeshProUGUI>();
                SetTMPFont(tLblTxt);
                tLblTxt.text = nodeData.text;
                tLblTxt.color = fontColor;
                tLblTxt.fontSize = fontSize;
                tLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                tLblTxt.enableWordWrapping = false;

                toggle.targetGraphic = tBgImg;
                toggle.graphic = checkImg;
                return go.transform;

            case "radio":
                // 单选框：与 toggle 外观一致的 Toggle，但使用圆形底图，
                // 且同一父容器下的所有 radio 自动归入同一个 ToggleGroup（互斥单选）。
                Toggle radio = go.AddComponent<Toggle>();

                Transform radioParent = go.transform.parent;
                if (radioParent != null)
                {
                    ToggleGroup radioGroup = radioParent.GetComponent<ToggleGroup>();
                    if (radioGroup == null) radioGroup = radioParent.gameObject.AddComponent<ToggleGroup>();
                    radio.group = radioGroup;
                    radioGroup.RegisterToggle(radio); // 编辑模式下再显式注册一次，确保互斥生效
                }
                radio.isOn = nodeData.isChecked;

                float radioSize = Mathf.Min(nodeData.height, 30f);
                GameObject rBgGo = CreateChildRect(go, "Background", new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                RectTransform rBgRect = rBgGo.GetComponent<RectTransform>();
                rBgRect.sizeDelta = new Vector2(radioSize, radioSize);
                rBgRect.anchoredPosition = new Vector2(radioSize / 2, 0);
                Image rBgImg = rBgGo.AddComponent<Image>();
                rBgImg.sprite = GetCircleSprite();
                rBgImg.color = Color.white;

                GameObject rCheckGo = CreateChildRect(rBgGo, "Checkmark", Vector2.zero, Vector2.one);
                Image rCheckImg = rCheckGo.AddComponent<Image>();
                rCheckImg.sprite = GetCircleSprite();
                rCheckImg.color = fontColor;
                RectTransform rCheckRect = rCheckGo.GetComponent<RectTransform>();
                rCheckRect.offsetMin = new Vector2(6, 6);
                rCheckRect.offsetMax = new Vector2(-6, -6);

                GameObject rLblGo = CreateChildRect(go, "Label", Vector2.zero, Vector2.one);
                RectTransform rLblRect = rLblGo.GetComponent<RectTransform>();
                rLblRect.offsetMin = new Vector2(radioSize + 10, 0);
                TextMeshProUGUI rLblTxt = rLblGo.AddComponent<TextMeshProUGUI>();
                SetTMPFont(rLblTxt);
                rLblTxt.text = nodeData.text;
                rLblTxt.color = fontColor;
                rLblTxt.fontSize = fontSize;
                rLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                rLblTxt.enableWordWrapping = false;

                radio.targetGraphic = rBgImg;
                radio.graphic = rCheckImg;
                return go.transform;

            case "slider":
                Slider slider = go.AddComponent<Slider>();
                slider.value = Mathf.Clamp01(nodeData.value);

                GameObject sBgGo = CreateChildRect(go, "Background", new Vector2(0, 0.25f), new Vector2(1, 0.75f));
                Image sBgImg = sBgGo.AddComponent<Image>();
                sBgImg.color = bgColor;

                GameObject fillAreaGo = CreateChildRect(go, "Fill Area", Vector2.zero, Vector2.one, new Vector2(5, 0), new Vector2(-15, 0));
                GameObject fillGo = CreateChildRect(fillAreaGo, "Fill", Vector2.zero, Vector2.one);
                Image fillImg = fillGo.AddComponent<Image>();
                fillImg.color = fontColor;

                GameObject handleAreaGo = CreateChildRect(go, "Handle Slide Area", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
                GameObject handleGo = CreateChildRect(handleAreaGo, "Handle", Vector2.zero, Vector2.one);
                RectTransform handleRect = handleGo.GetComponent<RectTransform>();
                handleRect.sizeDelta = new Vector2(20, 0);
                Image handleImg = handleGo.AddComponent<Image>();
                handleImg.color = Color.white;

                slider.targetGraphic = handleImg;
                slider.fillRect = fillGo.GetComponent<RectTransform>();
                slider.handleRect = handleRect;
                return go.transform;

            case "dropdown":
                Image dBgImg = go.AddComponent<Image>();
                dBgImg.color = bgColor;
                TMP_Dropdown dropdown = go.AddComponent<TMP_Dropdown>();

                GameObject dLblGo = CreateChildRect(go, "Label", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-30, 0));
                TextMeshProUGUI dLblTxt = dLblGo.AddComponent<TextMeshProUGUI>();
                SetTMPFont(dLblTxt);
                dLblTxt.color = fontColor;
                dLblTxt.fontSize = fontSize;
                dLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                dLblTxt.enableWordWrapping = false;

                GameObject arrowGo = CreateChildRect(go, "Arrow", new Vector2(1, 0.5f), new Vector2(1, 0.5f));
                RectTransform arrowRect = arrowGo.GetComponent<RectTransform>();
                arrowRect.sizeDelta = new Vector2(20, 20);
                arrowRect.anchoredPosition = new Vector2(-15, 0);
                Image arrowImg = arrowGo.AddComponent<Image>();
                arrowImg.color = fontColor;

                GameObject templateGo = CreateChildRect(go, "Template", new Vector2(0, 0), new Vector2(1, 0));
                RectTransform templateRect = templateGo.GetComponent<RectTransform>();
                templateRect.pivot = new Vector2(0.5f, 1);
                templateRect.sizeDelta = new Vector2(0, 150);
                templateRect.anchoredPosition = new Vector2(0, -2);
                Image tempImg = templateGo.AddComponent<Image>();
                tempImg.color = Color.white;
                ScrollRect tempScroll = templateGo.AddComponent<ScrollRect>();
                tempScroll.horizontal = false;
                tempScroll.vertical = true;
                templateGo.SetActive(false);

                GameObject dViewportGo = CreateChildRect(templateGo, "Viewport", Vector2.zero, Vector2.one);
                dViewportGo.AddComponent<Image>().color = Color.white;
                dViewportGo.AddComponent<Mask>();

                GameObject dContentGo = CreateChildRect(dViewportGo, "Content", new Vector2(0, 1), new Vector2(1, 1));
                RectTransform dContentRect = dContentGo.GetComponent<RectTransform>();
                dContentRect.pivot = new Vector2(0.5f, 1);
                dContentRect.sizeDelta = new Vector2(0, 28);

                GameObject itemGo = CreateChildRect(dContentGo, "Item", new Vector2(0, 0.5f), new Vector2(1, 0.5f));
                RectTransform itemRect = itemGo.GetComponent<RectTransform>();
                itemRect.sizeDelta = new Vector2(0, 28);
                Toggle itemToggle = itemGo.AddComponent<Toggle>();

                GameObject itemBgGo = CreateChildRect(itemGo, "Item Background", Vector2.zero, Vector2.one);
                Image itemBgImg = itemBgGo.AddComponent<Image>();
                itemBgImg.color = Color.white;

                GameObject itemCheckGo = CreateChildRect(itemGo, "Item Checkmark", new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                RectTransform itemCheckRect = itemCheckGo.GetComponent<RectTransform>();
                itemCheckRect.sizeDelta = new Vector2(20, 20);
                itemCheckRect.anchoredPosition = new Vector2(15, 0);
                Image itemCheckImg = itemCheckGo.AddComponent<Image>();
                itemCheckImg.color = Color.black;

                GameObject itemLblGo = CreateChildRect(itemGo, "Item Label", Vector2.zero, Vector2.one, new Vector2(30, 0), new Vector2(-10, 0));
                TextMeshProUGUI itemLblTxt = itemLblGo.AddComponent<TextMeshProUGUI>();
                SetTMPFont(itemLblTxt);
                itemLblTxt.color = Color.black;
                itemLblTxt.fontSize = fontSize;
                itemLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                itemLblTxt.enableWordWrapping = false;

                itemToggle.targetGraphic = itemBgImg;
                itemToggle.graphic = itemCheckImg;

                tempScroll.viewport = dViewportGo.GetComponent<RectTransform>();
                tempScroll.content = dContentRect;

                dropdown.targetGraphic = dBgImg;
                dropdown.template = templateRect;
                dropdown.captionText = dLblTxt;
                dropdown.itemText = itemLblTxt;

                if (nodeData.options != null && nodeData.options.Count > 0)
                {
                    dropdown.ClearOptions();
                    List<TMP_Dropdown.OptionData> optList = new List<TMP_Dropdown.OptionData>();
                    foreach (var opt in nodeData.options) optList.Add(new TMP_Dropdown.OptionData(opt));
                    dropdown.AddOptions(optList);
                }

                return go.transform;

            default:
                Debug.LogWarning($"[WebUI2UnityUI] 未知节点类型: {nodeData.type}");
                return go.transform;
        }
    }

    /// <summary>
    /// 若用户在烘焙前指定了覆盖字体，则应用到生成的 TMP_Text 组件；
    /// 未指定 (overrideFont 为空) 时不做任何处理，文本沿用项目默认字体。
    /// </summary>
    private void SetTMPFont(TextMeshProUGUI tmp)
    {
        if (overrideFont != null && tmp != null) tmp.font = overrideFont;
    }

    /// <summary>
    /// 单选框 / 开关用的圆形底图（Unity 内置 Knob 贴图），避免依赖外部美术资源。
    /// </summary>
    private static Sprite s_CircleSprite;
    private static Sprite GetCircleSprite()
    {
        if (s_CircleSprite == null)
            s_CircleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        return s_CircleSprite;
    }

    private TextAlignmentOptions ParseTextAlign(string alignStr)
    {
        if (string.IsNullOrEmpty(alignStr)) return TextAlignmentOptions.Midline;

        switch (alignStr.ToLower())
        {
            case "left":
            case "start":
                return TextAlignmentOptions.MidlineLeft;
            case "right":
            case "end":
                return TextAlignmentOptions.MidlineRight;
            case "center":
            default:
                return TextAlignmentOptions.Midline;
        }
    }

    private GameObject CreateChildRect(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2? offsetMin = null, Vector2? offsetMax = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) go.layer = uiLayer;
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin ?? Vector2.zero;
        rect.offsetMax = offsetMax ?? Vector2.zero;
        return go;
    }

    private Color ParseHexColor(string hex, Color defaultColor)
    {
        if (string.IsNullOrEmpty(hex)) return defaultColor;
        if (ColorUtility.TryParseHtmlString(hex, out Color color)) return color;
        return defaultColor;
    }
}
