using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Supabase 保活工具：读取 StreamingAssets 配置，依次登录多个数据库，避免长期未使用被冻结。
/// </summary>
public class SupabaseKeepAliveTool : MonoBehaviour
{
    [Header("Config")]
    public string configFileName = "supabase_keepalive.json";
    public bool runOnStart;
    public bool logVerbose = true;
    public bool showGuiText = true;
    [Min(10)] public int guiFontSize = 32;
    public bool showGuiLogs = true;
    [Min(1)] public int guiMaxLogs = 30;
    public string keepAliveTableName = "test";
    public bool showGuiEditor = true;

    [Header("Config Editor")]
    public string editEmail;
    public string editPassword;
    [Min(1)] public int editTimeoutSec = 8;
    [Min(0f)] public float editIntervalSeconds = 0.2f;
    public List<SupabaseKeepAliveTarget> editTargets = new List<SupabaseKeepAliveTarget>();

    [ContextMenu("保存配置")]
    public void SaveConfig()
    {
        var config = new SupabaseKeepAliveConfig
        {
            email = editEmail,
            password = editPassword,
            timeoutSec = editTimeoutSec,
            intervalSeconds = editIntervalSeconds,
            targets = editTargets.ToArray()
        };

        string json = JsonUtility.ToJson(config, true);
        string path = GetWritableConfigPath();
        string dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, json, Encoding.UTF8);
        Debug.Log($"[SupabaseKeepAlive] 配置已保存到: {path}");
        AddGuiLog($"配置已保存: {path}");
#if !UNITY_EDITOR
        AddGuiLog($"运行时使用可写目录: {Application.persistentDataPath}");
#endif
    }

    [ContextMenu("读取配置")]
    public void LoadConfigToEditor()
    {
        string writablePath = GetWritableConfigPath();
        if (File.Exists(writablePath))
        {
            string json = NormalizeJsonText(File.ReadAllText(writablePath, Encoding.UTF8));
            if (TryApplyConfigToEditor(json, $"编辑器加载({writablePath})"))
            {
                Debug.Log($"[SupabaseKeepAlive] 配置已读取: {writablePath}");
                AddGuiLog($"配置已读取: {writablePath}");
            }
            return;
        }

#if UNITY_EDITOR
        string streamingPath = Path.Combine(Application.streamingAssetsPath, configFileName);
        if (!File.Exists(streamingPath))
        {
            Debug.LogWarning($"[SupabaseKeepAlive] 配置文件不存在: {writablePath} (fallback: {streamingPath})");
            AddGuiLog($"配置文件不存在: {writablePath}");
            return;
        }

        string editorJson = NormalizeJsonText(File.ReadAllText(streamingPath, Encoding.UTF8));
        if (TryApplyConfigToEditor(editorJson, $"编辑器加载({streamingPath})"))
        {
            Debug.Log($"[SupabaseKeepAlive] 配置已读取: {streamingPath}");
            AddGuiLog($"配置已读取: {streamingPath}");
        }
#else
        StartCoroutine(LoadConfigToEditorFromStreamingAssets());
#endif
    }

    [Header("Runtime")]
    public bool isRunning;
    public int successCount;
    public int failCount;

    private readonly List<string> guiLogs = new List<string>();
    private Vector2 guiLogScroll;
    private Vector2 guiEditorScroll;
    private bool guiEditorFoldout = true;

    // 用户手动调节的字体缩放，1.0 = 默认，范围 0.5 ~ 2.5
    [HideInInspector] public float guiUserScale = 1.0f;

    private void Start()
    {
        LoadConfigToEditor();

        if (runOnStart)
            StartRun();
    }

    private Vector2 mainScrollPos;

    private void OnGUI()
    {
        if (!showGuiText)
            return;

        float screenScale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        screenScale = Mathf.Max(screenScale, 0.5f);

        // 用户手动缩放 x 屏幕自适应缩放 = 最终缩放
        guiUserScale = Mathf.Clamp(guiUserScale, 0.5f, 2.5f);
        float scale = screenScale * guiUserScale;

        int margin   = Mathf.RoundToInt(20 * scale);
        int width    = Screen.width - margin * 2;
        int fontSize = Mathf.Max(Mathf.RoundToInt(guiFontSize * scale), 18);
        int titleFontSize = Mathf.Max(Mathf.RoundToInt((guiFontSize + 6) * scale), 22);

        // ── 外层区域 ──
        Rect outerArea = new Rect(margin, margin, width, Screen.height - margin * 2);

        // 半透明背景
        Color bgColor = new Color(0.08f, 0.08f, 0.12f, 0.88f);
        Texture2D bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, bgColor);
        bgTex.Apply();
        var bgStyle = new GUIStyle();
        bgStyle.normal.background = bgTex;
        GUI.Box(outerArea, "", bgStyle);
        Destroy(bgTex);

        GUILayout.BeginArea(outerArea);
        mainScrollPos = GUILayout.BeginScrollView(mainScrollPos, GUIStyle.none, GUIStyle.none);

        // ═══════════════════════════════════════════
        // 标题栏 + 缩放按钮
        // ═══════════════════════════════════════════
        GUILayout.BeginHorizontal();
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = titleFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            padding = new RectOffset(Mathf.RoundToInt(16 * scale), 0, 0, 0),
            clipping = TextClipping.Overflow
        };
        GUILayout.Label("⚡ Supabase KeepAlive", titleStyle);
        GUILayout.FlexibleSpace();

        // 缩放按钮
        int zoomBtnW = Mathf.RoundToInt(44 * scale);
        int zoomBtnH = Mathf.RoundToInt(36 * scale);
        int zoomFontSize = Mathf.Max(Mathf.RoundToInt(guiFontSize * scale), 18);
        var zoomBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = zoomFontSize,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };
        GUI.backgroundColor = new Color(0.25f, 0.25f, 0.35f);
        if (GUILayout.Button("−", zoomBtnStyle, GUILayout.Width(zoomBtnW), GUILayout.Height(zoomBtnH)))
            guiUserScale -= 0.1f;
        GUI.backgroundColor = new Color(0.15f, 0.15f, 0.22f);
        var zoomLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(zoomFontSize - 2, 12),
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.7f, 0.7f, 0.75f) }
        };
        GUILayout.Label($"{guiUserScale:F1}x", zoomLabelStyle, GUILayout.Width(Mathf.RoundToInt(56 * scale)), GUILayout.Height(zoomBtnH));
        GUI.backgroundColor = new Color(0.25f, 0.25f, 0.35f);
        if (GUILayout.Button("＋", zoomBtnStyle, GUILayout.Width(zoomBtnW), GUILayout.Height(zoomBtnH)))
            guiUserScale += 0.1f;
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.Space(Mathf.RoundToInt(10 * scale));

        // ═══════════════════════════════════════════
        // 状态卡片
        // ═══════════════════════════════════════════
        DrawStatusCard(scale, fontSize);

        GUILayout.Space(Mathf.RoundToInt(12 * scale));

        // ═══════════════════════════════════════════
        // 日志区域
        // ═══════════════════════════════════════════
        if (showGuiLogs)
        {
            DrawLogSection(scale, fontSize);
            GUILayout.Space(Mathf.RoundToInt(12 * scale));
        }

        // ═══════════════════════════════════════════
        // Config Editor
        // ═══════════════════════════════════════════
        if (showGuiEditor)
            DrawRuntimeConfigEditor(scale);

        // 底部留白
        GUILayout.Space(Mathf.RoundToInt(40 * scale));

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawStatusCard(float scale, int fontSize)
    {
        int rowH     = Mathf.RoundToInt(40 * scale);
        // 行高至少要能容纳文字：字号 + 上下各留 4px 余量
        rowH = Mathf.Max(rowH, fontSize + 8);

        string status = isRunning ? "● Running" : "○ Idle";
        Color statusColor = isRunning ? new Color(0.3f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);

        var valStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            contentOffset = new Vector2(0, -2 * scale),
            normal = { textColor = statusColor }
        };
        var keyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(fontSize - 4, 14),
            alignment = TextAnchor.MiddleLeft,
            contentOffset = new Vector2(0, -1 * scale),
            normal = { textColor = new Color(0.65f, 0.65f, 0.7f) }
        };
        var numStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 4,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            contentOffset = new Vector2(0, -2 * scale),
            normal = { textColor = Color.white }
        };

        // 状态卡片高度：顶部留白 + 两行文字 + 底部留白，确保不遮挡
        int cardHeight = Mathf.RoundToInt(8 * scale) + rowH * 2 + Mathf.RoundToInt(16 * scale);
        GUILayout.BeginVertical("box", GUILayout.Height(cardHeight));
        GUILayout.Space(Mathf.RoundToInt(8 * scale));
        GUILayout.BeginHorizontal();

        // 状态
        GUILayout.BeginVertical();
        GUILayout.Label("状态", keyStyle);
        GUILayout.Label(status, valStyle);
        GUILayout.EndVertical();

        GUILayout.Space(Mathf.RoundToInt(40 * scale));

        // 成功 / 失败
        GUILayout.BeginVertical();
        GUILayout.Label("成功 / 失败", keyStyle);
        GUILayout.BeginHorizontal();
        var succStyle = new GUIStyle(numStyle) { normal = { textColor = new Color(0.3f, 1f, 0.4f) } };
        var failStyle = new GUIStyle(numStyle) { normal = { textColor = new Color(1f, 0.35f, 0.35f) } };
        GUILayout.Label(successCount.ToString(), succStyle, GUILayout.Width(Mathf.RoundToInt(50 * scale)));
        GUILayout.Label("/", numStyle, GUILayout.Width(Mathf.RoundToInt(20 * scale)));
        GUILayout.Label(failCount.ToString(), failStyle, GUILayout.Width(Mathf.RoundToInt(50 * scale)));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.Space(Mathf.RoundToInt(40 * scale));

        // 配置名
        GUILayout.BeginVertical();
        GUILayout.Label("配置", keyStyle);
        GUILayout.Label(configFileName, valStyle);
        GUILayout.EndVertical();

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void DrawLogSection(float scale, int fontSize)
    {
        var logTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.75f, 0.75f, 0.8f) }
        };
        GUILayout.Label($"📋 日志 ({guiLogs.Count})", logTitleStyle);

        int logFontSize   = Mathf.Max(Mathf.RoundToInt((guiFontSize - 6) * scale), 12);
        // 行高 = 字号 + 上下各留 6px，确保文字不被裁切
        int logLineHeight = Mathf.Max(logFontSize + 12, Mathf.RoundToInt(28 * scale));

        var logStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = logFontSize,
            wordWrap = false,
            alignment = TextAnchor.MiddleLeft,
            contentOffset = new Vector2(0, -1 * scale),
            normal = { textColor = new Color(0.75f, 0.78f, 0.82f) }
        };

        // 日志高度自适应：内容行数 × 行高，上限为屏幕高度 50%，下限 3 行
        int logMinLines = Mathf.Max(3, guiLogs.Count);
        int logContentH = logMinLines * logLineHeight + Mathf.RoundToInt(8 * scale);
        int logMaxH     = Mathf.RoundToInt(Screen.height * 0.5f);
        int logViewH    = Mathf.Clamp(logContentH, logLineHeight * 3, logMaxH);
        guiLogScroll = GUILayout.BeginScrollView(guiLogScroll, "box", GUILayout.Height(logViewH));
        GUILayout.BeginVertical();
        for (int i = 0; i < guiLogs.Count; i++)
        {
            GUILayout.Label(guiLogs[i], logStyle, GUILayout.Height(logLineHeight));
        }
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
    }

    private void DrawRuntimeConfigEditor(float scale)
    {
        int fs      = Mathf.Max(Mathf.RoundToInt((guiFontSize - 4) * scale), 14);
        int fsBold  = Mathf.Max(Mathf.RoundToInt(guiFontSize * scale), 18);
        // 行高至少要能容纳文字：字号 + 上下各留 6px 余量
        int rowH    = Mathf.Max(Mathf.Max(Mathf.RoundToInt(36 * scale), 24), fs + 12);
        int labelW  = Mathf.RoundToInt(140 * scale);
        int btnH    = Mathf.Max(Mathf.RoundToInt(34 * scale), fs + 10);

        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = fsBold, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.85f, 0.9f) }, alignment = TextAnchor.MiddleLeft, contentOffset = new Vector2(0, -2 * scale) };
        var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleLeft, contentOffset = new Vector2(0, -1 * scale), normal = { textColor = new Color(0.7f, 0.7f, 0.75f) } };
        var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = fs, padding = new RectOffset(8, 8, Mathf.Max(6, fs / 5), Mathf.Max(6, fs / 5)) };
        var btnStyle   = new GUIStyle(GUI.skin.button)  { fontSize = fs, padding = new RectOffset(12, 12, Mathf.Max(6, fs / 5), Mathf.Max(6, fs / 5)) };
        var sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = fs + 2, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, contentOffset = new Vector2(0, -1 * scale), normal = { textColor = new Color(0.9f, 0.7f, 0.3f) } };

        // 折叠标题
        if (GUILayout.Button((guiEditorFoldout ? "▼ " : "► ") + "Config Editor", titleStyle, GUILayout.Height(rowH)))
            guiEditorFoldout = !guiEditorFoldout;

        if (!guiEditorFoldout)
            return;

        GUILayout.Space(Mathf.RoundToInt(8 * scale));

        // ── 凭据区 ──
        GUILayout.Label("🔑 凭据", sectionStyle);
        GUILayout.Space(Mathf.RoundToInt(4 * scale));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Email:", labelStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
        editEmail = GUILayout.TextField(editEmail ?? "", fieldStyle, GUILayout.Height(rowH));
        GUILayout.EndHorizontal();
        GUILayout.Space(Mathf.RoundToInt(4 * scale));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Password:", labelStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
        editPassword = GUILayout.PasswordField(editPassword ?? "", '*', fieldStyle, GUILayout.Height(rowH));
        GUILayout.EndHorizontal();
        GUILayout.Space(Mathf.RoundToInt(4 * scale));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Timeout(s):", labelStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
        string timeoutStr = GUILayout.TextField(editTimeoutSec.ToString(), fieldStyle, GUILayout.Width(Mathf.RoundToInt(120 * scale)), GUILayout.Height(rowH));
        if (int.TryParse(timeoutStr, out int t) && t >= 1) editTimeoutSec = t;
        GUILayout.Space(Mathf.RoundToInt(16 * scale));
        GUILayout.Label("Interval(s):", labelStyle, GUILayout.Height(rowH));
        string intervalStr = GUILayout.TextField(editIntervalSeconds.ToString("F2"), fieldStyle, GUILayout.Width(Mathf.RoundToInt(120 * scale)), GUILayout.Height(rowH));
        if (float.TryParse(intervalStr, out float iv) && iv >= 0f) editIntervalSeconds = iv;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Mathf.RoundToInt(12 * scale));

        // ── Targets 区 ──
        int targetCount = editTargets != null ? editTargets.Count : 0;
        GUILayout.Label($"🎯 Targets ({targetCount})", sectionStyle);
        GUILayout.Space(Mathf.RoundToInt(4 * scale));

        if (editTargets == null) editTargets = new List<SupabaseKeepAliveTarget>();

        int removeIdx = -1;
        // Targets 高度自适应：每个 target 占 4 行（标题+Name+URL+ApiKey）+ 行间距 + 盒子内边距，上限屏幕 40%
        int targetItemH = rowH * 4 + Mathf.RoundToInt(42 * scale);
        int targetContentH = targetCount > 0 ? targetCount * targetItemH + Mathf.RoundToInt(16 * scale) : Mathf.RoundToInt(60 * scale);
        int targetMaxH  = Mathf.RoundToInt(Screen.height * 0.4f);
        int targetViewH = Mathf.Clamp(targetContentH, Mathf.RoundToInt(60 * scale), targetMaxH);
        guiEditorScroll = GUILayout.BeginScrollView(guiEditorScroll, "box", GUILayout.Height(targetViewH));

        for (int i = 0; i < editTargets.Count; i++)
        {
            var tgt = editTargets[i];
            if (tgt == null) { editTargets[i] = new SupabaseKeepAliveTarget(); tgt = editTargets[i]; }

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Target {i}", labelStyle, GUILayout.Height(rowH));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕ 删除", btnStyle, GUILayout.Width(Mathf.RoundToInt(80 * scale)), GUILayout.Height(rowH)))
                removeIdx = i;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", labelStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
            tgt.name = GUILayout.TextField(tgt.name ?? "", fieldStyle, GUILayout.Height(rowH));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("URL:", labelStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
            tgt.url = GUILayout.TextField(tgt.url ?? "", fieldStyle, GUILayout.Height(rowH));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ApiKey:", labelStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
            tgt.apikey = GUILayout.TextField(tgt.apikey ?? "", fieldStyle, GUILayout.Height(rowH));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(Mathf.RoundToInt(6 * scale));
        }

        GUILayout.EndScrollView();

        if (removeIdx >= 0)
            editTargets.RemoveAt(removeIdx);

        GUILayout.Space(Mathf.RoundToInt(6 * scale));

        // ── 按钮区 ──
        if (GUILayout.Button("＋ 添加 Target", btnStyle, GUILayout.Height(btnH)))
            editTargets.Add(new SupabaseKeepAliveTarget());

        GUILayout.Space(Mathf.RoundToInt(8 * scale));

        GUILayout.BeginHorizontal();
        Color originalBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.35f);
        if (GUILayout.Button("💾 保存配置", btnStyle, GUILayout.Height(btnH)))
            SaveConfig();
        GUI.backgroundColor = new Color(0.25f, 0.5f, 0.8f);
        if (GUILayout.Button("📂 读取配置", btnStyle, GUILayout.Height(btnH)))
            LoadConfigToEditor();
        GUI.backgroundColor = originalBg;
        GUILayout.EndHorizontal();
    }

    [ContextMenu("Run KeepAlive")]
    public void StartRun()
    {
        if (isRunning)
        {
            AddGuiLog("任务已在运行中，忽略重复启动");
            return;
        }

        StartCoroutine(RunKeepAlive());
    }

    public IEnumerator RunKeepAlive()
    {
        isRunning = true;
        successCount = 0;
        failCount = 0;
        guiLogs.Clear();
        AddGuiLog("开始执行保活任务");

        string jsonText = null;
        string readErr = null;

        if (TryReadPersistentConfigText(out jsonText, out readErr))
        {
            AddGuiLog($"读取本地配置成功: {GetPersistentConfigPath()}");
        }
        else
        {
            if (!string.IsNullOrEmpty(readErr))
            {
                Debug.LogWarning($"[SupabaseKeepAlive] 读取本地配置失败，回退 StreamingAssets: {readErr}");
                AddGuiLog("本地配置读取失败，已回退 StreamingAssets");
            }

            readErr = null;
            yield return ReadConfigText(configFileName, text => jsonText = text, err => readErr = err);

            if (!string.IsNullOrEmpty(readErr))
            {
                Debug.LogError($"[SupabaseKeepAlive] 读取配置失败: {readErr}");
                AddGuiLog($"读取配置失败: {readErr}");
                isRunning = false;
                yield break;
            }
        }

        jsonText = NormalizeJsonText(jsonText);

        SupabaseKeepAliveConfig config;
        try
        {
            config = JsonUtility.FromJson<SupabaseKeepAliveConfig>(jsonText);
        }
        catch (Exception e)
        {
            LogConfigParseError("运行时解析", jsonText, e);
            isRunning = false;
            yield break;
        }

        if (config == null || config.targets == null || config.targets.Length == 0)
        {
            Debug.LogError("[SupabaseKeepAlive] 配置为空，请检查 StreamingAssets/supabase_keepalive.json");
            AddGuiLog("配置为空，终止执行");
            isRunning = false;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(config.email) || string.IsNullOrWhiteSpace(config.password))
        {
            Debug.LogError("[SupabaseKeepAlive] email/password 不能为空");
            AddGuiLog("email/password 不能为空");
            isRunning = false;
            yield break;
        }

        for (int i = 0; i < config.targets.Length; i++)
        {
            var target = config.targets[i];
            if (target == null || string.IsNullOrWhiteSpace(target.url) || string.IsNullOrWhiteSpace(target.apikey))
            {
                failCount++;
                Debug.LogWarning($"[SupabaseKeepAlive] 跳过无效 target index={i}");
                AddGuiLog($"跳过无效 target index={i}");
                continue;
            }

            string accessToken = null;
            string signErr = null;
            yield return SignInOnce(target, config.email, config.password, config.timeoutSec, token => accessToken = token, e => signErr = e);

            if (string.IsNullOrEmpty(accessToken))
            {
                failCount++;
                string signFailMsg = $"登录失败 -> {GetTargetName(target, i)} | {signErr}";
                Debug.LogWarning($"[SupabaseKeepAlive] {signFailMsg}");
                AddGuiLog(signFailMsg);
                continue;
            }

            bool writeOk = false;
            string writeErr = null;
            yield return InsertKeepAliveRow(target, accessToken, config.timeoutSec, b => writeOk = b, e => writeErr = e);

            if (writeOk)
            {
                successCount++;
                string okMsg = $"保活写入成功 -> {GetTargetName(target, i)}";
                AddGuiLog(okMsg);
                if (logVerbose)
                    Debug.Log($"[SupabaseKeepAlive] {okMsg}");
            }
            else
            {
                failCount++;
                string failMsg = $"保活写入失败 -> {GetTargetName(target, i)} | {writeErr}";
                Debug.LogWarning($"[SupabaseKeepAlive] {failMsg}");
                AddGuiLog(failMsg);
            }

            if (config.intervalSeconds > 0f)
                yield return new WaitForSeconds(config.intervalSeconds);
        }

        Debug.Log($"[SupabaseKeepAlive] 完成 success={successCount}, fail={failCount}");
        AddGuiLog($"执行完成 success={successCount}, fail={failCount}");
        isRunning = false;
    }

    private IEnumerator ReadConfigText(string fileName, Action<string> onSuccess, Action<string> onError)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        string uri = BuildStreamingAssetsUri(path);

        using (var request = UnityWebRequest.Get(uri))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
            else
            {
                onError?.Invoke($"{request.error} | {request.downloadHandler?.text}");
            }
        }
    }

    private static string BuildStreamingAssetsUri(string path)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return "file://" + path.Replace("\\", "/");
    }

    private IEnumerator LoadConfigToEditorFromStreamingAssets()
    {
        string json = null;
        string err = null;

        yield return ReadConfigText(configFileName, text => json = text, e => err = e);

        if (!string.IsNullOrEmpty(err))
        {
            Debug.LogWarning($"[SupabaseKeepAlive] 配置文件不存在或读取失败: {GetPersistentConfigPath()} (fallback StreamingAssets失败: {err})");
            AddGuiLog("读取配置失败: StreamingAssets 不可用");
            yield break;
        }

        json = NormalizeJsonText(json);
        if (TryApplyConfigToEditor(json, "运行时加载(StreamingAssets)"))
        {
            string streamingPath = Path.Combine(Application.streamingAssetsPath, configFileName);
            Debug.Log($"[SupabaseKeepAlive] 配置已读取: {streamingPath}");
            AddGuiLog($"配置已读取: {streamingPath}");
        }
    }

    private IEnumerator SignInOnce(SupabaseKeepAliveTarget target, string email, string password, int timeoutSec, Action<string> onToken, Action<string> onError)
    {
        string url = target.url.TrimEnd('/') + "/auth/v1/token?grant_type=password";
        string body = JsonUtility.ToJson(new SupabaseSignInBody
        {
            email = email,
            password = password
        });

        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.timeout = Mathf.Max(3, timeoutSec);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", target.apikey);
            request.SetRequestHeader("Authorization", "Bearer " + target.apikey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                SupabaseSignInResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<SupabaseSignInResponse>(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"解析登录响应失败: {e.Message}");
                }

                if (!string.IsNullOrEmpty(response != null ? response.access_token : null))
                {
                    onToken?.Invoke(response.access_token);
                }
                else
                {
                    onError?.Invoke("响应里缺少 access_token");
                }
            }
            else
            {
                onError?.Invoke($"{request.error} | {request.downloadHandler?.text}");
            }
        }
    }

    private IEnumerator InsertKeepAliveRow(SupabaseKeepAliveTarget target, string accessToken, int timeoutSec, Action<bool> onDone, Action<string> onError)
    {
        string tableName = string.IsNullOrWhiteSpace(keepAliveTableName) ? "test" : keepAliveTableName.Trim();
        string url = target.url.TrimEnd('/') + "/rest/v1/" + tableName;

        string utcNow = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string body = JsonUtility.ToJson(new SupabaseKeepAliveRow
        {
            created_at = utcNow,
            change_time = utcNow
        });

        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.timeout = Mathf.Max(3, timeoutSec);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Prefer", "return=minimal");
            request.SetRequestHeader("apikey", target.apikey);
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onDone?.Invoke(true);
            }
            else
            {
                onDone?.Invoke(false);
                onError?.Invoke($"{request.error} | {request.downloadHandler?.text}");
            }
        }
    }

    private void AddGuiLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        guiLogs.Add(line);

        if (guiLogs.Count > guiMaxLogs)
            guiLogs.RemoveAt(0);

        guiLogScroll = new Vector2(0f, float.MaxValue);
    }

    private string GetPersistentConfigPath()
    {
        return Path.Combine(Application.persistentDataPath, configFileName);
    }

    private string GetWritableConfigPath()
    {
#if UNITY_EDITOR
        return Path.Combine(Application.streamingAssetsPath, configFileName);
#else
        return GetPersistentConfigPath();
#endif
    }

    private bool TryReadPersistentConfigText(out string text, out string error)
    {
        text = null;
        error = null;

        string path = GetPersistentConfigPath();
        if (!File.Exists(path))
            return false;

        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }
        catch (Exception e)
        {
            error = $"{path} | {e.Message}";
            return false;
        }
    }

    private static string GetTargetName(SupabaseKeepAliveTarget target, int index)
    {
        return string.IsNullOrWhiteSpace(target.name) ? $"target_{index}" : target.name;
    }

    private void LogConfigParseError(string source, string jsonText, Exception e)
    {
        string preview = BuildJsonPreview(jsonText, 400);
        string err = $"[SupabaseKeepAlive] 解析配置失败 ({source}): {e.GetType().Name} | {e.Message}\nJSON预览: {preview}";
        Debug.LogError(err);
        AddGuiLog($"解析配置失败({source}): {e.Message}");
    }

    private static string BuildJsonPreview(string jsonText, int maxLen)
    {
        if (string.IsNullOrEmpty(jsonText))
            return "<empty>";

        string compact = jsonText.Replace("\r", " ").Replace("\n", " ").Trim();
        if (compact.Length <= maxLen)
            return compact;

        return compact.Substring(0, maxLen) + "...";
    }

    private static string NormalizeJsonText(string jsonText)
    {
        if (string.IsNullOrEmpty(jsonText))
            return jsonText;

        int i = 0;
        while (i < jsonText.Length)
        {
            char c = jsonText[i];
            if (c == '\uFEFF' || c == '\u200B' || c == '\u2060' || char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            break;
        }

        return i > 0 ? jsonText.Substring(i) : jsonText;
    }

    private bool TryApplyConfigToEditor(string json, string source)
    {
        SupabaseKeepAliveConfig config;
        try
        {
            config = JsonUtility.FromJson<SupabaseKeepAliveConfig>(json);
        }
        catch (Exception e)
        {
            LogConfigParseError(source, json, e);
            return false;
        }

        if (config == null)
        {
            AddGuiLog($"解析配置失败({source}): 结果为空");
            Debug.LogError($"[SupabaseKeepAlive] 解析配置失败 ({source}): 结果为空");
            return false;
        }

        editEmail = config.email;
        editPassword = config.password;
        editTimeoutSec = config.timeoutSec;
        editIntervalSeconds = config.intervalSeconds;
        editTargets = config.targets != null
            ? new List<SupabaseKeepAliveTarget>(config.targets)
            : new List<SupabaseKeepAliveTarget>();

        return true;
    }
}

[Serializable]
public class SupabaseKeepAliveConfig
{
    public string email;
    public string password;
    public int timeoutSec = 8;
    public float intervalSeconds = 0.2f;
    public SupabaseKeepAliveTarget[] targets;
}

[Serializable]
public class SupabaseKeepAliveTarget
{
    public string name;
    public string url;
    public string apikey;
}

[Serializable]
public class SupabaseSignInBody
{
    public string email;
    public string password;
}

[Serializable]
public class SupabaseSignInResponse
{
    public string access_token;
}

[Serializable]
public class SupabaseKeepAliveRow
{
    public string created_at;
    public string change_time;
}
