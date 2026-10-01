using UnityEditor;

static class DesktopModeMenu
{
    const string Path = "Titanic/Desktop Mode (no VR)";

    [MenuItem(Path)]
    static void Toggle() => EditorPrefs.SetBool(DesktopMode.PrefKey, !DesktopMode.Enabled);

    [MenuItem(Path, true)]
    static bool Validate()
    {
        Menu.SetChecked(Path, DesktopMode.Enabled);
        return !EditorApplication.isPlaying;
    }
}
