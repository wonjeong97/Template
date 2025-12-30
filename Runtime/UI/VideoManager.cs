using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Wonjeong.UI
{
    public class VideoManager : MonoBehaviour
    {
        private static VideoManager _instance;
        private readonly List<RenderTexture> _activeRenderTextures = new List<RenderTexture>();

        public static VideoManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<VideoManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("VideoManager");
                        _instance = go.AddComponent<VideoManager>();
                    }
                }

                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        /// <summary> 플랫폼별 호환성을 고려하여 재생 가능한 URL을 반환합니다. </summary>
        public string ResolvePlayableUrl(string relativePath)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath).Replace("\\", "/");

            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Windows에서 webm 문제 대응을 위해 mp4 우선 확인 로직 (필요 시)
            if (relativePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
            {
                string mp4Path = Path.ChangeExtension(fullPath, ".mp4");
                if (File.Exists(mp4Path)) return new Uri(mp4Path).AbsoluteUri;
            }
            #endif
            return new Uri(fullPath).AbsoluteUri;
        }

        /// <summary> VideoPlayer와 RawImage를 위한 RenderTexture를 생성하고 연결합니다. </summary>
        public RenderTexture WireRawImageAndRenderTexture(VideoPlayer vp, RawImage raw, Vector2Int size)
        {
            if (vp == null)
            {
                Debug.LogError("VideoPlayer cannot be null");
                return null;
            }
            
            // 기존 텍스처 해제
            if (vp.targetTexture != null)
            {
                _activeRenderTextures.Remove(vp.targetTexture);
                vp.targetTexture.Release();
                Destroy(vp.targetTexture);
            }
            
            // Unity RenderTexture 최소 크기는 2x2
            int rtW = Mathf.Max(2, size.x);
            int rtH = Mathf.Max(2, size.y);
            RenderTexture rTex = new RenderTexture(rtW, rtH, 24);
            rTex.Create();
            _activeRenderTextures.Add(rTex);

            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rTex;
            if (raw != null) raw.texture = rTex;

            return rTex;
        }

        /// <summary> 비디오를 준비(Prepare)하고 완료되면 재생합니다. </summary>
        public IEnumerator PrepareAndPlayRoutine(VideoPlayer vp, string url, AudioSource audio, float volume)
        {
            if (vp == null)
            {
                Debug.LogError("VideoPlayer cannot be null");
                yield break;
            }
    
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("Video URL cannot be null or empty");
                yield break;
            }
    
            vp.source = VideoSource.Url;
            vp.url = url;
            vp.playOnAwake = false;

            if (audio)
            {
                vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
                vp.EnableAudioTrack(0, true);
                vp.SetTargetAudioSource(0, audio);
                audio.volume = Mathf.Clamp01(volume);
            }

            // 에러 추적을 위한 플래그
            bool hasError = false;
            string errorMessage = string.Empty;
    
            VideoPlayer.ErrorEventHandler errorHandler = (VideoPlayer source, string message) =>
            {
                hasError = true;
                errorMessage = message;
            };
    
            // 에러 이벤트 구독
            vp.errorReceived += errorHandler;

            vp.Prepare();

            // 타임아웃 설정 (30초)
            float timeoutSeconds = 30f;
            float elapsedTime = 0f;
    
            while (!vp.isPrepared && elapsedTime < timeoutSeconds && !hasError)
            {
                elapsedTime += Time.deltaTime;
                yield return null;
            }
    
            // 이벤트 구독 해제
            vp.errorReceived -= errorHandler;
    
            // 에러 체크
            if (hasError)
            {
                Debug.LogError($"Video preparation failed: {url}. Error: {errorMessage}");
                yield break;
            }
    
            // 타임아웃 체크
            if (!vp.isPrepared)
            {
                Debug.LogError($"Video preparation timed out after {timeoutSeconds}s: {url}");
                yield break;
            }

            vp.Play();
        }

        private void OnDestroy()
        {
            // 모든 생성된 RenderTexture 정리
            foreach (RenderTexture rt in _activeRenderTextures)
            {
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            }

            _activeRenderTextures.Clear();
        }
    }
}