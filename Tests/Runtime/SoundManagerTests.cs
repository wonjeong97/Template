using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using HuliacDev.UI;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// SoundManager의 BGM 요청 순서 보장, 로드 중 정지한 효과음, 미리 불러오기(PreloadAsync), 무시된 재생 요청의 로그 검증.
    ///
    /// 배경: 캐시에 없는 BGM은 다운로드가 끝난 뒤 재생되는데, 예전 구현은 그 사이
    /// StopBGM/FadeOutBGM/다른 PlayBGM이 호출됐는지 확인하지 않았음. 그래서 정지한 BGM이
    /// 로드 완료 후 되살아나거나, 늦게 도착한 이전 BGM이 새로 고른 BGM을 덮어썼음.
    /// 로드가 실제로 비동기로 진행되도록 임시 폴더에 무음 WAV 파일을 만들어 사용함.
    /// </summary>
    public class SoundManagerTests
    {
        /// <summary>로드가 끝났을 만큼 충분히 기다리는 실시간 시간(초).</summary>
        private const float LoadSettleSeconds = 1f;

        private GameObject _go;
        private SoundManager _soundManager;
        private string _clipPath;

        [SetUp]
        public void SetUp()
        {
            SingletonGuard<SoundManager>.ResetForTesting();

            _go = new GameObject("SoundManagerTests");
            _soundManager = _go.AddComponent<SoundManager>();
            SoundManagerTestHelper.SetLogger(_soundManager);

            // 5MB WAV는 실제 로드가 필요한 테스트에서만 PrepareSilentClip으로 만듦.
            _clipPath = Path.Combine(Application.temporaryCachePath, "SoundManagerTests.wav").Replace("\\", "/");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            SingletonGuard<SoundManager>.ResetForTesting();

            if (File.Exists(_clipPath)) File.Delete(_clipPath);
        }

        /// <summary>
        /// 로드 중에 StopBGM을 호출하면, 로드가 끝나도 BGM이 재생(할당)되면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 로드_중_StopBGM을_호출하면_로드가_끝나도_재생하지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            AudioSource bgmSource = GetBgmSource();
            PrepareSilentClip("a");

            // Start()의 의존성 누락 오류가 먼저 출력되게 한 프레임 넘김(아래 Assume으로 끝나도 예상 로그가 남도록).
            await UniTask.Yield();

            _soundManager.PlayBGM("a");
            AssumeLoadIsPending(!bgmSource.clip);
            _soundManager.StopBGM();

            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
            Assert.IsFalse(bgmSource.clip, "정지한 BGM이 로드 완료 후 되살아남");

            // 대조군: 같은 파일이 실제로 로드 가능해야 위 검증이 의미가 있음.
            _soundManager.PlayBGM("a");
            await WaitUntilBgmClipAsync(bgmSource, "a");
        });

        /// <summary>
        /// 재생 중인 BGM이 없는 상태(부팅 직후 첫 로드 등)에서 로드 중에 FadeOutBGM을 호출하면,
        /// 페이드할 대상은 없지만 요청은 취소되어 로드가 끝나도 BGM이 재생(할당)되면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 첫_로드_중_FadeOutBGM을_호출하면_로드가_끝나도_재생하지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            AudioSource bgmSource = GetBgmSource();
            PrepareSilentClip("a");

            // Start()의 의존성 누락 오류가 먼저 출력되게 한 프레임 넘김(아래 Assume으로 끝나도 예상 로그가 남도록).
            await UniTask.Yield();

            _soundManager.PlayBGM("a");
            AssumeLoadIsPending(!bgmSource.clip);
            _soundManager.FadeOutBGM(1f);

            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
            Assert.IsFalse(bgmSource.clip, "로드 중 페이드아웃한 BGM이 로드 완료 후 재생됨");

            // 대조군: 같은 파일이 실제로 로드 가능해야 위 검증이 의미가 있음.
            _soundManager.PlayBGM("a");
            await WaitUntilBgmClipAsync(bgmSource, "a");
        });

        /// <summary>
        /// 로드 중에 StopSFX를 호출하면, 로드가 끝나도 그 전에 요청한 효과음을 재생하면 안 됨.
        /// PlayOneShot은 AudioSource에 흔적을 남기지 않으므로 재생을 건너뛴 로그로 확인함.
        /// </summary>
        [UnityTest]
        public IEnumerator 로드_중_StopSFX를_호출하면_로드가_끝나도_재생하지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            PrepareSilentClip("a");

            // Start()의 의존성 누락 오류가 먼저 출력되게 한 프레임 넘김(아래 Assume으로 끝나도 예상 로그가 남도록).
            await UniTask.Yield();

            _soundManager.PlaySFX("a");
            // PlaySFX의 로드가 진행 중이면 PreloadAsync는 그 로드를 함께 기다리므로 아직 끝나지 않음.
            UniTask<bool> preload = _soundManager.PreloadAsync("a");
            AssumeLoadIsPending(preload.Status == UniTaskStatus.Pending);

            LogAssert.Expect(LogType.Log, new Regex("SFX request stopped while loading; skipped: a"));
            _soundManager.StopSFX();

            // 대조군: 같은 파일이 실제로 로드 가능해야 위 검증이 의미가 있음.
            Assert.IsTrue(await preload.AwaitWithRealtimeTimeout(), "효과음 파일을 읽지 못함");
            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
        });

        /// <summary>
        /// PreloadAsync로 미리 읽은 클립은 PlaySFX가 다시 읽지 않고 캐시에서 바로 재생해야 함.
        /// 파일을 지운 뒤 재생해, 다시 읽으려 하면 남는 "Failed to load sound" 오류가 없는지로 확인함
        /// (예상하지 않은 오류 로그는 테스트 프레임워크가 실패로 처리함).
        /// </summary>
        [UnityTest]
        public IEnumerator PreloadAsync로_읽은_클립은_PlaySFX가_다시_읽지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            PrepareSilentClip("a");

            bool isLoaded = await _soundManager.PreloadAsync("a").AwaitWithRealtimeTimeout();
            Assert.IsTrue(isLoaded, "효과음 파일을 읽지 못함");

            File.Delete(_clipPath);
            _soundManager.PlaySFX("a");

            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
        });

        /// <summary>
        /// PreloadAsync를 기다리던 호출자가 취소하면 OperationCanceledException을 받되, 이미 시작한 읽기는
        /// 멈추지 않고 캐시에 들어가야 함. 같은 클립을 함께 기다리는 PlaySFX가 있을 수 있어 읽기를 호출자 토큰으로 취소하지 않음.
        /// 캐시에 들어갔는지는 파일을 지운 뒤 PlaySFX가 "Failed to load sound" 오류 없이 끝나는지로 확인함.
        /// </summary>
        [UnityTest]
        public IEnumerator PreloadAsync를_취소해도_시작한_읽기는_캐시에_들어간다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            PrepareSilentClip("a");

            // Start()의 의존성 누락 오류가 먼저 출력되게 한 프레임 넘김(아래 Assume으로 끝나도 예상 로그가 남도록).
            await UniTask.Yield();

            bool isCanceled = false;
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                UniTask<bool> preload = _soundManager.PreloadAsync("a", cts.Token);
                AssumeLoadIsPending(preload.Status == UniTaskStatus.Pending);
                cts.Cancel();

                try
                {
                    await preload.AwaitWithRealtimeTimeout();
                }
                catch (OperationCanceledException)
                {
                    isCanceled = true;
                }
            }

            Assert.IsTrue(isCanceled, "취소했는데 OperationCanceledException이 전달되지 않음");

            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
            File.Delete(_clipPath);
            _soundManager.PlaySFX("a");
            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
        });

        /// <summary>
        /// 읽지 못한 클립이면 PreloadAsync가 false를 반환하고 원인을 오류 로그로 남겨야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator PreloadAsync는_읽지_못한_클립이면_false를_반환한다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            string missingPath = Path.Combine(Application.temporaryCachePath, "SoundManagerTests_missing.wav").Replace("\\", "/");
            SoundManagerTestHelper.SetSoundSetting(_soundManager, "missing", missingPath);

            // Start()의 의존성 누락 오류가 먼저 출력되게 한 프레임 넘김(LogAssert.Expect는 등록 순서대로 대조함).
            await UniTask.Yield();

            LogAssert.Expect(LogType.Error, new Regex("Failed to load sound.*SoundManagerTests_missing"));
            bool isLoaded = await _soundManager.PreloadAsync("missing").AwaitWithRealtimeTimeout();

            Assert.IsFalse(isLoaded, "읽지 못한 클립인데 true를 반환함");
        });

        /// <summary>
        /// 설정 로드 전에 들어온 재생 요청은 무시되더라도 원인을 알 수 있는 경고를 남겨야 함.
        /// </summary>
        [Test]
        public void 설정_로드_전_재생_요청은_경고를_남긴다()
        {
            LogAssert.Expect(LogType.Warning, new Regex("not loaded yet.*PlaySFX.*unknown_key"));

            _soundManager.PlaySFX("unknown_key");
        }

        /// <summary>
        /// 임시 폴더에 무음 WAV를 만들고 지정한 키로 등록함.
        /// clipPath가 절대 경로면 Path.Combine이 StreamingAssets 경로를 무시하므로 임시 파일을 그대로 가리킴.
        /// </summary>
        private void PrepareSilentClip(string key)
        {
            WriteSilentWav(_clipPath);
            SoundManagerTestHelper.SetSoundSetting(_soundManager, key, _clipPath);
        }

        /// <summary>
        /// 이 테스트들은 "요청 직후에는 로드가 아직 끝나지 않았다"는 상태에서만 경합을 재현할 수 있음.
        /// file:// 요청은 워커 스레드에서 읽혀서, 드물게 SendWebRequest 직후 이미 완료되어 동기적으로
        /// 끝나기도 함. 그 경우 재현 자체가 불가능하므로 실패가 아니라 결론 없음(Inconclusive)으로 처리함.
        /// </summary>
        private static void AssumeLoadIsPending(bool isPending)
        {
            Assume.That(isPending, "로드가 요청 직후 동기적으로 끝나 경합을 재현할 수 없음");
        }

        /// <summary>
        /// DI 없이 생성했으므로 Start에서 설정 제공자 미주입 오류가 출력됨. 프레임을 넘기는 테스트는 이를 예상해 둠.
        /// </summary>
        private static void ExpectMissingSettingsProviderError()
        {
            LogAssert.Expect(LogType.Error, new Regex("AppSettingsProvider was not injected"));
        }

        /// <summary>
        /// SoundManager가 만든 두 AudioSource 중 루프 재생하는 BGM용 소스를 반환함.
        /// </summary>
        private AudioSource GetBgmSource()
        {
            foreach (AudioSource source in _go.GetComponents<AudioSource>())
            {
                if (source.loop) return source;
            }

            Assert.Fail("BGM AudioSource를 찾지 못함");
            return null;
        }

        /// <summary>
        /// BGM 소스에 지정한 키의 클립이 할당될 때까지 실시간 제한 시간 안에서 기다림.
        /// </summary>
        private static UniTask WaitUntilBgmClipAsync(AudioSource bgmSource, string key)
        {
            return UniTask.WaitUntil(() => bgmSource.clip && bgmSource.clip.name == key).AwaitWithRealtimeTimeout();
        }

        /// <summary>
        /// 60초 길이(약 5MB)의 무음 16bit 모노 PCM WAV 파일을 만듦.
        /// file:// 요청은 워커 스레드에서 읽고 디코딩하므로, 파일이 작으면 요청 직후 이미 끝나 있는
        /// 경우가 잦음. 읽기·디코딩에 여러 프레임이 걸리도록 일부러 크게 만듦.
        /// </summary>
        private static void WriteSilentWav(string path)
        {
            const int sampleRate = 44100;
            const short channels = 1;
            const short bitsPerSample = 16;
            const int sampleCount = sampleRate * 60;
            int blockAlign = channels * bitsPerSample / 8;
            int dataSize = sampleCount * blockAlign;

            using (FileStream stream = new FileStream(path, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * blockAlign);
                writer.Write((short)blockAlign);
                writer.Write(bitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                writer.Write(new byte[dataSize]);
            }
        }
    }
}
