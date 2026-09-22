using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// JoyVeyor v1.0 — Sprint 5 menu flow (jv-design-uxui §1 Screen Flow + §2
// Wireframes). One scene, one overlay canvas above the in-game HUD.
//
//   boot ──> main menu ──> level select ──> in-game
//                │              │
//                └── How to Play (static, back)
//
// MenuFlow owns the chrome (boot / main menu / level select / how-to-play) on
// its own Screen Space–Camera canvas (sortingOrder 200, above the HUD's 100).
// The in-game HUD + world are GameScreen's; MenuFlow just hides its canvas to
// reveal the game and calls gameScreen.StartLevel(n). No dead ends: every
// overlay has a path back. Level select is the hub; "Next" advances without
// re-entering select. Progress (unlocks, stars) comes from ProgressStore.
//
// Added by the game boot / MenuTest probe to the JoyveyorRunner object:
//   runner.AddComponent<MenuFlow>();
public class MenuFlow : MonoBehaviour
{
    public enum Screen { Boot, MainMenu, LevelSelect, InGame }

    const float BootSeconds = 3f;
    const int TotalLevels = ProgressStore.TotalLevels;

    public Screen Current = Screen.Boot;
    public bool HowToPlayOpen;

    // ---- chrome refs (public for the headless probe) ----
    public Canvas menuCanvas;
    public GameObject bootScreen;
    public GameObject mainMenu;
    public GameObject levelSelect;
    public GameObject howToPlay;
    public Text versionTag;
    public Button playButton;
    public Button[] levelButtons;      // [level-1]
    public Image[] levelStarImages;    // [level-1][0..2]
    public Image[] levelLockImages;    // [level-1]
    public Text[] levelNumberTexts;    // [level-1]

    GameScreen gameScreen;
    ProgressStore progressFallback;
    int textScaleIndex = 1;            // 0=S 1=M 2=L (persisted)
    float bootTimer;
    Image bootBar;

    // Always prefer the live session's ProgressStore (the one GameSession
    // records wins into) so unlocks/stars stay fresh after a win. Falls back
    // to a loaded copy only before a session exists.
    ProgressStore Progress
    {
        get
        {
            if (gameScreen != null && gameScreen.session != null)
                return gameScreen.session.Progress;
            if (progressFallback == null) progressFallback = ProgressStore.Load();
            return progressFallback;
        }
    }

    static string SettingsPath =>
        System.IO.Path.Combine(Application.persistentDataPath, "joyveyor_settings.json");

    // ---- lifecycle ----

    void Start()
    {
        try
        {
            if (gameScreen == null) gameScreen = GetComponent<GameScreen>();
            if (gameScreen == null) gameScreen = gameObject.AddComponent<GameScreen>();
            gameScreen.OnNextLevel += HandleNextLevel;
            gameScreen.OnQuitToMenu += HandleQuitToMenu;

            LoadSettings();
            BuildCanvas();
            BuildBoot();
            BuildMainMenu();
            BuildLevelSelect();
            BuildHowToPlay();
            ShowBoot();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[MenuFlow] Start failed: " + e);
        }
    }

    void OnDestroy()
    {
        if (gameScreen != null)
        {
            gameScreen.OnNextLevel -= HandleNextLevel;
            gameScreen.OnQuitToMenu -= HandleQuitToMenu;
        }
    }

    void Update()
    {
        if (Current == Screen.Boot)
        {
            bootTimer += Time.unscaledDeltaTime;
            if (bootBar != null)
                bootBar.rectTransform.localScale = new Vector3(
                    Mathf.Clamp01(bootTimer / BootSeconds), 1f, 1f);
            if (bootTimer >= BootSeconds) ShowMainMenu();
        }
    }

    // ---- state transitions (public for the probe) ----

    public void ShowBoot()
    {
        Current = Screen.Boot;
        bootTimer = 0f;
        SetScreen(bootScreen);
    }

    public void ShowMainMenu()
    {
        Current = Screen.MainMenu;
        SetScreen(mainMenu);
    }

    public void ShowLevelSelect()
    {
        Current = Screen.LevelSelect;
        RefreshLevelSelect();
        SetScreen(levelSelect);
    }

    // Enter in-game for a level: reveal the game, hide the menu chrome.
    public void StartLevel(int level)
    {
        if (Progress != null && !Progress.IsUnlocked(level))
        {
            Debug.LogWarning("[MenuFlow] level " + level + " is locked");
            return;
        }
        gameScreen.SetTextScale(textScaleIndex);
        gameScreen.StartLevel(level);
        Current = Screen.InGame;
        menuCanvas.gameObject.SetActive(false);
    }

    public void ShowHowToPlay()
    {
        HowToPlayOpen = true;
        howToPlay.SetActive(true);
    }

    public void HideHowToPlay()
    {
        HowToPlayOpen = false;
        howToPlay.SetActive(false);
    }

    // Public so the headless probe can drive "Next" (the complete overlay's
    // Next button fires OnNextLevel, which MenuFlow routes here).
    public void HandleNextLevel()
    {
        if (gameScreen == null || gameScreen.session == null) return;
        int next = gameScreen.session.LevelIndex + 1;
        if (Progress != null && next <= TotalLevels && Progress.IsUnlocked(next))
            StartLevel(next);          // advance without re-entering select
        else
            ShowLevelSelect();         // last level / locked -> back to hub
    }

    void HandleQuitToMenu()
    {
        ShowLevelSelect();             // level select is the hub
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    // ---- internals ----

    void SetScreen(GameObject go)
    {
        menuCanvas.gameObject.SetActive(true);
        if (bootScreen != null) bootScreen.SetActive(go == bootScreen);
        if (mainMenu != null) mainMenu.SetActive(go == mainMenu);
        if (levelSelect != null) levelSelect.SetActive(go == levelSelect);
        if (howToPlay != null) howToPlay.SetActive(HowToPlayOpen);
    }

    void BuildCanvas()
    {
        var cam = Camera.main ?? FindObjectOfType<Camera>();
        if (cam == null)
        {
            var cgo = new GameObject("Main Camera");
            cgo.tag = "MainCamera";
            cam = cgo.AddComponent<Camera>();
        }
        var go = new GameObject("MenuCanvas");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.sortingOrder = 200;     // above the HUD canvas (100)
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        go.AddComponent<GraphicRaycaster>();
        menuCanvas = canvas;

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    void BuildBoot()
    {
        bootScreen = FullScreen(bootScreen, "BootScreen", new Color(0.07f, 0.065f, 0.06f, 1f));
        var title = MakeText(bootScreen.transform, "Title", "JOYVEYOR", 72, TextAnchor.MiddleCenter);
        Center(title.rectTransform, 0, 60, 600, 90);
        // Loading bar: a track + a fill that scales 0..1 over BootSeconds.
        var track = MakeImage(bootScreen.transform, "Track", new Color(0.2f, 0.2f, 0.24f, 1f));
        Center(track.GetComponent<RectTransform>(), 0, -20, 420, 22);
        var fill = MakeImage(track.transform, "Fill", new Color(1f, 0.84f, 0.2f, 1f));
        var frt = fill.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0, 1);
        frt.pivot = new Vector2(0, 0.5f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(420, 22);
        bootBar = fill.GetComponent<Image>();
    }

    void BuildMainMenu()
    {
        mainMenu = FullScreen(mainMenu, "MainMenu", new Color(0.07f, 0.065f, 0.06f, 1f));
        var title = MakeText(mainMenu.transform, "Title", "JOYVEYOR", 84, TextAnchor.MiddleCenter);
        Center(title.rectTransform, 0, 170, 700, 100);

        playButton = MakeButton(mainMenu.transform, "Play", "PLAY", 40);
        Center(playButton.GetComponent<RectTransform>(), 0, 40, 320, 84);
        playButton.onClick.AddListener(ShowLevelSelect);

        var how = MakeButton(mainMenu.transform, "HowToPlay", "HOW TO PLAY", 30);
        Center(how.GetComponent<RectTransform>(), 0, -70, 320, 72);
        how.GetComponent<Button>().onClick.AddListener(ShowHowToPlay);

        var quit = MakeButton(mainMenu.transform, "Quit", "QUIT", 30);
        Center(quit.GetComponent<RectTransform>(), 0, -180, 320, 72);
        quit.GetComponent<Button>().onClick.AddListener(QuitGame);

        versionTag = MakeText(mainMenu.transform, "Version", "v1.0", 22, TextAnchor.MiddleRight);
        var vrt = versionTag.rectTransform;
        vrt.anchorMin = new Vector2(1, 0); vrt.anchorMax = new Vector2(1, 0);
        vrt.pivot = new Vector2(1, 0);
        vrt.anchoredPosition = new Vector2(-24, 24);
        vrt.sizeDelta = new Vector2(120, 32);
    }

    void BuildLevelSelect()
    {
        levelSelect = FullScreen(levelSelect, "LevelSelect", new Color(0.07f, 0.065f, 0.06f, 1f));
        var back = MakeButton(levelSelect.transform, "Back", "< Back", 26);
        var brt = back.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1);
        brt.pivot = new Vector2(0, 1);
        brt.anchoredPosition = new Vector2(24, -24);
        brt.sizeDelta = new Vector2(160, 56);
        back.GetComponent<Button>().onClick.AddListener(ShowMainMenu);

        var title = MakeText(levelSelect.transform, "Title", "JOYVEYOR", 44, TextAnchor.MiddleCenter);
        Center(title.rectTransform, 0, 250, 600, 64);

        // 4-column grid, 10 slots (rows of 4, 4, 2) per the wireframe.
        levelButtons = new Button[TotalLevels];
        levelStarImages = new Image[TotalLevels * 3];
        levelLockImages = new Image[TotalLevels];
        levelNumberTexts = new Text[TotalLevels];
        const float cellW = 180, cellH = 96, gapX = 16, gapY = 16;
        float gridW = 4 * cellW + 3 * gapX;
        float top = 150;
        for (int i = 0; i < TotalLevels; ++i)
        {
            int col = i % 4, row = i / 4;
            float x = -gridW / 2f + cellW / 2f + col * (cellW + gapX);
            float y = top - cellH / 2f - row * (cellH + gapY);
            var cell = MakeImage(levelSelect.transform, "Level" + (i + 1),
                new Color(0.13f, 0.13f, 0.17f, 1f));
            Center(cell.GetComponent<RectTransform>(), x, y, cellW, cellH);

            // Level number (top-left).
            var num = MakeText(cell.transform, "Num", (i + 1).ToString(), 30, TextAnchor.UpperLeft);
            var nrt = num.rectTransform;
            nrt.anchorMin = new Vector2(0, 1); nrt.anchorMax = new Vector2(0, 1);
            nrt.pivot = new Vector2(0, 1);
            nrt.anchoredPosition = new Vector2(12, -10);
            nrt.sizeDelta = new Vector2(60, 40);
            levelNumberTexts[i] = num;

            // Three star slots (center).
            for (int s = 0; s < 3; ++s)
            {
                var sgo = new GameObject("Star" + s);
                sgo.transform.SetParent(cell.transform, false);
                var srt = sgo.AddComponent<RectTransform>();
                srt.anchorMin = new Vector2(0.5f, 0.5f); srt.anchorMax = new Vector2(0.5f, 0.5f);
                srt.pivot = new Vector2(0.5f, 0.5f);
                srt.anchoredPosition = new Vector2(-40 + s * 40, -8);
                srt.sizeDelta = new Vector2(34, 34);
                var simg = sgo.AddComponent<Image>();
                simg.sprite = GameScreen.StarSprite(false);
                simg.raycastTarget = false;
                levelStarImages[i * 3 + s] = simg;
            }

            // Padlock (shown when locked, over the stars).
            var lgo = new GameObject("Lock");
            lgo.transform.SetParent(cell.transform, false);
            var lrt = lgo.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.5f, 0.5f); lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(0, -8);
            lrt.sizeDelta = new Vector2(48, 48);
            var limg = lgo.AddComponent<Image>();
            limg.sprite = GameScreen.PadlockSprite;
            limg.raycastTarget = false;
            lgo.SetActive(false);
            levelLockImages[i] = limg;

            // Button (unlocked only; locked cells are not clickable).
            var btn = cell.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = cell.GetComponent<Image>();
            int lv = i + 1;
            btn.onClick.AddListener(() => StartLevel(lv));
            levelButtons[i] = btn;
        }
    }

    void RefreshLevelSelect()
    {
        for (int i = 0; i < TotalLevels; ++i)
        {
            int lv = i + 1;
            bool unlocked = Progress.IsUnlocked(lv);
            int stars = Progress.Stars(lv);
            levelButtons[i].interactable = unlocked;
            levelLockImages[i].gameObject.SetActive(!unlocked);
            var cellImg = levelButtons[i].targetGraphic;
            cellImg.color = unlocked
                ? new Color(0.13f, 0.13f, 0.17f, 1f)
                : new Color(0.09f, 0.09f, 0.11f, 1f);
            levelNumberTexts[i].color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.55f, 1f);
            for (int s = 0; s < 3; ++s)
                levelStarImages[i * 3 + s].sprite = GameScreen.StarSprite(s < stars);
        }
    }

    void BuildHowToPlay()
    {
        howToPlay = FullScreen(howToPlay, "HowToPlay", new Color(0.07f, 0.07f, 0.09f, 0.98f));
        var back = MakeButton(howToPlay.transform, "Back", "< Back", 26);
        var brt = back.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1);
        brt.pivot = new Vector2(0, 1);
        brt.anchoredPosition = new Vector2(24, -24);
        brt.sizeDelta = new Vector2(160, 56);
        back.GetComponent<Button>().onClick.AddListener(HideHowToPlay);

        var title = MakeText(howToPlay.transform, "Title", "HOW TO PLAY", 48, TextAnchor.MiddleCenter);
        Center(title.rectTransform, 0, 250, 600, 64);

        string[] cards =
        {
            "1  Belt — drag to draw; drag length = belt length; R rotates",
            "2  Source — spawns items",
            "3  Sink — receive items; fill the goal to win",
            "4  Splitter — 1 in, 2 out",
            "5  Merger — 2 in, 1 out",
        };
        float top = 170, cardH = 72, gap = 12;
        for (int i = 0; i < cards.Length; ++i)
        {
            var card = MakeImage(howToPlay.transform, "Card" + (i + 1),
                new Color(0.13f, 0.13f, 0.17f, 1f));
            Center(card.GetComponent<RectTransform>(), 0, top - cardH / 2f - i * (cardH + gap), 760, cardH);
            var t = MakeText(card.transform, "Text", cards[i], 26, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 16, 8, -16, -8);
        }
        var keys = MakeText(howToPlay.transform, "KeyLine",
            "Right-click deletes  ·  Z undo  ·  Esc pause  ·  1-6 select piece",
            22, TextAnchor.MiddleCenter);
        Center(keys.rectTransform, 0, top - cards.Length * (cardH + gap) - 20, 760, 34);
        howToPlay.SetActive(false);
    }

    // ---- settings (text scale) ----

    void LoadSettings()
    {
        textScaleIndex = 1;
        try
        {
            if (!File.Exists(SettingsPath)) return;
            string s = File.ReadAllText(SettingsPath);
            int i = s.IndexOf("textScale");
            if (i < 0) return;
            int c = s.IndexOf(':', i);
            if (c < 0) return;
            string num = s.Substring(c + 1).Trim();
            int end = num.IndexOfAny(new[] { '}', ',', ' ' });
            if (end > 0) num = num.Substring(0, end);
            int v;
            if (int.TryParse(num, out v)) textScaleIndex = Mathf.Clamp(v, 0, 2);
        }
        catch (System.Exception) { }
    }

    public void SetTextScale(int i)
    {
        textScaleIndex = Mathf.Clamp(i, 0, 2);
        try
        {
            string dir = Application.persistentDataPath;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, "{\"textScale\":" + textScaleIndex + "}");
        }
        catch (System.Exception) { }
        if (gameScreen != null && Current == Screen.InGame)
            gameScreen.SetTextScale(textScaleIndex);
    }

    // ---- UI helpers (mirrors GameScreen's; those are private there) ----

    GameObject FullScreen(GameObject existing, string name, Color c)
    {
        var go = existing != null ? existing : new GameObject(name);
        go.transform.SetParent(menuCanvas.transform, false);
        if (go.GetComponent<Image>() == null)
        {
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }

    static Font _font;
    static Font GetFont()
    {
        if (_font != null) return _font;
        try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch (System.Exception) { }
        if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (System.Exception) { } }
        if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Helvetica", 16);
        return _font;
    }

    static Text MakeText(Transform parent, string name, string txt, int size, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = txt;
        t.font = GetFont();
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        return t;
    }

    static GameObject MakeImage(Transform parent, string name, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = true;
        return go;
    }

    static Button MakeButton(Transform parent, string name, string label, int size)
    {
        var go = MakeImage(parent, name, new Color(0.16f, 0.16f, 0.2f, 1f));
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        btn.targetGraphic = go.GetComponent<Image>();
        var t = MakeText(go.transform, "Label", label, size, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform, 0, 0, 0, 0);
        return btn;
    }

    static void Center(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rt, float l, float b, float r, float t)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(r, t);
    }
}
