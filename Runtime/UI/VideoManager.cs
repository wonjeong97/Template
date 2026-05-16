using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using VContainer;
using ZLogger;

namespace Wonjeong.UI
{
    public class VideoManager : MonoBehaviour
    {
        private readonly List<RenderTexture> _activeRenderTextures = new List<RenderTexture>();
        private ILogger<VideoManager> _logger;

        /// <summary>
        /// VContainer 의존성 주입.
        /// 로거 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<VideoManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 씬 전환 시 비디오 매니저 파괴를 방지함.
        /// </summary>
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 플랫폼별 호환성을 고려하여 재생 가능한 URL을 반환함.
        /// 윈도우 환경의 webm 미지원 이슈를 우회하기 위함.
        /// </summary>
        public string ResolvePlayableUrl(string relativePath)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath).Replace("\\", "/");

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (relativePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
            {
                string mp4Path = Path.ChangeExtension(fullPath, ".mp4");
                if (File.Exists(mp4Path)) return new Uri(mp4Path).AbsoluteUri;
            }
#endif
            return new Uri(fullPath).AbsoluteUri;
        }

        /// <summary>
        /// 비디오 플레이어와 UI 이미지를 연결할 렌더 텍스처를 동적으로 생성함.
        /// 런타임 해상도 대응 및 독립적인 메모리 관리를 위함.
        /// </summary>
        public RenderTexture WireRawImageAndRenderTexture(VideoPlayer vp, RawImage raw, Vector2Int size)
        {
            if (!vp)
            {
                if (_logger != null) _logger.ZLogError($"[VideoManager] VideoPlayer cannot be null.");
                return null;
            }
            
            if (vp.targetTexture)
            {
                _activeRenderTextures.Remove(vp.targetTexture);
                vp.targetTexture.Release();
                Destroy(vp.targetTexture);
            }
            
            int rtW = Mathf.Max(2, size.x);
            int rtH = Mathf.Max(2, size.y);
            RenderTexture rTex = new RenderTexture(rtW, rtH, 24);
            rTex.Create();
            _activeRenderTextures.Add(rTex);

            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rTex;
            
            if (raw) raw.texture = rTex;

            return rTex;
        }

        /// <summary>
        /// 비디오 스트림을 비동기로 준비하고 완료 시 재생을 시작함.
        /// </summary>
        public async UniTask PrepareAndPlayAsync(VideoPlayer vp, string url, AudioSource audioSource, float volume, CancellationToken cancellationToken)
        {
            if (!vp)
            {
                if (_logger != null) _logger.ZLogError($"[VideoManager] VideoPlayer cannot be null.");
                return;
            }
    
            if (string.IsNullOrEmpty(url))
            {
                if (_logger != null) _logger.ZLogError($"[VideoManager] Video URL cannot be null or empty.");
                return;
            }
    
            vp.source = VideoSource.Url;
            vp.url = url;
            vp.playOnAwake = false;

            if (audioSource)
            {
                vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
                vp.EnableAudioTrack(0, true);
                vp.SetTargetAudioSource(0, audioSource);
                audioSource.volume = Mathf.Clamp01(volume);
            }

            bool hasError = false;
            string errorMessage = string.Empty;
    
            VideoPlayer.ErrorEventHandler errorHandler = (_, message) =>
            {
                hasError = true;
                errorMessage = message;
            };
    
            vp.errorReceived += errorHandler;
            vp.Prepare();

            float timeoutSeconds = 30f;
            float elapsedTime = 0f;
    
            try
            {
                while (!vp.isPrepared && elapsedTime < timeoutSeconds && !hasError)
                {
                    elapsedTime += Time.deltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                if (_logger != null) _logger.ZLogInformation($"[VideoManager] Video preparation canceled.");
                return; // finally 블록으로 이동하여 안전하게 해제됨
            }
            finally
            {
                // try 블록 안에서 무슨 일이 발생하든 무조건 이벤트 구독을 해제하여 메모리 누수 방지
                if (vp)
                {
                    vp.errorReceived -= errorHandler;
                }
            }
    
            if (hasError)
            {
                if (_logger != null) _logger.ZLogError($"[VideoManager] Video preparation failed: {url}. Error: {errorMessage}");
                return;
            }
    
            if (!vp.isPrepared)
            {
                if (_logger != null) _logger.ZLogError($"[VideoManager] Video preparation timed out after {timeoutSeconds}s: {url}");
                return;
            }

            vp.Play();
        }

        /// <summary>
        /// 생성된 모든 렌더 텍스처 메모리를 완전 해제함.
        /// VRAM 누수 방지 목적.
        /// </summary>
        private void OnDestroy()
        {
            foreach (RenderTexture rt in _activeRenderTextures)
            {
                if (rt)
                {
                    rt.Release();
                    Destroy(rt);
                }
            }

            _activeRenderTextures.Clear();
        }
    }
}