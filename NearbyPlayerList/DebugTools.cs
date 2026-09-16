using System;
using System.Diagnostics;
using System.IO;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace NearbyPlayerList;

// Debug helpers. Nothing here affects normal play; it exists so a problem can be
// investigated from in game rather than by asking someone to find a log file.
internal static unsafe class DebugTools
{
    // The game's own chat sound effects, the ones <se.1> to <se.16> play. Using these
    // rather than a Windows sound means the game's audio settings still apply, and
    // nothing has to be shipped with the plugin.
    public const int MinSound = 1;
    public const int MaxSound = 16;

    public static void PlaySound(int effect)
    {
        try
        {
            var clamped = (uint)Math.Clamp(effect, MinSound, MaxSound);
            UIGlobals.PlayChatSoundEffect(clamped);
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Could not play the sound effect.");
        }
    }

    public static string LogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "dalamud.log");

    public static void OpenLogFolder()
    {
        try
        {
            var path = LogPath;

            // Opens the folder with the log already selected when it exists, so there is
            // no hunting through a directory full of launcher files.
            var argument = File.Exists(path)
                ? $"/select,\"{path}\""
                : $"\"{Path.GetDirectoryName(path)}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Could not open the log folder.");
            Service.Chat.Print($"[NPL] Could not open the folder. The log is at {LogPath}");
        }
    }
}
