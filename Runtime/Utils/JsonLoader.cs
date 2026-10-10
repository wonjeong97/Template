using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ZLogger;

namespace HuliacDev.Utils
{
    /// <summary>
    /// JSON 파일 저장 및 로드 위치.
    /// </summary>
    public enum JsonStorageLocation
    {
        StreamingAssets,
        PersistentData
    }

    /// <summary>
    /// JSON 직렬화 및 파일 입출력을 담당하는 정적 유틸리티 클래스.
    /// 모든 데이터 파일 I/O 스트림의 최적화를 수행함.
    /// WebGL/Android처럼 StreamingAssets가 URL인 플랫폼에서는 UnityWebRequest로 로드함.
    /// </summary>
    public static class JsonLoader
    {
        /// <summary>
        /// 저장할 때 내용을 먼저 쓰는 임시 파일의 접미사. 대상 파일 경로 뒤에 붙임.
        /// Unity는 확장자가 .tmp인 파일을 임포트하지 않으므로, 에디터의 StreamingAssets에 잠시 생겨도 .meta가 만들어지지 않음.
        /// </summary>
        internal const string TempFileSuffix = ".tmp";

        /// <summary>
        /// 지정된 저장 위치 기준 전체 경로를 생성함 (.json 확장자 자동 부착).
        /// </summary>
        private static string GetPath(string fileName, JsonStorageLocation location = JsonStorageLocation.StreamingAssets)
        {
            string fullFileName = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? fileName : ZString.Concat(fileName, ".json");
            string basePath = location switch
            {
                JsonStorageLocation.PersistentData => Application.persistentDataPath,
                _ => Application.streamingAssetsPath
            };
            return Path.Combine(basePath, fullFileName).Replace("\\", "/");
        }

        /// <summary>
        /// 직접 파일 접근이 불가능한 URL 경로(WebGL, Android APK 내부)인지 판별함.
        /// </summary>
        private static bool IsRemotePath(string path)
        {
            return path.Contains("://");
        }

        /// <summary>
        /// 로거가 있으면 ZLogger로, 없으면 Unity 콘솔로 경고를 남김.
        /// 정적 클래스라 로거를 주입받을 수 없으므로 호출자가 넘긴 로거를 사용함.
        /// </summary>
        private static void LogWarning(Microsoft.Extensions.Logging.ILogger logger, string message)
        {
            if (logger != null) logger.ZLogWarning($"{message}");
            else Debug.LogWarning(message);
        }

        /// <summary>
        /// 로거가 있으면 ZLogger로, 없으면 Unity 콘솔로 오류를 남김.
        /// </summary>
        private static void LogError(Microsoft.Extensions.Logging.ILogger logger, string message)
        {
            if (logger != null) logger.ZLogError($"{message}");
            else Debug.LogError(message);
        }

        /// <summary>
        /// StreamingAssets에서 JSON 파일을 비동기로 읽어오는 기본 오버로드.
        /// </summary>
        public static UniTask<T> LoadAsync<T>(string fileName, CancellationToken cancellationToken = default,
            Microsoft.Extensions.Logging.ILogger logger = null) where T : new()
            => LoadAsync<T>(fileName, JsonStorageLocation.StreamingAssets, cancellationToken, logger);

        /// <summary>
        /// 지정된 저장 위치에서 JSON 파일을 비동기적으로 읽어옴.
        /// 파일이 없거나 형식이 잘못되었으면 new T()를 반환하므로, 읽기 실패와 파일에 적힌 기본값을 구분해야 하면
        /// <see cref="TryLoadAsync{T}(string, JsonStorageLocation, CancellationToken, Microsoft.Extensions.Logging.ILogger)"/>를 쓸 것.
        /// 취소되면 기본값을 반환하지 않고 OperationCanceledException을 던짐.
        /// URL 기반 플랫폼(WebGL, Android)에서는 UnityWebRequest, 그 외에는 파일 I/O를 사용함.
        /// logger를 넘기면 실패 로그를 ZLogger로 남기고, 없으면 Unity 콘솔로 대신 출력함.
        /// </summary>
        public static async UniTask<T> LoadAsync<T>(string fileName, JsonStorageLocation location,
            CancellationToken cancellationToken = default, Microsoft.Extensions.Logging.ILogger logger = null) where T : new()
        {
            (bool isSuccess, T data) result = await TryLoadAsync<T>(fileName, location, cancellationToken, logger);
            return result.data;
        }

        /// <summary>
        /// StreamingAssets에서 JSON 파일을 비동기로 읽어 성공 여부와 함께 반환하는 기본 오버로드.
        /// </summary>
        public static UniTask<(bool isSuccess, T data)> TryLoadAsync<T>(string fileName, CancellationToken cancellationToken = default,
            Microsoft.Extensions.Logging.ILogger logger = null) where T : new()
            => TryLoadAsync<T>(fileName, JsonStorageLocation.StreamingAssets, cancellationToken, logger);

        /// <summary>
        /// 지정된 저장 위치에서 JSON 파일을 비동기로 읽어 성공 여부와 함께 반환함.
        /// 파일이 없거나, 비어 있거나, 형식이 잘못되었거나(끝 쉼표 등), URL 요청이 실패하면 isSuccess가 false이고
        /// data는 null이 아닌 new T()임. 호출자는 isSuccess로 '읽기 실패'와 '파일에 false·0이 적혀 있음'을 구분해
        /// 실패했을 때 쓸 안전한 값을 직접 고를 수 있음. 실패 원인은 이 메서드가 로그로 남김.
        /// 취소되면 기본값을 반환하지 않고 OperationCanceledException을 던짐. 파괴된 호출부가 기본값으로
        /// 초기화를 이어가다 이미 해제된 객체에 접근하지 않게 하기 위함임.
        /// </summary>
        public static async UniTask<(bool isSuccess, T data)> TryLoadAsync<T>(string fileName, JsonStorageLocation location,
            CancellationToken cancellationToken = default, Microsoft.Extensions.Logging.ILogger logger = null) where T : new()
        {
            string path = GetPath(fileName, location);

            try
            {
                string json;

                if (IsRemotePath(path))
                {
                    json = await FetchViaWebRequestAsync(path, cancellationToken, logger);
                    if (json == null) return (false, new T());
                }
                else
                {
                    if (!File.Exists(path))
                    {
                        LogWarning(logger, ZString.Concat("[JsonLoader] JSON file not found: ", path));
                        return (false, new T());
                    }

                    json = await File.ReadAllTextAsync(path, cancellationToken);

                    await UniTask.SwitchToMainThread(cancellationToken);
                }

                // 내용이 "null"이거나 비어 있으면 FromJson이 예외 없이 null을 반환하므로 실패로 봄.
                T data = JsonUtility.FromJson<T>(json);
                if (data == null)
                {
                    LogWarning(logger, ZString.Concat("[JsonLoader] JSON file is empty: ", path));
                    return (false, new T());
                }

                return (true, data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                LogError(logger, ZString.Concat("[JsonLoader] Failed to parse JSON async: ", path, ". Error: ", e.Message));
                return (false, new T());
            }
        }

        /// <summary>
        /// StreamingAssets에서 JSON 파일을 동기적으로 읽어오는 기본 오버로드.
        /// </summary>
        public static T Load<T>(string fileName) where T : new()
            => Load<T>(fileName, JsonStorageLocation.StreamingAssets);

        /// <summary>
        /// 지정된 저장 위치에서 JSON 파일을 동기적으로 읽어옴.
        /// WebGL/Android처럼 직접 파일 접근이 불가능한 URL 기반 플랫폼에서는 동기 I/O 자체가
        /// 불가능하므로 지원하지 않으며, 호출 시 에러를 로그로 남기고 기본값을 반환함.
        /// 메인 스레드를 블로킹하므로 비동기 컨텍스트를 쓸 수 없는 초기화 극초반이나
        /// 에디터 전용 툴링 코드에서만 사용하고, 런타임 로직은 <see cref="LoadAsync{T}(string, JsonStorageLocation, CancellationToken, Microsoft.Extensions.Logging.ILogger)"/>를 쓸 것.
        /// </summary>
        public static T Load<T>(string fileName, JsonStorageLocation location) where T : new()
        {
            string path = GetPath(fileName, location);

            if (IsRemotePath(path))
            {
                Debug.LogError(ZString.Concat("[JsonLoader] Synchronous load is not supported for remote paths (WebGL/Android): ", path));
                return new T();
            }

            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);

                    // 내용이 "null"이거나 비어 있으면 FromJson이 null을 반환하므로 폴백 처리함.
                    return JsonUtility.FromJson<T>(json) ?? new T();
                }

                Debug.LogWarning(ZString.Concat("[JsonLoader] JSON file not found: ", path));
            }
            catch (Exception e)
            {
                Debug.LogError(ZString.Concat("[JsonLoader] Failed to parse JSON: ", path, ". Error: ", e.Message));
            }

            return new T();
        }

        /// <summary>
        /// UnityWebRequest로 URL 경로의 JSON 문자열을 비동기로 받아옴. 요청이 실패하면 경고를 남기고 null을 반환함.
        /// </summary>
        private static async UniTask<string> FetchViaWebRequestAsync(string path, CancellationToken cancellationToken,
            Microsoft.Extensions.Logging.ILogger logger)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                try
                {
                    // ToUniTask는 result가 Success가 아니면 결과를 반환하는 대신
                    // UnityWebRequestException을 던지므로, 실패는 예외로 잡아 처리한다.
                    await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
                }
                catch (UnityWebRequestException e)
                {
                    LogWarning(logger, ZString.Concat("[JsonLoader] Failed to fetch JSON: ", path, ". Error: ", e.Error));
                    return null;
                }

                return request.downloadHandler.text;
            }
        }

        /// <summary>
        /// 데이터를 StreamingAssets에 JSON으로 비동기 저장하는 기본 오버로드.
        /// </summary>
        public static UniTask<bool> SaveAsync<T>(string fileName, T data, CancellationToken cancellationToken = default,
            Microsoft.Extensions.Logging.ILogger logger = null)
            => SaveAsync<T>(fileName, data, JsonStorageLocation.StreamingAssets, cancellationToken, logger);

        /// <summary>
        /// 데이터를 JSON 형식으로 비동기 저장하고 성공 여부를 반환함 (.json 확장자 자동 부착).
        /// 임시 파일에 먼저 쓴 뒤 대상 파일과 바꾸므로, 저장 도중 전원이 꺼지거나 디스크가 가득 차도 기존 파일이 비거나 잘리지 않음.
        /// 쓰기에 실패하면(읽기 전용 위치, 권한 없음, 디스크 부족 등) 오류를 로그로 남기고 false를 반환함.
        /// 취소되면 기존 파일을 그대로 두고 OperationCanceledException을 던짐.
        /// StreamingAssets가 읽기 전용인 플랫폼(WebGL, Android)에서는 저장할 수 없어 false를 반환함.
        /// logger를 넘기면 실패 로그를 ZLogger로 남기고, 없으면 Unity 콘솔로 대신 출력함.
        /// </summary>
        public static async UniTask<bool> SaveAsync<T>(string fileName, T data, JsonStorageLocation location,
            CancellationToken cancellationToken = default, Microsoft.Extensions.Logging.ILogger logger = null)
        {
            string path = GetPath(fileName, location);

            if (IsRemotePath(path))
            {
                LogError(logger, ZString.Concat("[JsonLoader] Saving to StreamingAssets is not supported on this platform: ", path));
                return false;
            }

            try
            {
                string json = JsonUtility.ToJson(data, true);
                await WriteAllTextAtomicallyAsync(path, json, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                LogError(logger, ZString.Concat("[JsonLoader] Failed to save JSON async: ", path, ". Error: ", e.Message));
                return false;
            }
        }

        /// <summary>
        /// 데이터를 StreamingAssets에 JSON으로 동기 저장하는 기본 오버로드.
        /// </summary>
        public static bool Save<T>(string fileName, T data)
            => Save<T>(fileName, data, JsonStorageLocation.StreamingAssets);

        /// <summary>
        /// 데이터를 JSON 형식으로 동기적으로 저장하고 성공 여부를 반환함 (.json 확장자 자동 부착).
        /// 임시 파일을 거쳐 저장하는 방식과 실패 시 false를 반환하는 규칙은 SaveAsync와 같음.
        /// StreamingAssets가 읽기 전용인 플랫폼(WebGL, Android)에서는 저장할 수 없어 false를 반환함.
        /// 메인 스레드를 블로킹하므로 <see cref="SaveAsync{T}(string, T, JsonStorageLocation, CancellationToken, Microsoft.Extensions.Logging.ILogger)"/>를 쓸 수 없는 제한적인
        /// 상황(에디터 전용 툴링 등)에서만 사용할 것.
        /// </summary>
        public static bool Save<T>(string fileName, T data, JsonStorageLocation location)
        {
            string path = GetPath(fileName, location);

            if (IsRemotePath(path))
            {
                Debug.LogError(ZString.Concat("[JsonLoader] Saving to StreamingAssets is not supported on this platform: ", path));
                return false;
            }

            try
            {
                string json = JsonUtility.ToJson(data, true);
                WriteAllTextAtomically(path, json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(ZString.Concat("[JsonLoader] Failed to save JSON: ", path, ". Error: ", e.Message));
                return false;
            }
        }

        /// <summary>
        /// 임시 파일에 내용을 비동기로 쓰고 디스크에 반영한 뒤 대상 파일과 바꿈. 실패하거나 취소되면 임시 파일을 지우고 예외를 다시 던짐.
        /// 대상 파일을 바로 덮어쓰면 쓰는 도중 끊겼을 때 파일이 비거나 잘린 채 남아, 다음 실행부터 기본값으로 읽히기 때문임.
        /// </summary>
        private static async UniTask WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken)
        {
            string tempPath = PrepareTempPath(path);

            try
            {
                // Encoding.UTF8.GetBytes는 BOM을 붙이지 않으므로 File.WriteAllText 기본값과 같은 BOM 없는 UTF-8이 됨.
                byte[] bytes = Encoding.UTF8.GetBytes(contents);
                using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                    // OS 버퍼까지 비워 내용이 디스크에 기록된 뒤에 이름을 바꿈. 그렇지 않으면 전원이 끊겼을 때
                    // 이름 변경만 반영되고 내용은 비어 있는 파일이 남을 수 있음.
                    stream.Flush(true);
                }

                ReplaceWithTempFile(tempPath, path);
            }
            catch
            {
                DeleteTempFile(tempPath);
                throw;
            }
        }

        /// <summary>
        /// 임시 파일에 내용을 동기로 쓰고 디스크에 반영한 뒤 대상 파일과 바꿈. 실패하면 임시 파일을 지우고 예외를 다시 던짐.
        /// </summary>
        private static void WriteAllTextAtomically(string path, string contents)
        {
            string tempPath = PrepareTempPath(path);

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(contents);
                using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                ReplaceWithTempFile(tempPath, path);
            }
            catch
            {
                DeleteTempFile(tempPath);
                throw;
            }
        }

        /// <summary>
        /// 대상 파일의 폴더가 없으면 만들고, 내용을 먼저 쓸 임시 파일 경로를 반환함.
        /// 임시 파일을 대상과 같은 폴더에 두어야 교체가 다른 볼륨으로의 복사 없이 이름 변경으로 끝남.
        /// </summary>
        private static string PrepareTempPath(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return ZString.Concat(path, TempFileSuffix);
        }

        /// <summary>
        /// 다 쓴 임시 파일로 대상 파일을 바꿈. 대상이 있으면 File.Replace로 한 번에 교체하고, 없으면 이름만 바꿈.
        /// </summary>
        private static void ReplaceWithTempFile(string tempPath, string path)
        {
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        /// <summary>
        /// 실패한 저장의 임시 파일을 지움. 삭제 실패는 원래 예외를 덮지 않도록 무시하며, 남은 임시 파일은 다음 저장에서 덮어씀.
        /// </summary>
        private static void DeleteTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception)
            {
                // 호출부가 원래 예외를 다시 던져 저장 실패를 보고하므로 여기서는 추가로 처리하지 않음.
            }
        }
    }
}
