using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Wonjeong.Data;
using Wonjeong.Utils;

namespace Wonjeong.UI
{
    public class SoundManager : MonoBehaviour
    {
        private static SoundManager _instance;
        public static SoundManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SoundManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("SoundManager");
                        _instance = go.AddComponent<SoundManager>();
                    }
                }
                return _instance;
            }
        }

        private AudioSource _bgmSource;
        private AudioSource _sfxSource;

        // BGM 페이드아웃 코루틴 참조 변수 (중복 실행 방지용)
        private Coroutine _bgmFadeRoutine;

        private readonly Dictionary<string, SoundSetting> _soundSettings = new Dictionary<string, SoundSetting>();
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        private const int MAX_CACHE_COUNT = 20;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                InitSources();
                LoadSoundSettings();
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void InitSources()
        {
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;
        }

        private void LoadSoundSettings()
        {
            Settings settings = JsonLoader.Load<Settings>("Settings.json");
            if (settings != null && settings.sounds != null)
            {
                foreach (var s in settings.sounds)
                {
                    if (!_soundSettings.ContainsKey(s.key))
                        _soundSettings.Add(s.key, s);
                }
            }
        }

        #region Public Methods (Play / Stop / Fade)

        public void PlayBGM(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;

            // 새 BGM 재생 시 기존 페이드아웃 중단
            if (_bgmFadeRoutine != null)
            {
                StopCoroutine(_bgmFadeRoutine);
                _bgmFadeRoutine = null;
            }

            StartCoroutine(LoadAndPlayRoutine(setting, _bgmSource, true));
        }

        public void PlaySFX(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;

            StartCoroutine(LoadAndPlayRoutine(setting, _sfxSource, false));
        }

        public void StopBGM()
        {
            // 정지 시 페이드아웃 중단
            if (_bgmFadeRoutine != null)
            {
                StopCoroutine(_bgmFadeRoutine);
                _bgmFadeRoutine = null;
            }
            _bgmSource.Stop();
        }

        public void StopSFX() => _sfxSource.Stop();

        /// <summary>
        /// BGM을 지정된 시간에 걸쳐 서서히 줄이고 정지합니다.
        /// </summary>
        /// <param name="duration">페이드아웃 소요 시간(초)</param>
        public void FadeOutBGM(float duration)
        {
            if (!_bgmSource.isPlaying) return;

            // 이미 페이드 중이라면 취소하고 새로 시작
            if (_bgmFadeRoutine != null) StopCoroutine(_bgmFadeRoutine);

            _bgmFadeRoutine = StartCoroutine(FadeOutRoutine(duration));
        }

        public void ClearCache()
        {
            foreach (AudioClip clip in _clipCache.Values)
            {
                if (clip != null) Resources.UnloadAsset(clip);
            }
            _clipCache.Clear();
            Debug.Log("[SoundManager] Audio cache cleared.");
        }

        #endregion

        // --- Coroutines ---

        private IEnumerator FadeOutRoutine(float duration)
        {
            float startVolume = _bgmSource.volume;
            float timer = 0f;

            if (duration <= 0f) duration = 0.01f; // 0 나누기 방지

            while (timer < duration)
            {
                timer += Time.deltaTime;
                // 현재 볼륨에서 0까지 선형 보간
                _bgmSource.volume = Mathf.Lerp(startVolume, 0f, timer / duration);
                yield return null;
            }

            _bgmSource.volume = 0f;
            _bgmSource.Stop();
            _bgmFadeRoutine = null;
        }

        private IEnumerator LoadAndPlayRoutine(SoundSetting setting, AudioSource source, bool isBGM)
        {
            AudioClip clip = null;

            // 1. 캐시 확인
            if (_clipCache.TryGetValue(setting.key, out AudioClip cachedClip))
            {
                clip = cachedClip;
            }
            else
            {
                // 캐시 관리
                if (_clipCache.Count >= MAX_CACHE_COUNT)
                {
                    string firstKey = _clipCache.Keys.First();
                    AudioClip oldClip = _clipCache[firstKey];
                    _clipCache.Remove(firstKey);
                    if (oldClip != null) Resources.UnloadAsset(oldClip);
                }

                // 2. 로드
                string path = Path.Combine(Application.streamingAssetsPath, setting.clipPath).Replace("\\", "/");
                string uri = "file://" + path;

                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, GetAudioType(path)))
                {
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        clip = DownloadHandlerAudioClip.GetContent(www);
                        clip.name = setting.key;
                        _clipCache.Add(setting.key, clip);
                    }
                    else
                    {
                        Debug.LogError($"[SoundManager] Failed to load sound: {path} / {www.error}");
                        yield break;
                    }
                }
            }

            // 3. 재생
            if (clip != null)
            {
                if (isBGM)
                {
                    // 같은 곡 재생 중이면 무시 (볼륨만 원복)
                    if (source.clip == clip && source.isPlaying)
                    {
                        source.volume = setting.volume; // 페이드아웃 중이었다면 볼륨 복구
                        yield break;
                    }

                    source.clip = clip;
                    source.volume = setting.volume; // 볼륨 초기화
                    source.Play();
                }
                else
                {
                    source.PlayOneShot(clip, setting.volume);
                }
            }
        }

        private AudioType GetAudioType(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".wav" => AudioType.WAV,
                ".mp3" => AudioType.MPEG,
                ".ogg" => AudioType.OGGVORBIS,
                _ => AudioType.UNKNOWN
            };
        }

        private void OnDestroy()
        {
            ClearCache();
        }
    }
}