using System.Collections;
using TMPro;
using UnityEngine;

// The closing title card (call Show() from SinkingSequence.onDarkness): the name fades in over the black,
// holds, and fades out again. It floats in front of the player and lazily follows the head rather than being
// glued to it (head-locked text is uncomfortable in VR). Works for the XR camera and the desktop rig alike.
public class ClosingTitle : MonoBehaviour
{
    [TextArea(3, 6)]
    [Tooltip("Film-style title card: just the name out of the black (TMP rich text works, e.g. <cspace> for spacing).")]
    public string text = "<cspace=0.6em>TITANIC</cspace>";
    public TMP_FontAsset font;
    public float delay = 1.5f;
    public float fadeIn = 2.5f;
    public float hold = 7f;
    public float fadeOut = 2.5f;
    public float distance = 3f;
    [Tooltip("How quickly the title catches up with the head (higher = stiffer).")]
    public float follow = 2.5f;
    [Tooltip("An otherwise unused layer: the camera draws only this once the title appears.")]
    public int titleLayer = 31;

    TextMeshPro title;
    Transform head;

    public void Show() => StartCoroutine(Run());

    IEnumerator Run()
    {
        yield return new WaitForSeconds(delay);
        head = Camera.main ? Camera.main.transform : null;
        if (!head) yield break;

        // The world is already black, but its geometry still occludes a title floating 3 m out (walls,
        // furniture, the flooded room). Draw nothing but the title, on pure black, from here on.
        var cam = head.GetComponent<Camera>();
        cam.cullingMask = 1 << titleLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        title = new GameObject("ClosingTitle").AddComponent<TextMeshPro>();
        title.gameObject.layer = titleLayer;
        if (font) title.font = font;
        title.text = text;
        title.alignment = TextAlignmentOptions.Center;
        title.fontSize = 4f;
        title.rectTransform.sizeDelta = new Vector2(5.5f, 2f);
        title.color = new Color(0.92f, 0.94f, 1f, 0f);
        title.transform.SetPositionAndRotation(Target(), TargetRotation());

        yield return Fade(0f, 1f, fadeIn);
        yield return new WaitForSeconds(hold);
        yield return Fade(1f, 0f, fadeOut);
        Destroy(title.gameObject);
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            title.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        title.alpha = to;
    }

    void LateUpdate()
    {
        if (!title || !head) return;
        float k = 1f - Mathf.Exp(-follow * Time.deltaTime);
        title.transform.SetPositionAndRotation(Vector3.Lerp(title.transform.position, Target(), k),
                                               Quaternion.Slerp(title.transform.rotation, TargetRotation(), k));
    }

    Vector3 Target() => head.position + head.forward * distance;
    Quaternion TargetRotation() => Quaternion.LookRotation(head.forward, head.up); // text faces the viewer
}
