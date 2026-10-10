using System;
using System.IO;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace HSClipTest
{
    /// <summary>
    /// Trial B helper (MODLOG 2026-10-06): opens the Modding Kit editor without a click, so the agent can work with the
    /// game window in the background. Once the main menu (MBInitialScreenBase) has been on top for 2 s, it calls
    /// MBInitialScreenBase.OnEditModeEnterPress(), what the menu's "Editor" button and Ctrl+E call (decompiled 1.4.8
    /// ViewSubModule lines 170-175, MBInitialScreenBase lines 79-83 and 221-224). Editor build only, once per run.
    /// Writes what it does to %TEMP%\hsclip_autoeditor.log.
    /// </summary>
    public sealed class SubModule : MBSubModuleBase
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "hsclip_autoeditor.log");
        private bool _done;
        private bool _seen;
        private float _onMenuFor;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Log("loaded, edit mode enabled = " + TaleWorlds.Engine.Utilities.EditModeEnabled);
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (_done || !TaleWorlds.Engine.Utilities.EditModeEnabled)
            {
                return;
            }
            if (ScreenManager.TopScreen is MBInitialScreenBase)
            {
                if (!_seen)
                {
                    _seen = true;
                    Log("main menu on top");
                }
                _onMenuFor += dt;
                if (_onMenuFor > 2f)
                {
                    _done = true;
                    Log("calling MBInitialScreenBase.OnEditModeEnterPress()");
                    MBInitialScreenBase.OnEditModeEnterPress();
                    Log("returned from OnEditModeEnterPress()");
                }
            }
            else
            {
                _onMenuFor = 0f;
            }
        }

        private static void Log(string text)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
            }
            catch (IOException)
            {
                // The log is only an oracle; never let it break the game.
            }
        }
    }
}
