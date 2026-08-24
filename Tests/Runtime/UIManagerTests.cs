using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Wonjeong.Data;
using Wonjeong.UI;
using ZLogger.Unity;

namespace Wonjeong.Tests
{
    /// <summary>
    /// UIManager의 폰트 대기열 및 UI 설정 적용 검증.
    ///
    /// 설정 로드는 비동기이므로, 로드가 끝나기 전에 SetText/SetButton이 호출되는 레이스가
    /// 실제로 발생한다. 이때 폰트 키를 성급히 거부하면 이후 폰트가 로드되어도 적용 대상을
    /// 찾지 못해 폰트가 영구히 누락된다(be022c0에서 수정).
    /// </summary>
    public class UIManagerTests
    {
        private GameObject _managerGo;
        private UIManager _uiManager;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private static readonly BindingFlags Nonpublic = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp()
        {
            // Awake만 돌고 Start(설정 로드)는 아직 실행되지 않은 상태를 사용함.
            _managerGo = new GameObject("UIManagerTests");
            _uiManager = _managerGo.AddComponent<UIManager>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _spawned.Clear();

            if (_managerGo != null) UnityEngine.Object.DestroyImmediate(_managerGo);
        }

        /// <summary>
        /// 설정 로드 전에는 키 유효성을 판단할 수 없으므로 무조건 대기열에 넣어야 함.
        /// 여기서 거부하면 폰트가 영구히 적용되지 않는다.
        /// </summary>
        [Test]
        public void 설정_로드_전에_요청된_폰트는_대기열에_보관된다()
        {
            Assert.IsFalse(GetIsSettingsLoaded(), "설정이 아직 로드되지 않은 상태여야 함");

            SetText("Label", "body");

            Assert.AreEqual(1, GetPendingKeyCount(),
                "설정 로드 전 요청이 대기열에 들어가지 않으면 폰트가 영구히 누락됨");
        }

        /// <summary>
        /// 설정 로드 시점에 유효성을 판정하여, 실제로 존재하지 않는 키만 정리해야 함.
        /// </summary>
        [Test]
        public void 설정_로드_후_알_수_없는_폰트키만_대기열에서_제거된다()
        {
            SetText("Valid", "body");
            SetText("Invalid", "없는키");
            Assert.AreEqual(2, GetPendingKeyCount(), "로드 전에는 둘 다 보관되어야 함");

            SimulateSettingsLoaded("body", "Fonts/Sample");

            Assert.AreEqual(1, GetPendingKeyCount(), "유효한 키만 남아야 함");
            Assert.IsTrue(GetPendingKeys().Contains("body"));
        }

        /// <summary>
        /// 설정 로드가 끝난 뒤에는 알 수 없는 키를 즉시 거부해야 함.
        /// </summary>
        [Test]
        public void 설정_로드_후_알_수_없는_폰트키는_즉시_거부된다()
        {
            SimulateSettingsLoaded("body", "Fonts/Sample");

            SetText("AfterLoad", "또다른없는키");

            Assert.AreEqual(0, GetPendingKeyCount(), "로드 후 무효 키는 대기열에 쌓이지 않아야 함");
        }

        /// <summary>
        /// 폰트 외 텍스트 속성은 설정 로드 여부와 무관하게 즉시 적용되어야 함.
        /// </summary>
        [Test]
        public void 텍스트_속성은_설정_로드와_무관하게_즉시_적용된다()
        {
            GameObject go = SetText("Styled", null, "안녕하세요", 33, TextAnchor.LowerRight);

            Text text = go.GetComponent<Text>();
            Assert.AreEqual("안녕하세요", text.text);
            Assert.AreEqual(33, text.fontSize);
            Assert.AreEqual(TextAnchor.LowerRight, text.alignment);
        }

        /// <summary>
        /// RectTransform 속성이 설정값대로 적용되어야 함.
        /// </summary>
        [Test]
        public void RectTransform_속성이_설정값대로_적용된다()
        {
            GameObject go = new GameObject("Placed");
            _spawned.Add(go);

            TextSetting setting = new TextSetting
            {
                name = "Placed",
                text = "x",
                position = new Vector2(120f, -45f),
                size = new Vector2(300f, 80f),
                scale = new Vector3(2f, 2f, 2f)
            };
            _uiManager.SetText(go, setting);

            RectTransform rt = go.GetComponent<RectTransform>();
            Assert.AreEqual(new Vector2(120f, -45f), rt.anchoredPosition);
            Assert.AreEqual(new Vector2(300f, 80f), rt.sizeDelta);
            Assert.AreEqual(new Vector3(2f, 2f, 2f), rt.localScale);
        }

        /// <summary>
        /// null 인자에 예외 없이 방어해야 함.
        /// </summary>
        [Test]
        public void null_인자를_받아도_예외가_발생하지_않는다()
        {
            Assert.DoesNotThrow(() => _uiManager.SetText(null, new TextSetting()));
            Assert.DoesNotThrow(() => _uiManager.SetText(new GameObject("Tmp"), null));
            Assert.DoesNotThrow(() => _uiManager.SetImage(null, new ImageSetting()));
            Assert.DoesNotThrow(() => _uiManager.SetButton(null, new ButtonSetting()));
        }

        /// <summary>
        /// 캐시 해제가 예외 없이 반복 호출 가능해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator ClearSpriteCache는_반복_호출해도_안전하다() => UniTask.ToCoroutine(async () =>
        {
            // 이 테스트는 프레임을 넘기므로 Start()가 실행됨. 주입 없이 생성한 인스턴스이므로
            // 의존성 누락 안내 로그가 1회 출력되는 것이 정상 동작임.
            LogAssert.Expect(LogType.Error, new Regex("Dependencies were not injected"));

            _uiManager.ClearSpriteCache();
            _uiManager.ClearSpriteCache();

            await UniTask.Yield();

            _uiManager.ClearSpriteCache();
        });

        /// <summary>
        /// 주입 없이 컴포넌트만 붙인 경우, 원인 불명의 NullReferenceException 대신
        /// 무엇을 빠뜨렸는지 알려주는 오류가 출력되어야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 의존성_주입_누락시_안내_오류를_출력한다() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.Expect(LogType.Error, new Regex("LifetimeScope"));

            // Start()가 실행되도록 한 프레임 넘김
            await UniTask.Yield();
            await UniTask.Yield();
        });

        /// <summary>
        /// SetButton으로 등록한 buttonSound가 클릭 시 SoundManager.PlaySFX로 실제 전달되는지 검증.
        /// PlaySFX는 키를 찾으면 비동기 로드를 시작하는데, 존재하지 않는 clipPath를 넣어 로드가
        /// 실패하도록 만들고 그 실패 로그(클립 경로 포함)를 관찰함으로써 클릭→SoundManager 연결을
        /// 검증함. 로컬 파일 미존재는 UnityWebRequest가 프레임 진행 없이 동기적으로 실패를
        /// 확정하므로 프레임을 넘기지 않아도 됨 — 일부러 프레임을 넘기지 않아 이 테스트가
        /// _uiManager(DI 없이 생성됨)의 Start()를 건드리지 않도록 함(다른 로그와 순서 충돌 방지).
        /// </summary>
        [Test]
        public void 버튼_클릭시_buttonSound가_SoundManager로_전달된다()
        {
            GameObject soundGo = new GameObject("SoundManagerForButtonTest");
            _spawned.Add(soundGo);
            SoundManager soundManager = soundGo.AddComponent<SoundManager>();

            SetSoundManagerLogger(soundManager);
            SetSoundManager(_uiManager, soundManager);
            SetSoundSetting(soundManager, "click", "sounds/UIManagerTests_존재하지않는파일.wav");

            GameObject buttonGo = new GameObject("SoundButton");
            _spawned.Add(buttonGo);

            ButtonSetting setting = new ButtonSetting { name = "SoundButton", buttonSound = "click" };
            _uiManager.SetButton(buttonGo, setting);

            LogAssert.Expect(LogType.Error, new Regex("Failed to load sound.*UIManagerTests_존재하지않는파일"));

            buttonGo.GetComponent<Button>().onClick.Invoke();
        }

        /// <summary>
        /// SoundManager가 씬에 없어(주입되지 않아) null인 상태로 buttonSound가 설정된 버튼을
        /// 클릭해도 예외가 발생하면 안 됨.
        /// </summary>
        [Test]
        public void SoundManager가_없어도_buttonSound_클릭시_예외가_발생하지_않는다()
        {
            GameObject buttonGo = new GameObject("SoundButtonNoManager");
            _spawned.Add(buttonGo);

            ButtonSetting setting = new ButtonSetting { name = "SoundButtonNoManager", buttonSound = "click" };
            _uiManager.SetButton(buttonGo, setting);

            Assert.DoesNotThrow(() => buttonGo.GetComponent<Button>().onClick.Invoke());
        }

        // --- 헬퍼 ---

        private GameObject SetText(string name, string fontName, string content = "text",
            int fontSize = 20, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);

            TextSetting setting = new TextSetting
            {
                name = name,
                text = content,
                fontName = fontName,
                fontSize = fontSize,
                alignment = alignment,
                size = new Vector2(200f, 40f),
                scale = Vector3.one
            };
            _uiManager.SetText(go, setting);

            return go;
        }

        /// <summary>
        /// 실제 파일 로드 없이 '설정 로드 완료' 상태를 재현함.
        /// </summary>
        private void SimulateSettingsLoaded(string key, string address)
        {
            FontSetting[] fonts = { new FontSetting { key = key, address = address } };

            typeof(UIManager).GetMethod("CacheFontAddresses", Nonpublic)
                .Invoke(_uiManager, new object[] { fonts });
            typeof(UIManager).GetField("_isSettingsLoaded", Nonpublic)
                .SetValue(_uiManager, true);
            typeof(UIManager).GetMethod("DiscardUnknownPendingFonts", Nonpublic)
                .Invoke(_uiManager, null);
        }

        private bool GetIsSettingsLoaded()
        {
            return (bool)typeof(UIManager).GetField("_isSettingsLoaded", Nonpublic).GetValue(_uiManager);
        }

        private ICollection<string> GetPendingKeys()
        {
            FieldInfo field = typeof(UIManager).GetField("_pendingLabels", Nonpublic);
            var dict = (Dictionary<string, HashSet<Text>>)field.GetValue(_uiManager);
            return dict.Keys;
        }

        private int GetPendingKeyCount()
        {
            return GetPendingKeys().Count;
        }

        private static void SetSoundManager(UIManager uiManager, SoundManager soundManager)
        {
            typeof(UIManager).GetField("_soundManager", Nonpublic).SetValue(uiManager, soundManager);
        }

        private static void SetSoundSetting(SoundManager soundManager, string key, string clipPath)
        {
            FieldInfo field = typeof(SoundManager).GetField("_soundSettings", Nonpublic);
            var settings = (Dictionary<string, SoundSetting>)field.GetValue(soundManager);
            settings[key] = new SoundSetting { key = key, clipPath = clipPath, volume = 1f };
        }

        /// <summary>
        /// SoundManager의 실패 로그가 실제로 콘솔에 출력되도록, DI 없이 생성된 인스턴스에
        /// ZLogger 기반 로거를 직접 주입함(RootLifetimeScope.ConfigureLogging의 최소 재현).
        /// </summary>
        private static void SetSoundManagerLogger(SoundManager soundManager)
        {
            ILoggerFactory factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddZLoggerUnityDebug();
            });

            ILogger<SoundManager> logger = factory.CreateLogger<SoundManager>();
            typeof(SoundManager).GetField("_logger", Nonpublic).SetValue(soundManager, logger);
        }
    }
}
