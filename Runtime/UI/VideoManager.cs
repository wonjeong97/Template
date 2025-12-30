using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Wonjeong.UI
{
    public class VideoManager : MonoBehaviour
    {
        private static VideoManager _instance;
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
            // 기존 텍스처 해제
            if (vp.targetTexture != null)
            {
                vp.targetTexture.Release();
                Destroy(vp.targetTexture);
            }

            int rtW = Mathf.Max(2, size.x);
            int rtH = Mathf.Max(2, size.y);
            RenderTexture rTex = new RenderTexture(rtW, rtH, 24);
            rTex.Create();

            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rTex;
            if (raw != null) raw.texture = rTex;

            return rTex;
        }

        /// <summary> 비디오를 준비(Prepare)하고 완료되면 재생합니다. </summary>
        public IEnumerator PrepareAndPlayRoutine(VideoPlayer vp, string url, AudioSource audio, float volume)
        {
            vp.source = VideoSource.Url;
            vp.url = url;
            vp.playOnAwake = false;

            if (audio != null)
            {
                vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
                vp.EnableAudioTrack(0, true);
                vp.SetTargetAudioSource(0, audio);
                audio.volume = Mathf.Clamp01(volume);
            }

            vp.Prepare();

            // 고용량 비디오(100MB+)의 경우 Prepare 시간이 걸릴 수 있으므로 완료될 때까지 대기
            while (!vp.isPrepared)
            {
                yield return null;
            }

            vp.Play();
            if (audio != null && audio.volume > 0) audio.Play();
        }
    }
}