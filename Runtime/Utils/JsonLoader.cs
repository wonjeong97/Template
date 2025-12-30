using System.IO;
using UnityEngine;

namespace Wonjeong.Utils
{
    public static class JsonLoader
    {
        /// <summary> StreamingAssets 폴더에서 JSON 파일을 읽어온다. </summary>
        public static T Load<T>(string fileName)
        {
            // 경로 결합 및 역슬래시 처리
            string filePath = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

            if (!File.Exists(filePath))
            {
                Debug.LogWarning("[JsonLoader] File does not exist: " + filePath);
                return default(T);
            }

            try
            {
                string json = File.ReadAllText(filePath);
                // 필요시 디버그 로그 활성화
                // Debug.Log($"[JsonLoader] {fileName} load complete");
                
                return JsonUtility.FromJson<T>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[JsonLoader] Failed to parse json: {fileName}\nError: {e.Message}");
                return default(T);
            }
        }
        
        /// <summary> 데이터를 JSON 파일로 저장. </summary>
        public static void Save<T>(T data, string fileName, bool prettyPrint = true)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");
            string json = JsonUtility.ToJson(data, prettyPrint);
            File.WriteAllText(filePath, json);
        }
    }
}