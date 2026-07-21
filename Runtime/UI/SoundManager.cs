using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.Networking;
using VContainer;
using Wonjeong.Data;
using ZLogger;

namespace Wonjeong.UI
{
    public class SoundManager : MonoBehaviour
    {
        private AudioSource _bgmSource;
        private AudioSource _sfxSource;

        private CancellationTokenSource _bgmFadeCts;

        private readonly Dictionary<string, SoundSetting> _soundSettings = new Dictionary<string, SoundSetting>();
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        private readonly Dictionary<string, UniTask<AudioClip>> _activeDownloads =
            new Dictionary<string, UniTask<AudioClip>>();

        private ILogger<SoundManager> _logger;
        private AppSettingsProvider _settingsProvider;

        /// <summary>
        /// VContainer 의존성 주입.
        /// ZLogger 및 설정 제공자 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<SoundManager> logger, AppSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        /// <summary>
        /// 씬 전환 시 사운드 끊김을 방지하고 오디오 소스를 구성함.
        /// </summary>
        private void Awake()
        {
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
            InitSources();
        }

        /// <summary>
        /// 설정을 로드함. 의존성 주입은 Awake 이후에 완료되므로 Start에서 호출함.
        /// </summary>
        private void Start()
        {
            LoadSoundSettingsAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// BGM과 SFX를 분리하여 독립적인 볼륨 제어 및 루프 설정을 구성함.
        /// </summary>
        private void InitSources()
        {
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;
        }

        /// <summary>
        /// JSON 설정 파일에서 사운드 데이터를 비동기로 읽어와 딕셔너리에 캐싱함.
        /// WebGL 등 URL 기반 플랫폼 지원을 위해 비동기로 동작함.
        /// </summary>
        private async UniTask LoadSoundSettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                Settings settings = await _settingsProvider.GetAsync(cancellationToken);

                if (settings?.sounds == null) return;

                foreach (SoundSetting s in settings.sounds)
                {
                    if (!_soundSettings.ContainsKey(s.key))
                    {
                        _soundSettings.Add(s.key, s);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 정상적인 취소
            }
        }

        #region Public Methods (Play / Stop / Fade)

        /// <summary>
        /// 지정된 키의 배경음을 비동기로 재생함.
        /// 진행 중인 페이드 효과가 있다면 강제 취소하여 오작동을 방지함.
        /// </summary>
        public void PlayBGM(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;

            CancelFadeRoutine();
            LoadAndPlayAsync(setting, _bgmSource, true, this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// 지정된 키의 효과음을 비동기로 재생함.
        /// </summary>
        public void PlaySFX(string key)
        {
            if (!_soundSettings.TryGetValue(key, out SoundSetting setting)) return;

            LoadAndPlayAsync(setting, _sfxSource, false, this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// 재생 중인 배경음을 즉시 정지하고 진행 중인 연출을 취소함.
        /// </summary>
        public void StopBGM()
        {
            CancelFadeRoutine();
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
        /// 이전 페이드 토큰을 파기하고 새로운 비동기 사이클을 시작함.
        /// </summary>
        public void FadeOutBGM(float duration)
        {
            if (!_bgmSource || !_bgmSource.isPlaying) return;

            CancelFadeRoutine();
            _bgmFadeCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            // 토큰 자체가 아닌 CTS 객체를 넘겨 완료 후 자체 파기하도록 구성
            FadeOutAsync(duration, _bgmFadeCts).Forget();
        }

        /// <summary>
        /// 캐시된 모든 오디오 클립의 메모리를 강제 해제함.
        /// <para>
        /// 주의: 용량 초과 시 자동 축출(LRU)을 적용하지 않음. 캐시에 담기는 키는
        /// Settings.json의 sounds[] 목록으로 이미 상한이 정해져 있어 무한히 증가하지 않으며,
        /// 재생 중인 AudioClip을 임의로 파괴하면 소리가 끊기는 더 심각한 문제가 발생하기 때문임.
        /// (AudioSource.clip이 참조 중인 클립을 Destroy하면 재생이 중단됨)
        /// 따라서 해제 시점은 씬 전환 등 안전한 지점에서 호출자가 명시적으로 결정해야 함.
        /// UIManager.ClearSpriteCache()도 동일한 이유로 같은 방식을 따름.
        /// </para>
        /// </summary>
        public void ClearCache()
        {
            foreach (AudioClip clip in _clipCache.Values)
            {
                if (clip) Destroy(clip);
            }

            _clipCache.Clear();
            if (_logger != null) _logger.ZLogInformation($"[SoundManager] Audio cache cleared.");
        }

        #endregion

        #region Internal Async Methods

        /// <summary>
        /// 기존 페이드아웃 비동기 루프를 안전하게 강제 취소하고 리소스를 해제함.
        /// </summary>
        private void CancelFadeRoutine()
        {
            if (_bgmFadeCts != null)
            {
                _bgmFadeCts.Cancel();
                _bgmFadeCts.Dispose();
                _bgmFadeCts = null;
            }
        }

        /// <summary>
        /// 프레임 단위 보간을 통해 볼륨을 줄이는 페이드아웃 핵심 로직.
        /// 코루틴 대신 UniTask를 사용하여 GC 할당 없이 실행됨.
        /// </summary>
        private async UniTaskVoid FadeOutAsync(float duration, CancellationTokenSource cts)
        {
            float startVolume = _bgmSource.volume;
            float timer = 0f;

            if (duration <= 0f) duration = 0.01f;

            try
            {
                while (timer < duration)
                {
                    // Time.timeScale이 0이면 deltaTime도 0이라 페이드가 끝나지 않고
                    // BGM이 줄어들다 만 상태로 멈춤. 사운드 연출은 게임 시간과 무관해야 하므로
                    // unscaledDeltaTime을 사용함.
                    timer += Time.unscaledDeltaTime;
                    _bgmSource.volume = Mathf.Lerp(startVolume, 0f, timer / duration);
                    await UniTask.Yield(PlayerLoopTiming.Update, cts.Token);
                }

                _bgmSource.volume = 0f;
                _bgmSource.Stop();
            }
            catch (OperationCanceledException)
            {
                if (_logger != null) _logger.ZLogInformation($"[SoundManager] BGM fade transition canceled.");
            }
            finally
            {
                // 페이드가 자연 완료되거나 예외가 발생해도 할당된 토큰 소스를 무조건 메모리에서 해제함.
                if (_bgmFadeCts == cts)
                {
                    _bgmFadeCts.Dispose();
                    _bgmFadeCts = null;
                }
            }
        }

        /// <summary>
        /// 오디오 클립을 비동기로 로드하고 조건에 맞춰 오디오 소스에 할당함.
        /// </summary>
        private async UniTaskVoid LoadAndPlayAsync(SoundSetting setting, AudioSource source, bool isBGM,
            CancellationToken cancellationToken)
        {
            AudioClip targetClip = null;

            if (_clipCache.TryGetValue(setting.key, out AudioClip cachedClip))
            {
                targetClip = cachedClip;
            }
            else
            {
                targetClip = await DownloadAndCacheClipAsync(setting, cancellationToken);
            }

            if (!targetClip)
            {
                if (_logger != null) _logger.ZLogWarning($"[SoundManager] targetClip is null. Cannot play sound.");
                return;
            }

            ApplyClipToSource(targetClip, setting, source, isBGM);
        }

        /// <summary>
        /// 로컬 경로에서 오디오 클립을 비동기로 다운로드하고 캐시에 저장함.
        /// 여러 스레드나 프레임에서 동일한 파일을 동시에 요청할 경우 중복 로드를 방지함.
        /// </summary>
        private async UniTask<AudioClip> DownloadAndCacheClipAsync(SoundSetting setting,
            CancellationToken cancellationToken)
        {
            // 이미 동일한 파일의 다운로드가 진행 중이라면, 새로운 I/O를 발생시키지 않고 기존 작업의 완료를 함께 대기함.
            if (_activeDownloads.TryGetValue(setting.key, out UniTask<AudioClip> ongoingTask))
            {
                return await ongoingTask;
            }

            // 새로운 다운로드 태스크를 생성하여 추적 딕셔너리에 등록함.
            UniTask<AudioClip> downloadTask = ExecuteDownloadAsync(setting, cancellationToken);
            _activeDownloads[setting.key] = downloadTask;

            AudioClip resultClip = await downloadTask;

            // 로딩이 완료되면 추적 딕셔너리에서 제거함.
            _activeDownloads.Remove(setting.key);

            return resultClip;
        }

        /// <summary>
        /// 실제 UnityWebRequest를 통한 파일 I/O 및 오디오 클립 추출을 수행함.
        /// </summary>
        private async UniTask<AudioClip> ExecuteDownloadAsync(SoundSetting setting, CancellationToken cancellationToken)
        {
            string path = Path.Combine(Application.streamingAssetsPath, setting.clipPath).Replace("\\", "/");
            // WebGL/Android에서는 streamingAssetsPath가 이미 URL이므로 로컬 경로일 때만 file:// 접두어를 붙임.
            string uri = path.Contains("://") ? path : ZString.Concat("file://", path);

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, GetAudioType(path)))
            {
                try
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        if (_logger != null)
                            _logger.ZLogError($"[SoundManager] Failed to load sound: {path} / {www.error}");
                        return null;
                    }

                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

                    if (!clip)
                    {
                        if (_logger != null) _logger.ZLogWarning($"[SoundManager] Downloaded clip is null.");
                        return null;
                    }

                    clip.name = setting.key;

                    if (!_clipCache.ContainsKey(setting.key))
                    {
                        _clipCache.Add(setting.key, clip);
                    }

                    return clip;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 오디오 클립을 오디오 소스에 할당하고 재생함.
        /// </summary>
        private void ApplyClipToSource(AudioClip clip, SoundSetting setting, AudioSource source, bool isBGM)
        {
            if (!source)
            {
                if (_logger != null) _logger.ZLogWarning($"[SoundManager] source is null. Cannot apply clip.");
                return;
            }

            if (isBGM)
            {
                PlayBGMClip(clip, setting, source);
            }
            else
            {
                source.PlayOneShot(clip, setting.volume);
            }
        }

        /// <summary>
        /// 루프 및 볼륨 설정에 맞춰 BGM 클립을 교체하거나 이어서 재생함.
        /// </summary>
        private void PlayBGMClip(AudioClip clip, SoundSetting setting, AudioSource source)
        {
            if (source.clip == clip && source.isPlaying)
            {
                source.volume = setting.volume;
                return;
            }

            source.clip = clip;
            source.volume = setting.volume;
            source.Play();
        }

        /// <summary>
        /// 파일 확장자 문자열을 분석하여 유니티 내장 오디오 타입 포맷으로 변환함.
        /// </summary>
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

        #endregion

        private void OnDestroy()
        {
            CancelFadeRoutine();
            ClearCache();
        }
    }
}