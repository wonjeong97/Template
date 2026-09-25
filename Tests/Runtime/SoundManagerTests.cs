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
    /// 로드가 실제로 비동기로 진행되도록 임시 폴더에 작은 WAV 파일을 만들어 사용함.
    /// </summary>
    public class SoundManagerTests
    {
        /// <summary>로드가 끝났을 만큼 충분히 기다리는 실시간 시간(초).</summary>
        private const float LoadSettleSeconds = 1f;

        private GameObject _go;
        private SoundManager _soundManager;
        private string _clipPathA;
        private string _clipPathB;

        [SetUp]
        public void SetUp()
        {
            SingletonGuard<SoundManager>.ResetForTesting();

            _go = new GameObject("SoundManagerTests");
            _soundManager = _go.AddComponent<SoundManager>();
            SoundManagerTestHelper.SetLogger(_soundManager);

            _clipPathA = Path.Combine(Application.temporaryCachePath, "SoundManagerTests_a.wav").Replace("\\", "/");
            _clipPathB = Path.Combine(Application.temporaryCachePath, "SoundManagerTests_b.wav").Replace("\\", "/");
            WriteSilentWav(_clipPathA);
            WriteSilentWav(_clipPathB);

            // clipPath가 절대 경로면 Path.Combine이 StreamingAssets 경로를 무시하므로 임시 파일을 그대로 가리킴.
            SoundManagerTestHelper.SetSoundSetting(_soundManager, "a", _clipPathA);
            SoundManagerTestHelper.SetSoundSetting(_soundManager, "b", _clipPathB);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            SingletonGuard<SoundManager>.ResetForTesting();

            if (File.Exists(_clipPathA)) File.Delete(_clipPathA);
            if (File.Exists(_clipPathB)) File.Delete(_clipPathB);
        }

        /// <summary>
        /// 로드 중에 StopBGM을 호출하면, 로드가 끝나도 BGM이 재생(할당)되면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 로드_중_StopBGM을_호출하면_로드가_끝나도_재생하지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            AudioSource bgmSource = GetBgmSource();

            _soundManager.PlayBGM("a");
            Assert.IsFalse(bgmSource.clip, "전제 조건: 로드가 비동기로 진행 중이어야 함");
            _soundManager.StopBGM();

            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
            Assert.IsFalse(bgmSource.clip, "정지한 BGM이 로드 완료 후 되살아남");

            // 대조군: 같은 파일이 실제로 로드 가능해야 위 검증이 의미가 있음.
            _soundManager.PlayBGM("a");
            await WaitUntilBgmClipAsync(bgmSource, "a");
        });

        /// <summary>
        /// 캐시에 없는 BGM A를 요청한 직후 캐시된 BGM B를 요청하면, 늦게 도착한 A가 B를 덮어쓰면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 늦게_도착한_이전_BGM이_새로_요청한_BGM을_덮어쓰지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingSettingsProviderError();
            AudioSource bgmSource = GetBgmSource();

            _soundManager.PlayBGM("b");
            await WaitUntilBgmClipAsync(bgmSource, "b");

            _soundManager.PlayBGM("a");
            Assert.AreEqual("b", bgmSource.clip.name, "전제 조건: A의 로드가 비동기로 진행 중이어야 함");
            _soundManager.PlayBGM("b");

            await UniTask.Delay(TimeSpan.FromSeconds(LoadSettleSeconds), DelayType.UnscaledDeltaTime);
            Assert.AreEqual("b", bgmSource.clip.name, "늦게 도착한 이전 BGM이 새로 요청한 BGM을 덮어씀");
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
        /// 0.1초 길이의 무음 16bit 모노 PCM WAV 파일을 만듦.
        /// </summary>
        private static void WriteSilentWav(string path)
        {
            const int sampleRate = 8000;
            const short channels = 1;
            const short bitsPerSample = 16;
            const int sampleCount = 800;
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
