using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using Steamworks;

namespace AchievementFix;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private static ManualLogSource _logger;

    private static readonly string DataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NekogiriBread");

    private static readonly string AchievementsPath = Path.Combine(DataPath, "achievements.json");
    private static readonly string StatsPath = Path.Combine(DataPath, "stats.json");

    private static Dictionary<string, bool> _achievementStates;
    private static Dictionary<string, int> _statStatesInt = new();
    private static Dictionary<string, float> _statStatesFloat = new();

    private void Awake()
    {
        Directory.CreateDirectory(DataPath);

        _logger = Logger;
        _logger.LogInfo("Made by Bread-Chan 🩷. Hallo to Kirigiri~ 👋");

        LoadAchievements();
        LoadStats();

        var harmony = new Harmony("com.nekogiri.bread.achievementfix");
        harmony.PatchAll(typeof(SteamPatches));
        harmony.PatchAll(typeof(SteamPatches.GetStatFloatPatch));
        harmony.PatchAll(typeof(SteamPatches.GetStatIntPatch));
    }

    private class SteamPatches
    {
        #region Achievement Patches

        [HarmonyPatch(typeof(SteamUserStats), "GetAchievement")]
        [HarmonyPostfix]
        private static void GetAchievement(string pchName, ref bool pbAchieved)
        {
            if (!_achievementStates.ContainsKey(pchName))
            {
                _achievementStates[pchName] = pbAchieved;
                SaveAchievements();
            }

            pbAchieved = _achievementStates[pchName] || pbAchieved;
        }

        [HarmonyPatch(typeof(SteamUserStats), "SetAchievement")]
        [HarmonyPrefix]
        private static bool SetAchievement(string pchName)
        {
            _achievementStates[pchName] = true;
            SaveAchievements();

            return false;
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
                pData = GetStatInt(pchName);
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
                pData = GetStatFloat(pchName);
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

    #region Helper Functions

    #region Achievement

    private static void LoadAchievements()
    {
        if (File.Exists(AchievementsPath))
            try
            {
                var json = File.ReadAllText(AchievementsPath);
                _achievementStates = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
            }
            catch
            {
                _achievementStates = new Dictionary<string, bool>();
            }
        else
            _achievementStates = new Dictionary<string, bool>();
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

    private static void SetStat(string name, int value)
    {
        _statStatesInt[name] = value;
        SaveStats();
    }

    private static void SetStat(string name, float value)
    {
        _statStatesFloat[name] = value;
        SaveStats();
    }

    private static int GetStatInt(string name, int defaultValue = 0) =>
        _statStatesInt.TryGetValue(name, out var value) ? value : defaultValue;

    private static float GetStatFloat(string name, float defaultValue = 0f) =>
        _statStatesFloat.TryGetValue(name, out var value) ? value : defaultValue;

    #endregion

    #endregion
}