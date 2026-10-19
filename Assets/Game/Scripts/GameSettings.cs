using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Reads settings.txt next to the .exe (project root in the Editor) once the scene has loaded,
// before any Start(): picks the mirror character (locked, the A button does nothing) and the
// game length; DesktopMode reads "mode = pc" from here too. Args like "mode=pc" override the file. A missing file changes nothing in the Editor; in a build a template is written.
public static class GameSettings
{
    const string Template =
        "# Titanic VR settings. Edit, save, then start the .exe.\n" +
        "\n" +
        "# vr (Quest headset + controllers) or pc (keyboard + mouse; the mirror shows a stand-in, not Jack/Rose)\n" +
        "mode = vr\n" +
        "\n" +
        "# jack or rose\n" +
        "character = jack\n" +
        "\n" +
        "# short (~1.5 min), mid (3 min), long (5 min), or a number of minutes, e.g. 4\n" +
        "length = mid\n";

    public static string FilePath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "settings.txt");

    static Dictionary<string, string> settings;

    // Lower-cased value for key, or null. Read once per run.
    public static string Get(string key)
    {
        if (settings == null)
        {
            if (!File.Exists(FilePath) && !Application.isEditor) File.WriteAllText(FilePath, Template);
            var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : new string[0];
            // key=value command-line args (the launcher's Run) override the file; last one wins.
            settings = lines.Concat(System.Environment.GetCommandLineArgs().Skip(1))
                .Select(l => l.Split('#')[0].Split('='))
                .Where(kv => kv.Length == 2)
                .GroupBy(kv => kv[0].Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Last()[1].Trim().ToLowerInvariant());
        }
        return settings.TryGetValue(key, out var v) ? v : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() => settings = null; // the Editor keeps statics between Play sessions

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        if (Get("character") is string who) LockCharacter(who);
        if (Get("length") is string length) SetLength(length);
    }

    static void LockCharacter(string who)
    {
        var switcher = Object.FindAnyObjectByType<MirrorCharacterSwitcher>(FindObjectsInactive.Include);
        if (!switcher) return;
        string key = who == "male" ? "jack" : who == "female" ? "rose" : who;
        var pick = switcher.characters.FirstOrDefault(c => c && c.name.ToLowerInvariant().Contains(key));
        if (!pick) { Debug.LogWarning($"[GameSettings] unknown character '{who}'"); return; }
        foreach (var c in switcher.characters) if (c) c.SetActive(c == pick);
        switcher.characters = new() { pick }; // one entry: Start() shows it, the A button is a no-op
    }

    static void SetLength(string length)
    {
        var seq = Object.FindAnyObjectByType<SinkingSequence>(FindObjectsInactive.Include);
        if (!seq) return;
        float minutes;
        if (length == "short") return; // the scene's own timing
        else if (length == "mid") minutes = 3f;
        else if (length == "long") minutes = 5f;
        else if (!float.TryParse(length, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out minutes))
        { Debug.LogWarning($"[GameSettings] unknown length '{length}'"); return; }

        // Total = calm + sinking + ~15 s closing title.
        seq.calmDuration = Mathf.Max(10f, minutes * 60f - seq.sinkDuration - 15f);
        foreach (var berg in Object.FindObjectsByType<IcebergDrift>(FindObjectsInactive.Include))
            berg.approachTime = seq.calmDuration; // reaches the ship exactly when the calm ends
        Debug.Log($"[GameSettings] length {length}: calm {seq.calmDuration:0} s");
    }
}
