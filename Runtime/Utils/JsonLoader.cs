using System;
using System.IO;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Wonjeong.Utils
{
    /// <summary>
    /// JSON 직렬화 및 파일 입출력을 담당하는 정적 유틸리티 클래스.
    /// 모든 데이터 파일 I/O 스트림의 최적화를 수행함.
    /// </summary>
    public static class JsonLoader
    {
        /// <summary>
        /// 로컬 스토리지에서 JSON 파일을 동기적으로 읽어옴.
        /// </summary>
        public static T Load<T>(string fileName) where T : new()
        {   
            string fullFileName = fileName.EndsWith(".json") ? fileName : $"{fileName}.json";
            string path = Path.Combine(Application.streamingAssetsPath, fullFileName).Replace("\\", "/");
            
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    return JsonUtility.FromJson<T>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError(ZString.Concat("[JsonLoader] Failed to parse JSON: ", path, ". Error: ", e.Message));
                }
            }
            else
            {
                Debug.LogWarning(ZString.Concat("[JsonLoader] JSON file not found: ", path));
            }
            
            return new T(); // Null 발생 방지를 위한 Fallback 로직 규칙 준수
        }

        /// <summary>
        /// 로컬 스토리지에서 JSON 파일을 비동기적으로 읽어옴.
        /// </summary>
        public static async UniTask<T> LoadAsync<T>(string fileName, CancellationToken cancellationToken = default) where T : new()
        {
           string fullFileName = fileName.EndsWith(".json") ? fileName : $"{fileName}.json";
    
    string path = Path.Combine(Application.streamingAssetsPath, fullFileName).Replace("\\", "/");
            
            if (File.Exists(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path, cancellationToken);
                    
                    await UniTask.SwitchToMainThread(cancellationToken);
                    return JsonUtility.FromJson<T>(json);
                }
                catch (OperationCanceledException)
                {
                    return new T();
                }
                catch (Exception e)
                {
                    Debug.LogError(ZString.Concat("[JsonLoader] Failed to parse JSON async: ", path, ". Error: ", e.Message));
                }
            }
            else
            {
                Debug.LogWarning(ZString.Concat("[JsonLoader] JSON file not found: ", path));
            }
            
            return new T(); 
        }

        /// <summary>
        /// 데이터를 JSON 형식으로 비동기 저장함.
        /// </summary>
        public static async UniTask SaveAsync<T>(string fileName, T data, CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");
            
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