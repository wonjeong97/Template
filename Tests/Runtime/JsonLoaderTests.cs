using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wonjeong.Utils;

namespace Wonjeong.Tests
{
    [Serializable]
    public class JsonLoaderTestData
    {
        public string name;
        public int value;
    }

    /// <summary>
    /// JsonLoader의 동기 Load/Save API 검증.
    /// 메인 스레드 블로킹 I/O이므로 EditMode에서 실행 가능하며, 별도의 프레임 진행이 필요 없음.
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

            Assert.DoesNotThrow(() => JsonLoader.Save("http://fake-host/remote.json", new JsonLoaderTestData()));
        }
    }
}
