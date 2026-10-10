using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using HuliacDev.Network;

namespace HuliacDev.Tests
{
    /// <summary>
    /// ApiRetryUtil.GetTextWithRetryAsync의 응답 본문 반환과 에디터 전송 건너뛰기 선택 검증.
    /// 서버 없이 검증하도록 임시 파일의 file:// URL로 요청함. 네트워크 미연결로 판정되는 환경에서는
    /// 요청 전에 포기하는 정책 때문에 검증할 수 없어 결과를 '판정 불가'로 둠.
    /// </summary>
    public class ApiRetryUtilTests
    {
        private const string ResponseText = "{\"ok\":true}";

        private string _filePath;

        [SetUp]
        public void SetUp()
        {
            _filePath = Path.Combine(Application.temporaryCachePath, "ApiRetryUtilTests_response.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }

        /// <summary>
        /// 기본값으로는 에디터에서도 실제로 요청을 보내 성공과 응답 본문을 반환해야 함.
        /// 기존 SendGetRequestWithRetryAsync는 에디터에서 전송 자체를 건너뛰어 개발 중 서버 응답을 확인할 수 없었음.
        /// </summary>
        [UnityTest]
        public IEnumerator GetTextWithRetryAsync는_에디터에서도_요청을_보내_본문을_반환한다()
        {
            AssumeNetworkReachable();
            File.WriteAllText(_filePath, ResponseText);

            return UniTask.ToCoroutine(async () =>
            {
                (bool isSuccess, string responseText) result = await ApiRetryUtil
                    .GetTextWithRetryAsync(ToFileUrl(_filePath), "test", null, CancellationToken.None, 1, 0.1f)
                    .AwaitWithRealtimeTimeout();

                Assert.IsTrue(result.isSuccess, "요청이 성공했는데 실패로 보고함");
                Assert.AreEqual(ResponseText, result.responseText, "응답 본문이 그대로 반환되지 않음");
            });
        }

        /// <summary>
        /// 건너뛰기를 선택하면 에디터에서는 요청을 보내지 않고 실패와 null 본문을 반환해야 함.
        /// 응답할 파일이 있어도 본문이 오지 않아야 실제로 보내지 않았다는 뜻임.
        /// </summary>
        [UnityTest]
        public IEnumerator 건너뛰기를_선택하면_에디터에서는_요청을_보내지_않는다() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(_filePath, ResponseText);

            (bool isSuccess, string responseText) result = await ApiRetryUtil
                .GetTextWithRetryAsync(ToFileUrl(_filePath), "test", null, CancellationToken.None, 1, 0.1f, skipInEditorAndDevelopmentBuild: true)
                .AwaitWithRealtimeTimeout();

            Assert.IsFalse(result.isSuccess, "건너뛰기를 선택했는데 성공으로 보고함");
            Assert.IsNull(result.responseText, "건너뛰기를 선택했는데 본문을 받음(실제로 요청을 보냄)");
        });

        /// <summary>
        /// 재시도를 모두 소진하면 실패와 null 본문을 반환해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 재시도를_모두_소진하면_실패와_null_본문을_반환한다()
        {
            AssumeNetworkReachable();

            return UniTask.ToCoroutine(async () =>
            {
                (bool isSuccess, string responseText) result = await ApiRetryUtil
                    .GetTextWithRetryAsync(ToFileUrl(_filePath), "test", null, CancellationToken.None, 2, 0.1f)
                    .AwaitWithRealtimeTimeout();

                Assert.IsFalse(result.isSuccess, "없는 파일 요청이 성공으로 보고됨");
                Assert.IsNull(result.responseText);
            });
        }

        /// <summary>
        /// 네트워크 미연결로 판정되면 요청 전에 포기하므로, 이 경우 테스트를 판정 불가로 둠.
        /// </summary>
        private static void AssumeNetworkReachable()
        {
            Assume.That(Application.internetReachability, Is.Not.EqualTo(NetworkReachability.NotReachable),
                "네트워크 미연결로 판정되어 요청 전에 포기하므로 검증할 수 없음");
        }

        /// <summary>
        /// 로컬 파일 경로를 UnityWebRequest가 읽을 수 있는 file:// URL로 바꿈.
        /// </summary>
        private static string ToFileUrl(string path)
        {
            return new Uri(path).AbsoluteUri;
        }
    }
}
