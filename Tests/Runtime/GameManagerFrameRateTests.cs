using NUnit.Framework;
using UnityEngine;
using HuliacDev.Core;
using HuliacDev.Data;

namespace HuliacDev.Tests
{
    /// <summary>
    /// GameManagerBase.ApplyFrameRateSettings의 수직 동기화·프레임 제한 규칙 검증.
    ///
    /// 배경: 예전에는 targetFrameRate가 0 이하면 아무것도 바꾸지 않아, 활성 품질 레벨의 vSyncCount가 0이면
    /// 수직 동기화도 프레임 제한도 없이 1000프레임 넘게 돌았음. 이제 0 이하면 수직 동기화를 켬.
    /// </summary>
    public class GameManagerFrameRateTests
    {
        private class TestGameManager : GameManagerBase
        {
            /// <summary>
            /// 설정을 넣고 프레임 설정 적용을 호출함.
            /// </summary>
            public void Apply(Settings loadedSettings)
            {
                settings = loadedSettings;
                ApplyFrameRateSettings();
            }
        }

        private GameObject _go;
        private TestGameManager _manager;
        private int _savedVSyncCount;
        private int _savedTargetFrameRate;

        /// <summary>
        /// 전역 설정을 저장하고, 꺼진 오브젝트에 매니저를 붙임(Awake·Start의 싱글톤 등록과 설정 로드를 돌리지 않으려는 것).
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _savedVSyncCount = QualitySettings.vSyncCount;
            _savedTargetFrameRate = Application.targetFrameRate;

            _go = new GameObject("TestGameManager");
            _go.SetActive(false);
            _manager = _go.AddComponent<TestGameManager>();
        }

        /// <summary>
        /// 오브젝트를 지우고 전역 설정을 원래대로 돌림.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (_go) Object.DestroyImmediate(_go);

            QualitySettings.vSyncCount = _savedVSyncCount;
            Application.targetFrameRate = _savedTargetFrameRate;
        }

        /// <summary>
        /// 품질 레벨의 vSyncCount가 0이어도 0 이하면 수직 동기화가 켜져야 함(꺼진 채 남으면 프레임 제한이 없음).
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        public void targetFrameRate가_0_이하면_수직_동기화를_켠다(int targetFrameRate)
        {
            QualitySettings.vSyncCount = 0;

            _manager.Apply(new Settings { targetFrameRate = targetFrameRate });

            Assert.AreEqual(1, QualitySettings.vSyncCount, "수직 동기화가 꺼진 채 남아 프레임 제한이 없음");
        }

        /// <summary>
        /// Settings.json에 키가 없으면 JsonUtility가 0으로 두므로 0과 같게 동작해야 함.
        /// </summary>
        [Test]
        public void targetFrameRate_키가_없으면_수직_동기화를_켠다()
        {
            QualitySettings.vSyncCount = 0;

            _manager.Apply(JsonUtility.FromJson<Settings>("{}"));

            Assert.AreEqual(1, QualitySettings.vSyncCount, "키가 없을 때 수직 동기화가 꺼진 채 남음");
        }

        /// <summary>
        /// 값을 지정하면 수직 동기화를 꺼야 Application.targetFrameRate가 적용됨(켜져 있으면 무시됨).
        /// </summary>
        [Test]
        public void targetFrameRate를_지정하면_수직_동기화를_끄고_그_값으로_제한한다()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;

            _manager.Apply(new Settings { targetFrameRate = 45 });

            Assert.AreEqual(0, QualitySettings.vSyncCount, "수직 동기화가 켜져 있으면 targetFrameRate가 무시됨");
            Assert.AreEqual(45, Application.targetFrameRate, "지정한 값으로 제한되지 않음");
        }
    }
}
