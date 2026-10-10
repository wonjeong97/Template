using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    [Serializable]
    public class JsonLoaderTestData
    {
        public string name;
        public int value;
    }

    /// <summary>
    /// JsonLoader의 Load/Save API 검증.
    /// 동기 API는 프레임 진행 없이 [Test]로, 비동기 API는 [UnityTest]로 검증함(이 테스트 어셈블리는 PlayMode에서 실행됨).
    /// </summary>
    public class JsonLoaderTests
    {
        private const string TestFileName = "JsonLoaderTests_temp.json";

        private string _testFilePath;

        [SetUp]
        public void SetUp()
        {
            _testFilePath = Path.Combine(Application.streamingAssetsPath, TestFileName).Replace("\\", "/");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }

            // 저장 실패 테스트가 임시 파일 자리에 만든 폴더와, 실패한 구현이 남겼을 수 있는 임시 파일을 정리함.
            string tempPath = _testFilePath + JsonLoader.TempFileSuffix;
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath);
            }
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Save로 저장한 내용을 Load로 그대로 다시 읽을 수 있어야 함.
        /// </summary>
        [Test]
        public void Save로_저장한_데이터를_Load로_그대로_읽는다()
        {
            JsonLoaderTestData data = new JsonLoaderTestData { name = "hello", value = 42 };

            JsonLoader.Save(TestFileName, data);
            JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>(TestFileName);

            Assert.IsTrue(File.Exists(_testFilePath), "Save가 실제 파일을 생성해야 함");
            Assert.AreEqual("hello", loaded.name);
            Assert.AreEqual(42, loaded.value);
        }

        /// <summary>
        /// 존재하지 않는 파일을 Load하면 예외 없이 경고를 남기고 기본값(new T())을 반환해야 함.
        /// </summary>
        [Test]
        public void 존재하지_않는_파일을_Load하면_경고와_함께_기본값을_반환한다()
        {
            LogAssert.Expect(LogType.Warning, new Regex("JSON file not found"));

            JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>("JsonLoaderTests_없는파일.json");

            Assert.IsNotNull(loaded, "파일이 없어도 null이 아닌 기본값을 반환해야 함");
            Assert.AreEqual(0, loaded.value);
        }

        /// <summary>
        /// 원격 경로(WebGL/Android 등 URL 기반 StreamingAssets)에서 동기 Load를 호출하면
        /// 실제 I/O를 시도하지 않고 에러 로그와 함께 기본값을 반환해야 함.
        /// GetPath는 파일명을 StreamingAssets 경로에 이어 붙이므로, "://"를 포함한 파일명을
        /// 넘기면 결과 경로에도 "://"가 남아 IsRemotePath 판정을 재현할 수 있음.
        /// </summary>
        [Test]
        public void 원격_경로에서_Load를_호출하면_에러와_함께_기본값을_반환한다()
        {
            LogAssert.Expect(LogType.Error, new Regex("Synchronous load is not supported"));

            JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>("http://fake-host/remote.json");

            Assert.IsNotNull(loaded);
        }

        /// <summary>
        /// 원격 경로에서 동기 Save를 호출하면 저장을 시도하지 않고 에러 로그만 남겨야 함.
        /// </summary>
        [Test]
        public void 원격_경로에서_Save를_호출하면_에러를_남기고_저장하지_않는다()
        {
            LogAssert.Expect(LogType.Error, new Regex("Saving to StreamingAssets is not supported"));

            bool isSaved = true;
            Assert.DoesNotThrow(() => isSaved = JsonLoader.Save("http://fake-host/remote.json", new JsonLoaderTestData()));
            Assert.IsFalse(isSaved, "저장하지 못했는데 성공으로 보고함");
        }

        /// <summary>
        /// .json 확장자 없이 저장해도 자동으로 .json이 부착되어야 함.
        /// </summary>
        [Test]
        public void 확장자_없이_저장해도_json_확장자가_자동으로_부착된다()
        {
            string fileNameWithoutExt = "JsonLoaderTests_noext";
            string expectedPath = Path.Combine(Application.streamingAssetsPath, "JsonLoaderTests_noext.json").Replace("\\", "/");

            try
            {
                JsonLoaderTestData data = new JsonLoaderTestData { name = "no_ext", value = 99 };
                JsonLoader.Save(fileNameWithoutExt, data);

                Assert.IsTrue(File.Exists(expectedPath), "Save가 .json 확장자를 붙여 파일을 생성해야 함");

                JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>(fileNameWithoutExt);
                Assert.AreEqual("no_ext", loaded.name);
                Assert.AreEqual(99, loaded.value);
            }
            finally
            {
                if (File.Exists(expectedPath)) File.Delete(expectedPath);
            }
        }

        /// <summary>
        /// PersistentData 위치를 지정하면 persistentDataPath에 저장되고 읽혀야 함.
        /// </summary>
        [Test]
        public void PersistentData_위치에_정상적으로_저장되고_로드된다()
        {
            string fileName = "JsonLoaderTests_persistent";
            string expectedPath = Path.Combine(Application.persistentDataPath, "JsonLoaderTests_persistent.json").Replace("\\", "/");

            try
            {
                JsonLoaderTestData data = new JsonLoaderTestData { name = "persistent", value = 777 };
                JsonLoader.Save(fileName, data, JsonStorageLocation.PersistentData);

                Assert.IsTrue(File.Exists(expectedPath), "PersistentData 경로에 파일이 생성되어야 함");

                JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>(fileName, JsonStorageLocation.PersistentData);
                Assert.AreEqual("persistent", loaded.name);
                Assert.AreEqual(777, loaded.value);
            }
            finally
            {
                if (File.Exists(expectedPath)) File.Delete(expectedPath);
            }
        }

        /// <summary>
        /// 정상 파일을 TryLoadAsync로 읽으면 성공과 파일 내용을 함께 반환해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator TryLoadAsync로_정상_파일을_읽으면_성공과_내용을_반환한다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_testFilePath, "{\"name\":\"ok\",\"value\":7}");

            (bool isSuccess, JsonLoaderTestData data) result =
                await JsonLoader.TryLoadAsync<JsonLoaderTestData>(TestFileName).AwaitWithRealtimeTimeout();

            Assert.IsTrue(result.isSuccess, "정상 파일인데 실패로 보고함");
            Assert.AreEqual("ok", result.data.name);
            Assert.AreEqual(7, result.data.value);
        });

        /// <summary>
        /// 끝 쉼표처럼 형식이 잘못된 파일은 오류를 남기고 실패와 기본값(new T())을 반환해야 함.
        /// 이전에는 LoadAsync만 있어, 기본값을 받은 호출부가 '파일에 기본값이 적혀 있음'과 구별할 수 없었음.
        /// </summary>
        [UnityTest]
        public IEnumerator TryLoadAsync로_형식이_잘못된_파일을_읽으면_실패와_기본값을_반환한다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_testFilePath, "{\"name\":\"broken\",\"value\":7,}");
            LogAssert.Expect(LogType.Error, new Regex("Failed to parse JSON async"));

            (bool isSuccess, JsonLoaderTestData data) result =
                await JsonLoader.TryLoadAsync<JsonLoaderTestData>(TestFileName).AwaitWithRealtimeTimeout();

            Assert.IsFalse(result.isSuccess, "형식이 잘못된 파일인데 성공으로 보고함");
            Assert.IsNotNull(result.data, "실패해도 null이 아닌 기본값을 반환해야 함");
            Assert.AreEqual(0, result.data.value);
        });

        /// <summary>
        /// 없는 파일은 경고를 남기고 실패를 반환해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator TryLoadAsync로_없는_파일을_읽으면_실패를_반환한다() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.Expect(LogType.Warning, new Regex("JSON file not found"));

            (bool isSuccess, JsonLoaderTestData data) result =
                await JsonLoader.TryLoadAsync<JsonLoaderTestData>("JsonLoaderTests_없는파일.json").AwaitWithRealtimeTimeout();

            Assert.IsFalse(result.isSuccess, "파일이 없는데 성공으로 보고함");
            Assert.IsNotNull(result.data);
        });

        /// <summary>
        /// 빈 파일은 FromJson이 예외 없이 null을 반환하는 경우라, 경고를 남기고 실패로 보고해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator TryLoadAsync로_빈_파일을_읽으면_실패를_반환한다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_testFilePath, string.Empty);
            LogAssert.Expect(LogType.Warning, new Regex("JSON file is empty"));

            (bool isSuccess, JsonLoaderTestData data) result =
                await JsonLoader.TryLoadAsync<JsonLoaderTestData>(TestFileName).AwaitWithRealtimeTimeout();

            Assert.IsFalse(result.isSuccess, "빈 파일인데 성공으로 보고함");
            Assert.IsNotNull(result.data);
        });

        /// <summary>
        /// 취소된 토큰으로 LoadAsync를 호출하면 기본값을 반환하지 않고 OperationCanceledException을 던져야 함.
        /// 이전에는 취소를 삼키고 new T()를 반환해, 파괴된 호출부가 기본값으로 초기화를 이어갔음.
        /// </summary>
        [UnityTest]
        public IEnumerator 취소된_토큰으로_LoadAsync를_호출하면_OperationCanceledException을_던진다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_testFilePath, "{\"name\":\"ok\",\"value\":7}");

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();

                bool isCanceled = false;
                try
                {
                    await JsonLoader.LoadAsync<JsonLoaderTestData>(TestFileName, cts.Token).AwaitWithRealtimeTimeout();
                }
                catch (OperationCanceledException)
                {
                    isCanceled = true;
                }

                Assert.IsTrue(isCanceled, "취소됐는데 예외 없이 값을 반환함");
            }
        });

        /// <summary>
        /// 기존 파일이 있을 때 SaveAsync가 성공하면 true를 반환하고, 대상 파일 내용을 바꾸고 임시 파일은 남기지 않아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator SaveAsync가_성공하면_true를_반환하고_임시_파일을_남기지_않는다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_testFilePath, "{\"name\":\"old\",\"value\":1}");

            bool isSaved = await JsonLoader.SaveAsync(TestFileName, new JsonLoaderTestData { name = "new", value = 2 })
                .AwaitWithRealtimeTimeout();

            Assert.IsTrue(isSaved, "저장했는데 실패로 보고함");
            Assert.IsFalse(File.Exists(_testFilePath + JsonLoader.TempFileSuffix), "임시 파일이 남음");

            JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>(TestFileName);
            Assert.AreEqual("new", loaded.name);
            Assert.AreEqual(2, loaded.value);
        });

        /// <summary>
        /// 쓰기에 실패하면 false를 반환하고 기존 파일 내용은 그대로 남아야 함.
        /// 임시 파일 자리에 폴더를 만들어 쓰기 단계에서 실패시킴. 대상 파일을 바로 덮어쓰던 이전 구현은
        /// 임시 파일을 거치지 않아 기존 내용을 덮어쓰고 true에 해당하는 결과를 냈음(저장 도중 끊김에 무방비였음).
        /// </summary>
        [UnityTest]
        public IEnumerator SaveAsync가_쓰기에_실패하면_false를_반환하고_기존_파일을_유지한다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_testFilePath, "{\"name\":\"old\",\"value\":1}");
            Directory.CreateDirectory(_testFilePath + JsonLoader.TempFileSuffix);
            LogAssert.Expect(LogType.Error, new Regex("Failed to save JSON async"));

            bool isSaved = await JsonLoader.SaveAsync(TestFileName, new JsonLoaderTestData { name = "new", value = 2 })
                .AwaitWithRealtimeTimeout();

            Assert.IsFalse(isSaved, "쓰기에 실패했는데 성공으로 보고함");
            JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>(TestFileName);
            Assert.AreEqual("old", loaded.name, "저장에 실패했는데 기존 파일 내용이 바뀜");
        });

        /// <summary>
        /// 동기 Save도 쓰기에 실패하면 false를 반환하고 기존 파일 내용을 유지해야 함.
        /// </summary>
        [Test]
        public void Save가_쓰기에_실패하면_false를_반환하고_기존_파일을_유지한다()
        {
            File.WriteAllText(_testFilePath, "{\"name\":\"old\",\"value\":1}");
            Directory.CreateDirectory(_testFilePath + JsonLoader.TempFileSuffix);
            LogAssert.Expect(LogType.Error, new Regex("Failed to save JSON"));

            bool isSaved = JsonLoader.Save(TestFileName, new JsonLoaderTestData { name = "new", value = 2 });

            Assert.IsFalse(isSaved, "쓰기에 실패했는데 성공으로 보고함");
            JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>(TestFileName);
            Assert.AreEqual("old", loaded.name, "저장에 실패했는데 기존 파일 내용이 바뀜");
        }

        /// <summary>
        /// 취소된 토큰으로 SaveAsync를 호출하면 OperationCanceledException을 던지고, 기존 파일을 바꾸거나 임시 파일을 남기지 않아야 함.
        /// 이전에는 취소도 '저장 실패' 오류 로그로 삼켰음.
        /// </summary>
        [UnityTest]
        public IEnumerator 취소된_토큰으로_SaveAsync를_호출하면_예외를_던지고_기존_파일을_유지한다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_testFilePath, "{\"name\":\"old\",\"value\":1}");

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();

                bool isCanceled = false;
                try
                {
                    await JsonLoader.SaveAsync(TestFileName, new JsonLoaderTestData { name = "new", value = 2 }, cts.Token)
                        .AwaitWithRealtimeTimeout();
                }
                catch (OperationCanceledException)
                {
                    isCanceled = true;
                }

                Assert.IsTrue(isCanceled, "취소됐는데 예외를 던지지 않음");
            }

            Assert.IsFalse(File.Exists(_testFilePath + JsonLoader.TempFileSuffix), "취소 후 임시 파일이 남음");
            JsonLoaderTestData loaded = JsonLoader.Load<JsonLoaderTestData>(TestFileName);
            Assert.AreEqual("old", loaded.name, "취소됐는데 기존 파일 내용이 바뀜");
        });
    }
}
