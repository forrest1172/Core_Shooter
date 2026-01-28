using UnityEngine;

public enum TacticalSfxId : byte
{
    Footstep_Default,
    Footstep_Metal,
    Footstep_Wood,
    JumpLand,
    Gunshot,
    Death,
    PickUp,
    Drop
}

public class TacticalAudioManager : MonoBehaviour
{
    public static TacticalAudioManager Instance;

    [System.Serializable]
    public struct SfxEntry
    {
        public TacticalSfxId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
        public float minDistance;
        public float maxDistance;
    }

    [SerializeField] private SfxEntry[] sfx;
    [Header("Optional Occlusion")]
    [SerializeField] private bool useOcclusion = true;
    [SerializeField] private LayerMask occlusionMask = ~0;
    [SerializeField] private float occludedVolumeMultiplier = 0.35f;

    void Awake() => Instance = this;

    bool TryGet(TacticalSfxId id, out SfxEntry entry)
    {
        foreach (var e in sfx)
            if (e.id == id) { entry = e; return true; }
        entry = default;
        return false;
    }

    public void Play3D(TacticalSfxId id, Vector3 pos, float loudness01)
    {
        if (!TryGet(id, out var entry) || entry.clip == null) return;

        var listener = FindListener();
        if (listener == null)
        {
            Debug.LogWarning("NO AUDIO LISTENER FOUND");
            return;
        }

        float vol = Mathf.Clamp01(entry.volume * loudness01);

        if (useOcclusion)
        {
            //var listener = FindListener();
            if (listener != null)
            {
                Vector3 from = listener.position;
                Vector3 to = pos;
                Vector3 dir = to - from;
                float dist = dir.magnitude;
                if (dist > 0.05f)
                {
                    if (Physics.Raycast(from, dir / dist, dist, occlusionMask, QueryTriggerInteraction.Ignore))
                        vol *= occludedVolumeMultiplier;
                }
            }
        }

        var go = new GameObject($"SFX_{id}");
        go.transform.position = pos;

        var src = go.AddComponent<AudioSource>();
        src.clip = entry.clip;
        src.volume = vol;
        src.spatialBlend = 1f;
        src.minDistance = entry.minDistance;
        src.maxDistance = entry.maxDistance;
        src.rolloffMode = AudioRolloffMode.Logarithmic;

        src.Play();
        Destroy(go, entry.clip.length + 0.1f);
    }

    Transform FindListener()
    {
        var al = FindFirstObjectByType<AudioListener>();
        return al != null ? al.transform : null;
    }
}

