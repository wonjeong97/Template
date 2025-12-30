using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Wonjeong.Utils
{
    public static class JsonLoader
    {
        /// <summary> StreamingAssets 폴더에서 JSON 파일을 읽어온다. </summary>
        public static T Load<T>(string fileName)
        {
            // 확장자 처리
            if (!fileName.EndsWith(".json")) fileName += ".json";
            
            string filePath = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[JsonLoader] File does not exist: {filePath}");
                return default(T);
            }

            try
            {
                string json = File.ReadAllText(filePath);
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonLoader] Failed to parse json: {fileName}\nError: {e.Message}");
                return default(T);
            }
        }
        
        /// <summary> 데이터를 JSON 파일로 저장 </summary>
        public static void Save<T>(T data, string fileName, bool prettyPrint = true)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            // 확장자 처리
            if (!fileName.EndsWith(".json")) fileName += ".json";

            string filePath = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

            try
            {
                string directoryPath = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string json = JsonUtility.ToJson(data, prettyPrint);
                File.WriteAllText(filePath, json, Encoding.UTF8);
                Debug.Log($"[JsonLoader] {fileName} saved successfully at {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonLoader] Failed to save json: {fileName}\nError: {e.Message}");
            }
#else
            Debug.LogError("[JsonLoader] Save is only supported on PC platforms.");
#endif
        }
    }
}