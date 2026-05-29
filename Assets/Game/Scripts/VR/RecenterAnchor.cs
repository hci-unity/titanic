using UnityEngine;

// Stores the play-space center (the spot the physical-movement boundary is measured from).
// Captured at the HMD's horizontal position on start; can be re-captured (recenter).
[DisallowMultipleComponent]
public class RecenterAnchor : MonoBehaviour
{
    public Transform hmd;
    public Vector3 Center { get; private set; }
    public bool HasCenter { get; private set; }

    void Start() { if (hmd != null) Recenter(); }

    public void Recenter()
    {
        if (hmd == null) return;
        var p = hmd.position;
        Center = new Vector3(p.x, 0f, p.z); // horizontal only
        HasCenter = true;
    }

    // Horizontal distance of the HMD from the captured center.
    public float HorizontalDistance()
    {
        if (!HasCenter || hmd == null) return 0f;
        var p = hmd.position;
        return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Center.x, Center.z));
    }
}
