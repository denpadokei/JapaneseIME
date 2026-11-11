using System;
using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPALogger = IPA.Logging.Logger;

namespace JapaneseIME
{
    [Plugin(RuntimeOptions.SingleStartInit), NoEnableDisable]
    public class Plugin
    {
        internal static IPALogger Log { get; private set; }
        internal static Harmony Harmony { get; private set; }

        internal static PluginConfig Config =>
            PluginConfig.Instance ?? (PluginConfig.Instance = new PluginConfig());

        [Init]
        public Plugin(IPALogger logger, Config config)
        {
            Log = logger;
            try
            {
                PluginConfig.Instance = config.Generated<PluginConfig>();
            }
            catch (Exception ex)
            {
                PluginConfig.Instance = new PluginConfig();
                Log.Error("Could not load config, using defaults: " + ex);
            }
        }

        [OnStart]
        public void OnApplicationStart()
        {
            try
            {
                Harmony = new Harmony("JapaneseIME");

                // Patch one class at a time: a single bad target must not stop the
                // rest of the plugin from loading.
                foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (!type.IsClass ||
                        type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        Harmony.CreateClassProcessor(type).Patch();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"JapaneseIME patch failed for '{type.Name}': {ex.Message}");
                    }
                }

                ImeManager.EnsureCreated();
                ImeMessageHook.EnsureInstalled();

                if (Config.SelfTestMode && ImeManager.Instance != null)
                {
                    ImeManager.Instance.Activate(new FakeTextTarget(ImeManager.Instance.gameObject));
                    Log.Info("JapaneseIME self-test mode is active.");
                }

                Log.Info("JapaneseIME started: Windows IME input is enabled for in-game text fields.");
            }
            catch (Exception ex)
            {
                Log.Error("JapaneseIME failed to start: " + ex);
            }
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            try
            {
                ImeMessageHook.Uninstall();
                ImeManager.Instance?.Shutdown();
                Harmony?.UnpatchSelf();
                ImeManager.DestroyInstance();
                CandidateWindow.Destroy();
            }
            catch (Exception ex)
            {
                Log?.Error("JapaneseIME shutdown error: " + ex);
            }

            Log?.Info("JapaneseIME stopped.");
        }
    }
}
