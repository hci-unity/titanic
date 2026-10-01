using System.Collections;
using UnityEngine;

// A ceiling lamp that shakes loose (added + configured at runtime by SinkingSequence): wobbles on its
// mount, drops, and shatters (see Shatter) with a random break sound on its first hard hit.
// Falls along ShipShake's "real" down (straight down in the player's view even while the ship tilts),
// not Physics.gravity, which stays world-down.
public class FallingLamp : MonoBehaviour
{
    public AudioClip[] breakClips;
    public Material shardMaterial;
    public ShipShake ship;
    public System.Action onDetach; // the lamp's light dies when it rips off the ceiling
    public float wobbleTime = 1.5f;
    public float wobbleAngle = 12f;
    public float breakSpeed = 1.5f;
    public int shardCount = 12;
    [Range(0f, 1f)] public float breakVolume = 0.55f;
    [Tooltip("Within this distance the break sound plays at full volume.")]
    public float breakFullVolumeDistance = 6f;

    Rigidbody body;
    bool broken;
    float detachTime = float.MaxValue;

    public void Drop(float delay) => StartCoroutine(DropRoutine(delay));

    IEnumerator DropRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Swing around the ceiling mount, not the transform pivot: the model's pivot sits far from the lamp,
        // so rotating around it swept the lamp through the walls.
        var b = GetComponent<Renderer>().bounds;
        var mount = b.center + Vector3.up * b.extents.y;
        Vector3 restPos = transform.position;
        var restRot = transform.rotation;
        var axis = Vector3.Cross(Vector3.up, Random.onUnitSphere).normalized;
        for (float t = 0; t < wobbleTime; t += Time.deltaTime)
        {
            var swing = Quaternion.AngleAxis(Mathf.Sin(t * 18f) * wobbleAngle * t / wobbleTime, axis);
            transform.SetPositionAndRotation(mount + swing * (restPos - mount), swing * restRot);
            yield return null;
        }

        onDetach?.Invoke();
        foreach (var c in GetComponents<Collider>()) Destroy(c); // the model's mesh collider can't be dynamic
        // Start clear of the ceiling it hung from, or physics resolves the overlap by pushing it up on top.
        transform.position += (ship ? ship.LevelRotation : Quaternion.identity) * Vector3.down * 0.05f;
        gameObject.AddComponent<BoxCollider>().size *= 0.9f;
        body = gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = 3f;
        body.angularVelocity = Random.insideUnitSphere * 0.5f; // a slight tumble, not a spin
        detachTime = Time.time;
    }

    void FixedUpdate()
    {
        if (body) body.AddForce((ship ? ship.LevelRotation : Quaternion.identity) * Physics.gravity, ForceMode.Acceleration);
    }

    void OnCollisionEnter(Collision hit)
    {
        if (hit.relativeVelocity.magnitude >= breakSpeed) Break();
    }

    // Fallback: a soft first contact (railing graze, slow slide) never re-triggers Enter, so once it has
    // been falling for a moment, any lasting contact shatters it too.
    void OnCollisionStay() { if (Time.time - detachTime > 0.4f) Break(); }

    void Break()
    {
        if (broken || !body) return;
        broken = true;
        var center = GetComponent<Renderer>().bounds.center;
        if (breakClips != null && breakClips.Length > 0)
            Shatter.Sound(breakClips[Random.Range(0, breakClips.Length)], center, breakVolume, breakFullVolumeDistance);
        Shatter.Burst(center, Vector3.one * 0.15f, shardCount, new Vector2(0.03f, 0.09f), shardMaterial, body.linearVelocity * 0.3f, ship);
        Destroy(gameObject);
    }
}
