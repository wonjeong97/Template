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
                if (!_instance)
                {
                    _instance = FindFirstObjectByType<SoundManager>();
                    if (!_instance)
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

        private Coroutine _bgmFadeRoutine;

        private readonly Dictionary<string, SoundSetting> _soundSettings = new Dictionary<string, SoundSetting>();
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        private const int MAX_CACHE_COUNT = 20;

        private void Awake()
        {
            if (!_instance)
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
                foreach (SoundSetting s in settings.sounds)
                {
                    if (!_soundSettings.ContainsKey(s.key))
                    {
                        _soundSettings.Add(s.key, s);
                    }
                }
            }
        }

        #region Public Methods (Play / Stop / Fade)

        /// <summary>
        /// 지정된 키의 배경음을 재생함.
        /// </summary>
        /// <param name="key">재생할 사운드의 키값.</param>
        public void PlayBGM(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;

            if (_bgmFadeRoutine != null)
            {
                StopCoroutine(_bgmFadeRoutine);
                _bgmFadeRoutine = null;
            }

            StartCoroutine(LoadAndPlayRoutine(setting, _bgmSource, true));
        }

        /// <summary>
        /// 지정된 키의 효과음을 재생함.
        /// </summary>
        /// <param name="key">재생할 사운드의 키값.</param>
        public void PlaySFX(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;

            StartCoroutine(LoadAndPlayRoutine(setting, _sfxSource, false));
        }

        /// <summary>
        /// 재생 중인 배경음을 즉시 정지함.
        /// </summary>
        public void StopBGM()
        {
            if (_bgmFadeRoutine != null)
            {
                StopCoroutine(_bgmFadeRoutine);
                _bgmFadeRoutine = null;
            }
            if (_bgmSource) _bgmSource.Stop();
        }

        /// <summary>
        /// 재생 중인 효과음을 즉시 정지함.
        /// </summary>
        public void StopSFX()
        {
            if (_sfxSource) _sfxSource.Stop();
        }

        /// <summary>
        /// 배경음을 지정된 시간에 걸쳐 서서히 줄인 후 정지함.
        /// </summary>
        /// <param name="duration">페이드 아웃 소요 시간(초).</param>
        public void FadeOutBGM(float duration)
        {
            if (!_bgmSource || !_bgmSource.isPlaying) return;

            if (_bgmFadeRoutine != null) StopCoroutine(_bgmFadeRoutine);

            _bgmFadeRoutine = StartCoroutine(FadeOutRoutine(duration));
        }

        /// <summary>
        /// 캐시된 모든 오디오 클립의 메모리를 해제하고 딕셔너리를 비움.
        /// Why: 장시간 실행 시 오디오 클립이 메모리에 누적되는 것을 방지하기 위함.
        /// </summary>
        public void ClearCache()
        {
            foreach (AudioClip clip in _clipCache.Values)
            {
                if (clip) Destroy(clip);
            }
            _clipCache.Clear();
            Debug.Log("[SoundManager] 오디오 캐시 메모리를 정리했습니다.");
        }

        #endregion

        // --- Coroutines ---

        /// <summary>
        /// 서서히 볼륨을 줄이는 연출을 수행함.
        /// </summary>
        private IEnumerator FadeOutRoutine(float duration)
        {
            float startVolume = _bgmSource.volume;
            float timer = 0f;

            if (duration <= 0f) duration = 0.01f; 

            while (timer < duration)
            {
                timer += Time.deltaTime;
                _bgmSource.volume = Mathf.Lerp(startVolume, 0f, timer / duration);
                yield return null;
            }

            _bgmSource.volume = 0f;
            _bgmSource.Stop();
            _bgmFadeRoutine = null;
        }

        /// <summary>
        /// 오디오 클립을 비동기로 로드하고 재생함.
        /// Why: 메인 스레드 멈춤 없이 로컬 파일 시스템에서 사운드를 동적으로 스트리밍하기 위함.
        /// </summary>
        private IEnumerator LoadAndPlayRoutine(SoundSetting setting, AudioSource source, bool isBGM)
        {
            AudioClip clip = null;

            if (_clipCache.TryGetValue(setting.key, out AudioClip cachedClip))
            {
                clip = cachedClip;
            }
            else
            {
                if (_clipCache.Count >= MAX_CACHE_COUNT)
                {
                    string firstKey = _clipCache.Keys.First();
                    AudioClip oldClip = _clipCache[firstKey];
                    _clipCache.Remove(firstKey);
                    
                    if (oldClip) Destroy(oldClip);
                }

                string path = Path.Combine(Application.streamingAssetsPath, setting.clipPath).Replace("\\", "/");
                string uri = "file://" + path;

                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, GetAudioType(path)))
                {
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        clip = DownloadHandlerAudioClip.GetContent(www);
                        clip.name = setting.key;
                        
                        // 비동기 다운로드 중 중복 요청으로 인한 딕셔너리 키 충돌 예외를 방지함.
                        if (!_clipCache.ContainsKey(setting.key))
                        {
                            _clipCache.Add(setting.key, clip);
                        }
                    }
                    else
                    {
                        Debug.LogError($"[SoundManager] Failed to load sound: {path} / {www.error}");
                        yield break;
                    }
                }
            }

            if (clip)
            {
                if (isBGM)
                {
                    if (source.clip == clip && source.isPlaying)
                    {
                        source.volume = setting.volume; 
                        yield break;
                    }

                    source.clip = clip;
                    source.volume = setting.volume; 
                    source.Play();
                }
                else
                {
                    source.PlayOneShot(clip, setting.volume);
                }
            }
        }

        /// <summary> 파일 확장자를 기반으로 오디오 타입을 반환함. </summary>
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