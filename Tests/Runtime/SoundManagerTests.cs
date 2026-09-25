using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using HuliacDev.UI;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// SoundManager의 BGM 요청 순서 보장과 무시된 재생 요청의 로그 검증.
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

            _clipPath = Path.Combine(Application.temporaryCachePath, "SoundManagerTests.wav").Replace("\\", "/");
            WriteSilentWav(_clipPath);

            // clipPath가 절대 경로면 Path.Combine이 StreamingAssets 경로를 무시하므로 임시 파일을 그대로 가리킴.
            SoundManagerTestHelper.SetSoundSetting(_soundManager, "a", _clipPath);
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
        /// 설정 로드 전에 들어온 재생 요청은 무시되더라도 원인을 알 수 있는 경고를 남겨야 함.
        /// </summary>
        [Test]
        public void 설정_로드_전_재생_요청은_경고를_남긴다()
        {
            LogAssert.Expect(LogType.Warning, new Regex("not loaded yet.*PlaySFX.*unknown_key"));

            _soundManager.PlaySFX("unknown_key");
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
