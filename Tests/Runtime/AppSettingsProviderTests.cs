using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wonjeong.Data;

namespace Wonjeong.Tests
{
    /// <summary>
    /// AppSettingsProvider의 공유/동시성 동작 검증.
    ///
    /// 배경: 초기 구현은 UniTask.Preserve() 결과를 AttachExternalCancellation으로 감쌌는데,
    /// 로드 완료 전에 두 소비자가 동시에 GetAsync를 호출하면 continuation이 중복 등록되어
    /// InvalidOperationException("Already continuation registered")이 발생했음.
    /// GameManager.Start와 GameCloser.Start가 같은 프레임에 실행되어 실제로 걸렸던 문제이며,
    /// 이 테스트는 그 회귀를 막기 위한 것임.
    /// </summary>
    public class AppSettingsProviderTests
    {
        private const string SettingsFileName = "Settings.json";

        private string _settingsPath;
        private bool _createdByTest;

        /// <summary>
        /// 동시성 조건을 만들려면 로드가 실제로 '대기'해야 함.
        /// 파일이 없으면 JsonLoader가 즉시 기본값을 반환해 버려서 경합 구간이 생기지 않으므로,
        /// Settings.json이 없을 경우에만 테스트용 파일을 만들고 종료 시 되돌림.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _settingsPath = Path.Combine(Application.streamingAssetsPath, SettingsFileName)
                .Replace("\\", "/");

            if (File.Exists(_settingsPath)) return;

            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.WriteAllText(_settingsPath, "{\"warningTime\":60,\"resetTime\":90,\"fadeTime\":0.5}");
            _createdByTest = true;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // 테스트가 만든 파일만 정리함. 원래 있던 프로젝트 설정은 건드리지 않음.
            if (_createdByTest && File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }

        /// <summary>
        /// 로드 완료 전에 여러 소비자가 동시에 요청해도 예외 없이 모두 결과를 받아야 함.
        /// await를 사이에 두지 않고 GetAsync를 연달아 호출하여 경합 구간을 만듦.
        /// </summary>
        [UnityTest]
        public IEnumerator 동시_호출시_예외없이_모두_결과를_받는다() => UniTask.ToCoroutine(async () =>
        {
            using (AppSettingsProvider provider = new AppSettingsProvider())
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                // 반드시 '취소 가능한' 토큰을 넘겨야 함. CancellationToken.None은
                // CanBeCanceled가 false라 실제 소비자(GetCancellationTokenOnDestroy)와
                // 코드 경로가 달라져 경합이 재현되지 않음.
                //
                // 핵심: 어느 것도 await하지 않은 채로 먼저 전부 호출해야 로드가 '진행 중'인
                // 상태에서 awaiter가 겹침. 사이에 await를 넣으면 경합이 재현되지 않음.
                UniTask<Settings> first = provider.GetAsync(cts.Token);
                UniTask<Settings> second = provider.GetAsync(cts.Token);
                UniTask<Settings> third = provider.GetAsync(cts.Token);

                Settings a = await first;
                Settings b = await second;
                Settings c = await third;

                Assert.IsNotNull(a, "첫 번째 소비자가 결과를 받지 못함");
                Assert.IsNotNull(b, "두 번째 소비자가 결과를 받지 못함");
                Assert.IsNotNull(c, "세 번째 소비자가 결과를 받지 못함");
            }
        });

        /// <summary>
        /// 모든 소비자가 동일한 인스턴스를 받아야 함.
        /// 서로 다른 인스턴스가 나오면 파일을 여러 번 읽었다는 뜻이므로 일원화가 깨진 것임.
        /// </summary>
        [UnityTest]
        public IEnumerator 동시_호출시_모두_같은_인스턴스를_공유한다() => UniTask.ToCoroutine(async () =>
        {
            using (AppSettingsProvider provider = new AppSettingsProvider())
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                UniTask<Settings> first = provider.GetAsync(cts.Token);
                UniTask<Settings> second = provider.GetAsync(cts.Token);

                Settings a = await first;
                Settings b = await second;

                Assert.AreSame(a, b, "인스턴스가 다름 - Settings.json을 두 번 로드했을 가능성");
            }
        });

        /// <summary>
        /// 로드가 이미 완료된 뒤에 요청해도 같은 결과를 반환해야 함.
        /// (완료 전 동시 요청과 완료 후 요청은 코드 경로가 다르므로 별도로 검증함)
        /// </summary>
        [UnityTest]
        public IEnumerator 완료_후_재호출시_같은_인스턴스를_반환한다() => UniTask.ToCoroutine(async () =>
        {
            using (AppSettingsProvider provider = new AppSettingsProvider())
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Settings first = await provider.GetAsync(cts.Token);
                Settings second = await provider.GetAsync(cts.Token);

                Assert.IsNotNull(first);
                Assert.AreSame(first, second, "완료 후 재호출에서 다른 인스턴스가 반환됨");
            }
        });

        /// <summary>
        /// 한 소비자가 취소되어도 다른 소비자는 정상적으로 결과를 받아야 함.
        /// 오브젝트 파괴 시 GetCancellationTokenOnDestroy가 취소되는 상황에 대응함.
        /// </summary>
        [UnityTest]
        public IEnumerator 한쪽이_취소되어도_다른쪽은_영향받지_않는다() => UniTask.ToCoroutine(async () =>
        {
            using (AppSettingsProvider provider = new AppSettingsProvider())
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                UniTask<Settings> canceled = provider.GetAsync(cts.Token);
                UniTask<Settings> healthy = provider.GetAsync(CancellationToken.None);

                cts.Cancel();

                try
                {
                    await canceled;
                }
                catch (System.OperationCanceledException)
                {
                    // 취소된 소비자는 여기로 오는 것이 정상
                }

                Settings result = await healthy;
                Assert.IsNotNull(result, "다른 소비자의 취소가 전파되어 결과를 받지 못함");
            }
        });
    }
}
