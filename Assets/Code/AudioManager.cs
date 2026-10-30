using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const float NearPeopleZoomThreshold = 12f;
    private const float AmbientFadeSpeed = 0.8f;
    private const float ConstructionAudibleRadius = 35f;

    private AudioSource ambientSource;
    private AudioSource nearPeopleSource;
    private AudioSource uiSource;
    private AudioSource worldSource;
    private Camera mainCam;
    private AudioClip outsideBirdClip;
    private AudioClip nearPeopleClip;
    private AudioClip notificationClip;
    private AudioClip selectClip;
    private AudioClip hammerClip;
    private bool nearPeopleStarted;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        mainCam = Camera.main;

        outsideBirdClip = Resources.Load<AudioClip>("Sound/outsideandbird");
        nearPeopleClip = Resources.Load<AudioClip>("Sound/nearpeople");
        notificationClip = Resources.Load<AudioClip>("Sound/notification");
        selectClip = Resources.Load<AudioClip>("Sound/select");
        hammerClip = Resources.Load<AudioClip>("Sound/multiplehammerhit");

        ambientSource = CreateSource("AmbientBirds", true, 0.45f);
        nearPeopleSource = CreateSource("NearPeople", true, 0f);
        uiSource = CreateSource("UISfx", false, 1f);
        worldSource = CreateSource("WorldSfx", false, 0.9f);

        ambientSource.clip = outsideBirdClip;
        nearPeopleSource.clip = nearPeopleClip;

        if (ambientSource.clip != null)
        {
            ambientSource.Play();
        }
    }

    private void Update()
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
        }

        // Ambient-Prüfung drosseln (vorher pro Frame inkl. Villager-Suche)
        ambientTimer -= Time.deltaTime;
        if (ambientTimer <= 0f)
        {
            ambientTimer = 0.25f;
            UpdateNearPeopleAmbient();
        }
    }

    private float ambientTimer;

    public void PlaySelectSound()
    {
        PlayClipFromOffset(uiSource, selectClip, 0.22f, 0.8f);
    }

    public void PlayNotificationSound()
    {
        PlayOneShot(uiSource, notificationClip, 0.9f);
    }

    public void PlayConstructionSound(Vector3 worldPosition)
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
        }

        if (mainCam == null)
        {
            return;
        }

        float audibleRadius = Mathf.Max(ConstructionAudibleRadius, mainCam.orthographicSize * 1.4f);
        if (Vector2.Distance(mainCam.transform.position, worldPosition) > audibleRadius)
        {
            return;
        }

        PlayOneShot(worldSource, hammerClip, 0.4f);
    }

    private AudioSource CreateSource(string sourceName, bool loop, float volume)
    {
        GameObject sourceGO = new GameObject(sourceName);
        sourceGO.transform.SetParent(transform, false);
        AudioSource source = sourceGO.AddComponent<AudioSource>();
        source.loop = loop;
        source.playOnAwake = false;
        source.volume = volume;
        source.spatialBlend = 0f;
        return source;
    }

    private void UpdateNearPeopleAmbient()
    {
        if (nearPeopleSource == null || nearPeopleSource.clip == null || mainCam == null)
        {
            return;
        }

        bool shouldPlay = mainCam.orthographicSize <= NearPeopleZoomThreshold && HasUnitsNearCamera();
        float targetVolume = shouldPlay ? 0.38f : 0f;

        if (shouldPlay && !nearPeopleStarted)
        {
            nearPeopleSource.time = Random.Range(0f, Mathf.Max(0.1f, nearPeopleSource.clip.length - 0.5f));
            nearPeopleSource.Play();
            nearPeopleStarted = true;
        }

        nearPeopleSource.volume = Mathf.MoveTowards(nearPeopleSource.volume, targetVolume, Time.deltaTime * AmbientFadeSpeed);

        if (nearPeopleStarted && nearPeopleSource.volume <= 0.001f && !shouldPlay)
        {
            nearPeopleSource.Stop();
            nearPeopleStarted = false;
        }
    }

    private bool HasUnitsNearCamera()
    {
        Vector2 camPos = mainCam.transform.position;
        float radius = mainCam.orthographicSize * 1.3f;

        for (int i = 0; i < Soldier.ActiveSoldiers.Count; i++)
        {
            Soldier soldier = Soldier.ActiveSoldiers[i];
            if (soldier != null && Vector2.Distance(camPos, soldier.Position2D) <= radius)
            {
                return true;
            }
        }

        // Villager über den Manager (kein FindObjectsByType-Allokations-Rauschen pro Ändervorgang)
        if (VillagerManager.Instance != null)
        {
            var activeVillagers = VillagerManager.Instance.ActiveVillagers;
            for (int i = 0; i < activeVillagers.Count; i++)
            {
                Villager v = activeVillagers[i];
                if (v != null && Vector2.Distance(camPos, v.transform.position) <= radius)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void PlayOneShot(AudioSource source, AudioClip clip, float volumeScale)
    {
        if (source == null || clip == null)
        {
            return;
        }

        source.PlayOneShot(clip, volumeScale);
    }

    private static void PlayClipFromOffset(AudioSource source, AudioClip clip, float startTime, float volume)
    {
        if (source == null || clip == null)
        {
            return;
        }

        GameObject tempSourceObject = new GameObject("TempOffsetAudio");
        AudioSource tempSource = tempSourceObject.AddComponent<AudioSource>();
        tempSource.outputAudioMixerGroup = source.outputAudioMixerGroup;
        tempSource.spatialBlend = 0f;
        tempSource.loop = false;
        tempSource.playOnAwake = false;
        tempSource.volume = volume;
        tempSource.clip = clip;
        tempSource.time = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, clip.length - 0.05f));
        tempSource.Play();
        Object.Destroy(tempSourceObject, Mathf.Max(0.2f, clip.length - tempSource.time));
    }
}
