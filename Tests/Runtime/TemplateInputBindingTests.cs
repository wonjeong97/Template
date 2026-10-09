using NUnit.Framework;
using UnityEngine.InputSystem;

namespace HuliacDev.Tests
{
    /// <summary>
    /// TemplateInputActions의 System 단축키 기본 바인딩과, RootLifetimeScope.ConfigureInputBindings에서
    /// 바인딩을 바꾸는 README 예시가 실제로 동작하는지 검증함.
    /// InputTestFixture로 실제 장치 입력을 막은 가상 입력 환경에서 실행함.
    /// </summary>
    public class TemplateInputBindingTests : InputTestFixture
    {
        private TemplateInputActions _inputActions;
        private Keyboard _keyboard;
        private int _performedCount;

        /// <summary>
        /// 가상 키보드와 입력 액션을 만들고 포커스 복구 토글 실행 횟수를 셈.
        /// </summary>
        [SetUp]
        public void SetUpInput()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _inputActions = new TemplateInputActions();
            _performedCount = 0;
            _inputActions.System.ToggleFocusRestore.performed += _ => _performedCount++;
        }

        /// <summary>
        /// 입력 액션을 해제함. 실제 입력 환경 복원은 이후 기반 클래스의 TearDown이 담당함.
        /// </summary>
        [TearDown]
        public void TearDownInput()
        {
            _inputActions.Dispose();
        }

        /// <summary>
        /// 기본 바인딩에서는 F 키 하나로 포커스 복구 토글이 실행되어야 함.
        /// </summary>
        [Test]
        public void 기본_바인딩은_F_키로_포커스_복구를_토글한다()
        {
            _inputActions.Enable();

            PressAndRelease(_keyboard.fKey);

            Assert.AreEqual(1, _performedCount, "F 키로 ToggleFocusRestore가 실행되지 않음");
        }

        /// <summary>
        /// README 예시처럼 Ctrl+F 조합으로 바꾸면, 스캐너가 보내는 것과 같은 F 단독 입력에는 실행되지 않고
        /// Ctrl을 누른 채 F를 눌렀을 때만 실행되어야 함.
        /// </summary>
        [Test]
        public void Ctrl_F로_바꾸면_F_단독_입력에는_토글하지_않는다()
        {
            InputAction toggle = _inputActions.System.ToggleFocusRestore;
            toggle.ChangeBinding(0).Erase();
            toggle.AddCompositeBinding("OneModifier")
                .With("Modifier", "<Keyboard>/ctrl")
                .With("Binding", "<Keyboard>/f");
            _inputActions.Enable();

            PressAndRelease(_keyboard.fKey);
            Assert.AreEqual(0, _performedCount, "F 단독 입력으로 토글됨");

            Press(_keyboard.leftCtrlKey);
            PressAndRelease(_keyboard.fKey);
            Release(_keyboard.leftCtrlKey);
            Assert.AreEqual(1, _performedCount, "Ctrl+F로 토글되지 않음");
        }
    }
}
