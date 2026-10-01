using System.Collections.Generic;
using UnityEngine;

// Breaking glass, shared by the falling lamps and the mirror: a burst of shard rigidbodies that fall along
// ShipShake's "real" down (straight down in the player's view even while the ship tilts), and a mostly-3D
// break sound that stays audible across the room.
public class Shatter : MonoBehaviour
{
    readonly List<Rigidbody> shards = new();
    ShipShake ship;
    float lostBelowY;

    // lumpMesh set = solid irregular lumps of that shape (ice), otherwise thin flat glass shards.
    public static void Burst(Vector3 center, Vector3 spread, int count, Vector2 sizeRange, Material material, Vector3 velocity, ShipShake ship, Mesh lumpMesh = null)
    {
        var s = new GameObject("Shards").AddComponent<Shatter>();
        s.ship = ship;
        s.lostBelowY = center.y - 5f; // deeper than the gallery-to-floor drop (~3.5 m), so spilled shards still land
        for (int i = 0; i < count; i++)
        {
            GameObject g;
            if (lumpMesh)
            {
                g = new GameObject("Shard", typeof(MeshFilter), typeof(MeshRenderer));
                g.GetComponent<MeshFilter>().sharedMesh = lumpMesh;
                g.AddComponent<BoxCollider>(); // fits the mesh bounds
                g.transform.localScale = Vector3.one * (Random.Range(sizeRange.x, sizeRange.y) / lumpMesh.bounds.size.magnitude);
            }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "Shard";
                g.transform.localScale = new Vector3(Random.Range(sizeRange.x, sizeRange.y), 0.008f, Random.Range(sizeRange.x, sizeRange.y));
            }
            g.transform.SetParent(s.transform, true);
            g.transform.SetPositionAndRotation(center + Vector3.Scale(Random.insideUnitSphere, spread), Random.rotation);
            g.GetComponent<Renderer>().sharedMaterial = material;
            var rb = g.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = lumpMesh ? 2f : 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // 8 mm and fast: tunnels through the floor otherwise
            rb.linearVelocity = velocity + Random.onUnitSphere * Random.Range(0.5f, 2.5f);
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            s.shards.Add(rb);
        }
    }

    public static void Sound(AudioClip clip, Vector3 position, float volume, float fullVolumeDistance)
    {
        if (!clip) return;
        var a = new GameObject("BreakSound").AddComponent<AudioSource>();
        a.transform.position = position;
        a.clip = clip;
        a.volume = volume;
        a.spatialBlend = 0.8f; // mostly 3D so you can tell where it broke, but never faint
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = fullVolumeDistance;
        a.maxDistance = 40f;
        a.Play();
        Destroy(a.gameObject, clip.length + 0.1f);
    }

    void FixedUpdate()
    {
        var g = (ship ? ship.LevelRotation : Quaternion.identity) * Physics.gravity;
        foreach (var rb in shards)
        {
            if (!rb) continue;
            // A few start overlapping the floor and get pushed out underneath; don't let them fall forever.
            if (rb.position.y < lostBelowY) { Destroy(rb.gameObject); continue; }
            rb.AddForce(g, ForceMode.Acceleration);
        }
    }
}
