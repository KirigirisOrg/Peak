using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;
using Zorro.Core;

namespace ExtraTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private static ManualLogSource _logger;
    private static readonly BuildVersion BuildVersion = new(Application.version);

    private static readonly DateTime
        LevelIndexStartDate =
            new(2025, 6,
                13); // Used for offline level index, should be similar index as https://peaklogin.azurewebsites.net/api/VersionCheck

    private void Awake()
    {
        _logger = Logger;

        _offline = Config.Bind("1_General",
            "Offline",
            false,
            "Sets the game to Offline mode. CloudAPI settings ignored if set to true!");

        _cloudApiURLEnabled = Config.Bind("2_CloudAPI",
            "Enabled",
            true,
            "Setting this will use the ingame CloudAPI URL! Version Checks might fail.");
        _cloudApiURL = Config.Bind("2_CloudAPI",
            "CloudAPI_URL", "https://kirigiri.pythonanywhere.com/kirigiri-peak",
            "Only works if CloudAPI_URL_ENABLED is set to true!");

        _photonEnabled = Config.Bind("3_Photon", "Enabled", false, "Enables the custom Photon settings.");
        _authType = Config.Bind("3_Photon", "AuthType", CustomAuthenticationType.None);
        _appIdRealtime = Config.Bind("3_Photon", "AppIdRealtime", "2be03cc8-3633-4033-a22e-7ef1c243b1fd");
        _appIdChat = Config.Bind("3_Photon", "AppIdChat", "");
        _appIdVoice = Config.Bind("3_Photon", "AppIdVoice", "ba60e90a-af6f-4965-80cf-ff783c91c992");
        _appIdFusion = Config.Bind("3_Photon", "AppIdFusion", "");
        _fixedRegion = Config.Bind("3_Photon", "FixedRegion", "");
        _appIdRealtime = Config.Bind("3_Photon", "AppIdRealtime", "");
        _steamAppId = Config.Bind("3_Photon", "SteamAppId", 480, "Set to 3527290 if you're playing legit!");

        StartupPrint();
        var harmony = new Harmony("nekogirifix.peak");
        _logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        switch (_offline.Value)
        {
            case true:
                harmony.PatchAll(typeof(Offline));
                break;
            case false when _cloudApiURLEnabled.Value:
                harmony.PatchAll(typeof(Online));
                break;
        }

        if (!_photonEnabled.Value) return;
        SetPhoton();
        harmony.PatchAll(typeof(PhotonPatches));
    }


    private void StartupPrint()
    {
        _logger.LogInfo("""

                         ██ ▄█▀ ██▓ ██▀███   ██▓  ▄████  ██▓ ██▀███   ██▓
                         ██▄█▒ ▓██▒▓██ ▒ ██▒▓██▒ ██▒ ▀█▒▓██▒▓██ ▒ ██▒▓██▒
                        ▓███▄░ ▒██▒▓██ ░▄█ ▒▒██▒▒██░▄▄▄░▒██▒▓██ ░▄█ ▒▒██▒
                        ▓██ █▄ ░██░▒██▀▀█▄  ░██░░▓█  ██▓░██░▒██▀▀█▄  ░██░
                        ▒██▒ █▄░██░░██▓ ▒██▒░██░░▒▓███▀▒░██░░██▓ ▒██▒░██░
                        ▒ ▒▒ ▓▒░▓  ░ ▒▓ ░▒▓░░▓   ░▒   ▒ ░▓  ░ ▒▓ ░▒▓░░▓  
                        ░ ░▒ ▒░ ▒ ░  ░▒ ░ ▒░ ▒ ░  ░   ░  ▒ ░  ░▒ ░ ▒░ ▒ ░
                        ░ ░░ ░  ▒ ░  ░░   ░  ▒ ░░ ░   ░  ▒ ░  ░░   ░  ▒ ░
                        ░  ░    ░     ░      ░        ░  ░     ░      ░  
                                                                         
                        https://discord.gg/TBs8Te5nwn
                        """);
        _logger.LogInfo("Made with <3 By Kirigiri and Kirigiri's Personal Cat Girl");
        _logger.LogInfo($"U can find the config file in: {Config.ConfigFilePath}");
    }

    #region Photon

    private static void SetPhoton()
    {
        var serverSettings = Resources.Load<ServerSettings>("PhotonServerSettings");

        serverSettings.AppSettings.AppIdRealtime =
            GetValueOrDefault(_appIdRealtime.Value, serverSettings.AppSettings.AppIdRealtime);
        serverSettings.AppSettings.AppIdChat =
            GetValueOrDefault(_appIdChat.Value, serverSettings.AppSettings.AppIdChat);
        serverSettings.AppSettings.AppIdVoice =
            GetValueOrDefault(_appIdVoice.Value, serverSettings.AppSettings.AppIdVoice);
        serverSettings.AppSettings.AppIdFusion =
            GetValueOrDefault(_appIdFusion.Value, serverSettings.AppSettings.AppIdFusion);
        serverSettings.AppSettings.FixedRegion =
            GetValueOrDefault(_fixedRegion.Value, serverSettings.AppSettings.FixedRegion);

        return;

        string GetValueOrDefault(string newValue, string oldValue) =>
            string.IsNullOrEmpty(newValue) ? oldValue : newValue;
    }

    private class PhotonPatches
    {
        [HarmonyPatch(typeof(SteamManager), "Awake")]
        [HarmonyPrefix]
        private static void SteamManager_Awake(SteamManager __instance)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_appid.txt");
            File.WriteAllText(path, _steamAppId.Value.ToString());
            SteamAPI.Init();
        }

        [HarmonyPatch(typeof(PhotonNetwork), "AuthValues", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool PhotonNetwork_AuthValues(ref AuthenticationValues __result)
        {
            __result.AuthType = _authType.Value;
            return false;
        }
    }

    #endregion

    #region Configuration

    private ConfigEntry<bool> _offline;

    private static ConfigEntry<string> _cloudApiURL;
    private static ConfigEntry<bool> _cloudApiURLEnabled;

    #region Photon

    private static ConfigEntry<bool> _photonEnabled;
    private static ConfigEntry<CustomAuthenticationType> _authType;
    private static ConfigEntry<string> _appIdRealtime;
    private static ConfigEntry<string> _appIdChat;
    private static ConfigEntry<string> _appIdVoice;
    private static ConfigEntry<string> _appIdFusion;
    private static ConfigEntry<string> _fixedRegion;
    private static ConfigEntry<int> _steamAppId;

    #endregion

    #endregion

    #region Level Stuff

    private static int getLevelIndex() => Math.Abs((DateTime.Today - LevelIndexStartDate).Days);

    private static (int Hours, int Minutes, int Seconds) getUntilNextLevel()
    {
        var timeLeft = DateTime.Today.AddDays(1) - DateTime.Now;
        return (timeLeft.Hours, timeLeft.Minutes, timeLeft.Seconds);
    }

    #endregion

    #region Version Strings

    private const string Stable = "why did the chicken cross the caldera?";

    private const string Beta =
        "Thanks for testing the PEAK beta. Watch out for bugs! (the current beta is the same as the live game, check back later for a new beta!)";

    #endregion

    #region Online/Offline Patches

    private class Online
    {
        [HarmonyPatch(typeof(CloudAPI), nameof(CloudAPI.CheckVersion))]
        [HarmonyPrefix]
        private static bool CloudAPI_CheckVersion(Action<LoginResponse> response)
        {
            _logger.LogDebug("CloudAPI.CheckVersion");
            _logger.LogDebug($"Using server {_cloudApiURL.Value} from config");

            GameHandler.AddStatus<QueryingGameTimeStatus>(new QueryingGameTimeStatus());

            _logger.LogDebug($"Sending GET Request to: {_cloudApiURL.Value}");
            var request = UnityWebRequest.Get(_cloudApiURL.Value);

            request.SendWebRequest().completed += _ =>
            {
                GameHandler.ClearStatus<QueryingGameTimeStatus>();
                if (request.result is UnityWebRequest.Result.ConnectionError
                    or UnityWebRequest.Result.DataProcessingError or UnityWebRequest.Result.ProtocolError)
                {
                    _logger.LogError($"Got error: {request.error}");

                    var (h, m, s) = getUntilNextLevel();
                    response?.Invoke(new LoginResponse
                    {
                        VersionOkay = true,
                        HoursUntilLevel = h,
                        MinutesUntilLevel = m,
                        SecondsUntilLevel = s,
                        LevelIndex = getLevelIndex(),
                        Message = request.result switch
                        {
                            UnityWebRequest.Result.ConnectionError => "Connection to CloudAPI Failed!",
                            UnityWebRequest.Result.DataProcessingError => "Failed to process CloudAPI data!",
                            _ => "Connection to the CloudAPI had a Protocol Error!"
                        } + "Falling back to local data."
                    });
                }
                else
                {
                    var text = request.downloadHandler.text;
                    _logger.LogDebug($"Got message: {text}");
                    response?.Invoke(JsonUtility.FromJson<LoginResponse>(text));
                }
            };
            return false;
        }
    }

    private class Offline
    {
        [HarmonyPatch(typeof(CloudAPI), nameof(CloudAPI.CheckVersion))]
        [HarmonyPrefix]
        private static bool CloudAPI_CheckVersion(Action<LoginResponse> response)
        {
            _logger.LogDebug("CloudAPI.CheckVersion");

            var (h, m, s) = getUntilNextLevel();
            response?.Invoke(new LoginResponse
            {
                VersionOkay = true,
                HoursUntilLevel = h,
                MinutesUntilLevel = m,
                SecondsUntilLevel = s,
                LevelIndex = getLevelIndex(),
                Message = BuildVersion.BuildName == "beta" ? Beta : Stable
            });
            return false;
        }

        [HarmonyPatch(typeof(NetworkConnector), nameof(NetworkConnector.ConnectToPhoton))]
        [HarmonyPrefix]
        private static bool NetworkConnector_ConnectToPhoton()
        {
            PhotonNetwork.OfflineMode = true;
            return false;
        }
    }

    #endregion
}
