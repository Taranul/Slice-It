using UnityEngine;

namespace SliceIt.Core
{
    /// <summary>
    /// Manages arena background music loop and procedural/asset audio effects.
    /// Integrates Assets/_Project/Music/Main Soundtrack.wav.
    /// </summary>
    public class ArenaAudioManager : MonoBehaviour
    {
        public static ArenaAudioManager Instance { get; private set; }

        [Header("Music")]
        [SerializeField] private AudioClip soundtrackClip;
        [SerializeField] private float musicVolume = 0.45f;

        private AudioSource musicSource;
        private AudioSource sfxSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.volume = musicVolume;
            musicSource.playOnAwake = false;

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.volume = 0.7f;

            if (soundtrackClip == null)
            {
                soundtrackClip = Resources.Load<AudioClip>("Main Soundtrack");
            }
        }

        private void Start()
        {
            PlaySoundtrack();
        }

        public void PlaySoundtrack()
        {
            if (soundtrackClip != null && musicSource != null)
            {
                musicSource.clip = soundtrackClip;
                musicSource.Play();
            }
        }

        public void PlaySfx(AudioClip clip, float volume = 0.7f)
        {
            if (clip != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(clip, volume);
            }
        }
    }
}
