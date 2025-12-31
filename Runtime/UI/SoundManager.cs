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
        
        // 사운드 설정 데이터를 키값으로 관리
        private readonly Dictionary<string, SoundSetting> _soundSettings = new Dictionary<string, SoundSetting>();
        // 로드된 클립 캐시
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
            // BGM용 오디오 소스 설정
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            // SFX용 오디오 소스 설정
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;
        }

        private void LoadSoundSettings()
        {
            // Settings.json 로드 및 딕셔너리 구성
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

        #region Public Methods (Play / Stop)

        /// <summary> BGM을 재생합니다. (하나만 루프 재생) </summary>
        public void PlayBGM(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;
            
            StartCoroutine(LoadAndPlayRoutine(setting, _bgmSource, true));
        }

        /// <summary> 효과음을 재생합니다. (중첩 가능) </summary>
        public void PlaySFX(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;

            StartCoroutine(LoadAndPlayRoutine(setting, _sfxSource, false));
        }

        public void StopBGM() => _bgmSource.Stop();
        public void StopSFX() => _sfxSource.Stop();
        
        public void ClearCache()
        {
            foreach (AudioClip clip in _clipCache.Values)
            {
                if (clip != null) Resources.UnloadAsset(clip); // 메모리 해제
            }
            _clipCache.Clear();
            Debug.Log("[SoundManager] Audio cache cleared.");
        }

        #endregion

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
                // 캐시가 꽉 찼다면 가장 오래된 것 중 하나를 삭제
                if (_clipCache.Count >= MAX_CACHE_COUNT)
                {
                    string firstKey = _clipCache.Keys.First();
                    AudioClip oldClip = _clipCache[firstKey];
                    _clipCache.Remove(firstKey);
                    if (oldClip != null) Resources.UnloadAsset(oldClip);
                }
                
                // 2. StreamingAssets에서 로드 (UnityWebRequest 사용)
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

            // 3. 재생 처리
            if (clip != null)
            {
                if (isBGM)
                {
                    if (source.clip == clip && source.isPlaying) yield break; // 이미 재생 중이면 무시
                    source.clip = clip;
                    source.volume = setting.volume;
                    source.Play();
                }
                else
                {
                    // 효과음은 한 번에 여러 개가 겹쳐 나올 수 있도록 PlayOneShot 사용
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
            ClearCache(); // 파괴 시 메모리 정리
        }
    }
}