using UnityEngine;
using UnityEngine.UI;

// Binds world-space UI controls to ComfortSettings. Attach to the settings panel root.
[DisallowMultipleComponent]
public class ComfortSettingsUI : MonoBehaviour
{
    public ComfortSettings comfort;
    public Button snapButton;
    public Button smoothButton;
    public Button noneButton;
    public Text statusLabel;

    void Start()
    {
        if (snapButton != null) snapButton.onClick.AddListener(OnSnap);
        if (smoothButton != null) smoothButton.onClick.AddListener(OnSmooth);
        if (noneButton != null) noneButton.onClick.AddListener(OnNone);
        Refresh();
    }

    void OnSnap()   { if (comfort != null) comfort.UseSnap();   Refresh(); }
    void OnSmooth() { if (comfort != null) comfort.UseSmooth(); Refresh(); }
    void OnNone()   { if (comfort != null) comfort.UseNone();   Refresh(); }

    void Refresh()
    {
        if (statusLabel != null && comfort != null)
            statusLabel.text = "Turn: " + comfort.Current;
    }
}
