using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AchievementFix;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private static ManualLogSource _logger;
    private static GameObject _popupCanvas;

    private static readonly string DataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NekogiriBread");

    private static readonly string AchievementsPath = Path.Combine(DataPath, "achievements.json");
    private static readonly string StatsPath = Path.Combine(DataPath, "stats.json");

    private static Dictionary<string, AchievementData> _achievementStates;
    private static Dictionary<string, int> _statStatesInt = new();
    private static Dictionary<string, float> _statStatesFloat = new();
    private static Material _uiWoodMaterial;
    private static TMP_FontAsset darumaFont;

    private void Awake()
    {
        Directory.CreateDirectory(DataPath);

        _logger = Logger;
        _logger.LogInfo("Made by Bread-Chan 🩷. Hallo to Kirigiri~ 👋");

        MigrateAchievements();
        LoadAchievements();
        LoadStats();

        var harmony = new Harmony("com.nekogiri.bread.achievementfix");
        harmony.PatchAll(typeof(SteamPatches));
        harmony.PatchAll(typeof(SteamPatches.GetStatFloatPatch));
        harmony.PatchAll(typeof(SteamPatches.GetStatIntPatch));

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name is "Title") FindResources();
    }

    private static void FindResources()
    {
        foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            if (mat.name == "UI_Wood")
            {
                _uiWoodMaterial = mat;
                break;
            }

        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (font.name == "DarumaDropOne-Regular SDF")
            {
                darumaFont = font;
                break;
            }
    }

    private class SteamPatches
    {
        #region Achievement Patches

        [HarmonyPatch(typeof(SteamUserStats), "GetAchievement")]
        [HarmonyPostfix]
        public static void GetAchievement(string pchName, ref bool pbAchieved)
        {
            if (!_achievementStates.ContainsKey(pchName))
            {
                _achievementStates[pchName] = new AchievementData { Gotten = pbAchieved };
                SaveAchievements();
            }

            pbAchieved = _achievementStates[pchName].Gotten || pbAchieved;
        }

        [HarmonyPatch(typeof(SteamUserStats), "SetAchievement")]
        [HarmonyPrefix]
        private static void SetAchievement(string pchName)
        {
            if (!_achievementStates.ContainsKey(pchName))
                _achievementStates[pchName] = new AchievementData();

            var data = _achievementStates[pchName];
            data.Gotten = true;
            data.TimeAchieved = DateTime.UtcNow;

            if (Enum.TryParse<ACHIEVEMENTTYPE>(pchName, out var achievement))
                TriggerAch(achievement);

            SaveAchievements();
        }

        #endregion

        #region Stat Patches

        #region Int

        [HarmonyPatch(typeof(SteamUserStats), "SetStat", typeof(string), typeof(int))]
        [HarmonyPostfix]
        public static void SetStat_Int(string pchName, ref int nData) => SetStat(pchName, nData);

        [HarmonyPatch]
        public static class GetStatIntPatch
        {
            // ReSharper disable once UnusedMember.Local
            private static MethodInfo TargetMethod() =>
                typeof(SteamUserStats).GetMethod("GetStat", [typeof(string), typeof(int).MakeByRefType()]);

            // ReSharper disable once UnusedMember.Local
            [HarmonyPrefix]
            public static void Postfix(string pchName, ref int pData, ref bool __result, ref bool __runOriginal)
            {
                if (!_statStatesInt.ContainsKey(pchName)) SetStat(pchName, pData);
                pData = GetStat<int>(pchName);
                __result = true;
                __runOriginal = false;
            }
        }

        #endregion

        #region Float

        [HarmonyPatch(typeof(SteamUserStats), "SetStat", typeof(string), typeof(float))]
        [HarmonyPostfix]
        public static void SetStat_FLOAT(string pchName, ref float fData) => SetStat(pchName, fData);

        [HarmonyPatch]
        public static class GetStatFloatPatch
        {
            // ReSharper disable once UnusedMember.Local
            private static MethodInfo TargetMethod() =>
                typeof(SteamUserStats).GetMethod("GetStat", [typeof(string), typeof(float).MakeByRefType()]);

            // ReSharper disable once UnusedMember.Local
            [HarmonyPostfix]
            public static void Postfix(string pchName, ref float pData, ref bool __result, ref bool __runOriginal)
            {
                if (!_statStatesFloat.ContainsKey(pchName)) SetStat(pchName, pData);
                pData = GetStat<float>(pchName);
                __result = true;
                __runOriginal = false;
            }
        }

        #endregion

        [HarmonyPatch(typeof(SteamUserStats), "StoreStats")]
        [HarmonyPostfix]
        public static void StoreStats() => SaveStats();

        #endregion
    }

    #region UI

    private static void TriggerAch(ACHIEVEMENTTYPE ach)
    {
        var badgeData = GUIManager.instance.mainBadgeManager.GetBadgeData(ach);

        if (badgeData == null) return;

        var displayName = badgeData.displayName;
        var description = badgeData.description;
        var iconTexture = badgeData.icon;

        Sprite iconSprite = null;
        if (iconTexture != null)
        {
            var tex2D = iconTexture as Texture2D;
            if (tex2D != null)
                iconSprite = Sprite.Create(tex2D, new Rect(0, 0, tex2D.width, tex2D.height), new Vector2(0.5f, 0.5f));
        }

        ShowPopup(displayName, description, iconSprite);
    }


    private static void ShowPopup(string title, string description, Sprite icon)
    {
        if (_popupCanvas == null)
        {
            _popupCanvas = new GameObject("AchievementPopupCanvas");
            var canvas = _popupCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            _popupCanvas.AddComponent<CanvasScaler>();
            _popupCanvas.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(_popupCanvas);
        }

        var go = new GameObject("AchievementPopup");
        go.transform.SetParent(_popupCanvas.transform, false);

        var bg = go.AddComponent<Image>();
        bg.material = _uiWoodMaterial;
        bg.color = new Color(0.783f, 0.6374f, 0.4765f, 1f);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(350, 100);
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-10, 10);

        if (icon != null)
        {
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var img = iconGo.AddComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            var iconRT = iconGo.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0, 0.5f);
            iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.sizeDelta = new Vector2(75, 75);
            iconRT.anchoredPosition = new Vector2(10, 0);
        }

        var textParent = new GameObject("TextParent");
        textParent.transform.SetParent(go.transform, false);
        var textRT = textParent.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 0);
        textRT.anchorMax = new Vector2(1, 1);
        textRT.offsetMin = new Vector2(90, 10);
        textRT.offsetMax = new Vector2(-10, -10);

        var titleText = new GameObject("TitleText");
        titleText.transform.SetParent(textParent.transform, false);
        var titleTMP = titleText.AddComponent<TextMeshProUGUI>();
        titleTMP.font = darumaFont;
        titleTMP.fontSize = 25;
        titleTMP.color = new Color(0.184f, 0.122f, 0.059f, 1);
        titleTMP.text = title;
        titleTMP.textWrappingMode = TextWrappingModes.Normal;
        titleTMP.rectTransform.anchorMin = new Vector2(0, 0.5f);
        titleTMP.rectTransform.anchorMax = new Vector2(1, 1);
        titleTMP.rectTransform.offsetMin = Vector2.zero;
        titleTMP.rectTransform.offsetMax = Vector2.zero;

        var descText = new GameObject("DescriptionText");
        descText.transform.SetParent(textParent.transform, false);
        var descTMP = descText.AddComponent<TextMeshProUGUI>();
        descTMP.font = darumaFont;
        descTMP.fontSize = 18;
        descTMP.color = new Color(0.368f, 0.257f, 0.12f, 1);
        descTMP.text = description;
        descTMP.textWrappingMode = TextWrappingModes.Normal;
        descTMP.rectTransform.anchorMin = new Vector2(0, 0);
        descTMP.rectTransform.anchorMax = new Vector2(1, 0.5f);
        descTMP.rectTransform.offsetMin = Vector2.zero;
        descTMP.rectTransform.offsetMax = Vector2.zero;

        go.AddComponent<CanvasGroup>();

        go.AddComponent<AchievementPopupFade>();
    }

    private class AchievementPopupFade : MonoBehaviour
    {
        private const float DisplayTime = 1.5f;
        private const float FadeDuration = 1.5f;
        private CanvasGroup _canvasGroup;
        private float _timer;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _canvasGroup.alpha = 1f;
            _timer = 0f;
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            if (!(_timer > DisplayTime)) return;
            var fadeTime = _timer - DisplayTime;
            _canvasGroup.alpha = 1f - fadeTime / FadeDuration;

            if (fadeTime >= FadeDuration)
                Destroy(gameObject);
        }
    }

    #endregion

    #region Helpers

    #region Achievements

    private class AchievementData
    {
        public bool Gotten { get; set; }
        public DateTime? TimeAchieved { get; set; }
    }

    private static void LoadAchievements()
    {
        if (File.Exists(AchievementsPath))
            try
            {
                var json = File.ReadAllText(AchievementsPath);
                _achievementStates = JsonConvert.DeserializeObject<Dictionary<string, AchievementData>>(json)
                                     ?? new Dictionary<string, AchievementData>();
            }
            catch
            {
                _achievementStates = new Dictionary<string, AchievementData>();
            }
        else
            _achievementStates = new Dictionary<string, AchievementData>();
    }

    private static void SaveAchievements()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_achievementStates, Formatting.Indented);
            File.WriteAllText(AchievementsPath, json);
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to save achievements: {e}");
        }
    }

    // NOTE: This is used to migrate the achievement JSON file to newer format past v0.0.4
    private static void MigrateAchievements()
    {
        if (!File.Exists(AchievementsPath)) return;

        try
        {
            var json = File.ReadAllText(AchievementsPath);

            if (json.Contains("Gotten")) return;

            var oldData = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
            if (oldData == null) return;

            var newData = new Dictionary<string, AchievementData>();
            foreach (var kvp in oldData)
                newData[kvp.Key] = new AchievementData
                {
                    Gotten = kvp.Value,
                    TimeAchieved = kvp.Value ? DateTime.UtcNow : null
                };

            var newJson = JsonConvert.SerializeObject(newData, Formatting.Indented);
            File.WriteAllText(AchievementsPath, newJson);
            _logger.LogInfo("Achievements migrated to new format with timestamps.");
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to migrate achievements: {e}");
        }
    }

    #endregion

    #region Stats

    private class StatWrapper
    {
        public Dictionary<string, float> Float = new();
        public Dictionary<string, int> Int = new();
    }

    private static void LoadStats()
    {
        if (File.Exists(StatsPath))
        {
            try
            {
                var json = File.ReadAllText(StatsPath);
                var wrapper = JsonConvert.DeserializeObject<StatWrapper>(json);
                _statStatesInt = wrapper?.Int ?? new Dictionary<string, int>();
                _statStatesFloat = wrapper?.Float ?? new Dictionary<string, float>();
            }
            catch (Exception e)
            {
                _logger.LogError($"Failed to load stats: {e}");
                _statStatesInt = new Dictionary<string, int>();
                _statStatesFloat = new Dictionary<string, float>();
            }
        }
        else
        {
            _statStatesInt = new Dictionary<string, int>();
            _statStatesFloat = new Dictionary<string, float>();
        }
    }

    private static void SaveStats()
    {
        try
        {
            var wrapper = new StatWrapper
            {
                Int = _statStatesInt,
                Float = _statStatesFloat
            };
            var json = JsonConvert.SerializeObject(wrapper, Formatting.Indented);
            File.WriteAllText(StatsPath, json);
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to save stats: {e}");
        }
    }

    private static void SetStat<T>(string name, T value) where T : struct
    {
        if (typeof(T) == typeof(int))
            _statStatesInt[name] = (int)(object)value;
        else if (typeof(T) == typeof(float))
            _statStatesFloat[name] = (float)(object)value;
        else
            throw new NotSupportedException($"Type {typeof(T)} not supported");
        SaveStats();
    }

    private static T GetStat<T>(string name, T defaultValue = default) where T : struct
    {
        if (typeof(T) == typeof(int))
            return _statStatesInt.TryGetValue(name, out var value)
                ? (T)(object)value
                : defaultValue;

        if (typeof(T) == typeof(float))
            return _statStatesFloat.TryGetValue(name, out var value)
                ? (T)(object)value
                : defaultValue;

        throw new NotSupportedException($"Type {typeof(T)} not supported");
    }

    #endregion

    #endregion
}