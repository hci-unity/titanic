using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Management;

// Feature flag: test the scene in plain PC first-person (WASD + mouse) without a headset.
// Toggle in the editor via menu "Titanic/Desktop Mode (no VR)". Off = normal VR. Builds are always VR.
// When on, at Play start: stops XR, disables the XR Origin + mirror avatars, enables the old FirstPersonRig.
public static class DesktopMode
{
    public const string PrefKey = "Titanic.DesktopMode";

    public static bool Enabled
    {
#if UNITY_EDITOR
        get => UnityEditor.EditorPrefs.GetBool(PrefKey, false);
#else
        get => false;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        if (!Enabled) return;

        var fp = Object.FindAnyObjectByType<FirstPersonController>(FindObjectsInactive.Include);
        if (fp == null) { Debug.LogWarning("DesktopMode: no FirstPersonRig in scene, staying in VR."); return; }

        var xrManager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (xrManager != null && xrManager.isInitializationComplete)
        {
            xrManager.StopSubsystems();
            xrManager.DeinitializeLoader();
        }

        var xrOrigin = Object.FindAnyObjectByType<XROrigin>();
        if (xrOrigin != null) xrOrigin.gameObject.SetActive(false);

        // Avatars are driven by the XR rig; without it they'd stand frozen in the mirror.
        var avatars = GameObject.Find("MirrorAvatars");
        if (avatars != null) avatars.SetActive(false);

        fp.gameObject.SetActive(true);
        Debug.Log("DesktopMode: ON — PC first-person (WASD/mouse, Shift run, Space jump).");
    }
}
