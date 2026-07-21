using System;
using System.IO;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Wonjeong.Utils
{
    /// <summary>
    /// JSON 직렬화 및 파일 입출력을 담당하는 정적 유틸리티 클래스.
    /// 모든 데이터 파일 I/O 스트림의 최적화를 수행함.
    /// WebGL/Android처럼 StreamingAssets가 URL인 플랫폼에서는 UnityWebRequest로 로드함.
    /// </summary>
    public static class JsonLoader
    {
        /// <summary>
        /// StreamingAssets 기준 전체 경로를 생성함.
        /// </summary>
        private static string GetPath(string fileName)
        {
            string fullFileName = fileName.EndsWith(".json") ? fileName : $"{fileName}.json";
            return Path.Combine(Application.streamingAssetsPath, fullFileName).Replace("\\", "/");
        }

        /// <summary>
        /// 직접 파일 접근이 불가능한 URL 경로(WebGL, Android APK 내부)인지 판별함.
        /// </summary>
        private static bool IsRemotePath(string path)
        {
            return path.Contains("://");
        }

        /// <summary>
        /// StreamingAssets에서 JSON 파일을 비동기적으로 읽어옴.
        /// URL 기반 플랫폼(WebGL, Android)에서는 UnityWebRequest, 그 외에는 파일 I/O를 사용함.
        /// </summary>
        public static async UniTask<T> LoadAsync<T>(string fileName, CancellationToken cancellationToken = default) where T : new()
        {
            string path = GetPath(fileName);

            try
            {
                if (IsRemotePath(path))
                {
                    return await LoadViaWebRequestAsync<T>(path, cancellationToken);
                }

                if (File.Exists(path))
                {
                    string json = await File.ReadAllTextAsync(path, cancellationToken);

                    await UniTask.SwitchToMainThread(cancellationToken);

                    // 내용이 "null"이거나 비어 있으면 FromJson이 null을 반환하므로 폴백 처리함.
                    return JsonUtility.FromJson<T>(json) ?? new T();
                }

                Debug.LogWarning(ZString.Concat("[JsonLoader] JSON file not found: ", path));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogError(ZString.Concat("[JsonLoader] Failed to parse JSON async: ", path, ". Error: ", e.Message));
            }

            return new T();
        }

        /// <summary>
        /// UnityWebRequest로 URL 경로의 JSON을 비동기 로드함.
        /// </summary>
        private static async UniTask<T> LoadViaWebRequestAsync<T>(string path, CancellationToken cancellationToken) where T : new()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                await request.SendWebRequest().WithCancellation(cancellationToken);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning(ZString.Concat("[JsonLoader] Failed to fetch JSON: ", path, ". Error: ", request.error));
                    return new T();
                }

                // 내용이 "null"이거나 비어 있으면 FromJson이 null을 반환하므로 폴백 처리함.
                return JsonUtility.FromJson<T>(request.downloadHandler.text) ?? new T();
            }
        }

        /// <summary>
        /// 데이터를 JSON 형식으로 비동기 저장함.
        /// StreamingAssets가 읽기 전용인 플랫폼(WebGL, Android)에서는 저장이 불가능함.
        /// </summary>
        public static async UniTask SaveAsync<T>(string fileName, T data, CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

            if (IsRemotePath(path))
            {
                Debug.LogError(ZString.Concat("[JsonLoader] Saving to StreamingAssets is not supported on this platform: ", path));
                return;
            }

            try
            {
                string json = JsonUtility.ToJson(data, true);
                await File.WriteAllTextAsync(path, json, cancellationToken);
            }
            catch (Exception e)
            {
                Debug.LogError(ZString.Concat("[JsonLoader] Failed to save JSON async: ", path, ". Error: ", e.Message));
            }
        }
    }
}
