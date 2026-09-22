using System;
using NUnit.Framework;
using HuliacDev.Core;

namespace HuliacDev.Tests
{
    public class StateMachineTests
    {
        private class TestState : IState
        {
            public int EnterCount { get; private set; }
            public int UpdateCount { get; private set; }
            public int ExitCount { get; private set; }

            public void Enter() => EnterCount++;
            public void Update() => UpdateCount++;
            public void Exit() => ExitCount++;
        }

        private class ContextTestState : IState<string>
        {
            public string LastContextReceived { get; private set; }
            public int EnterCount { get; private set; }

            public void Enter(string context)
            {
                LastContextReceived = context;
                EnterCount++;
            }

            public void Update(string context)
            {
                LastContextReceived = context;
            }

            public void Exit(string context)
            {
                LastContextReceived = context;
            }
        }

        private enum TestStateType
        {
            Idle,
            Move,
            Attack
        }

        [Test]
        public void 상태머신_초기상태_전이시_Enter가_호출된다()
        {
            var sm = new StateMachine<TestStateType>();
            var idle = new TestState();

            sm.AddState(TestStateType.Idle, idle);
            sm.ChangeState(TestStateType.Idle);

            Assert.AreEqual(1, idle.EnterCount);
            Assert.AreEqual(TestStateType.Idle, sm.CurrentStateType);
            Assert.AreSame(idle, sm.CurrentState);
        }

        [Test]
        public void 상태_전이시_이전상태_Exit와_새상태_Enter가_순차적으로_호출된다()
        {
            var sm = new StateMachine<TestStateType>();
            var idle = new TestState();
            var move = new TestState();

            sm.AddState(TestStateType.Idle, idle);
            sm.AddState(TestStateType.Move, move);

            sm.ChangeState(TestStateType.Idle);
            sm.ChangeState(TestStateType.Move);

            Assert.AreEqual(1, idle.ExitCount);
            Assert.AreEqual(1, move.EnterCount);
            Assert.AreEqual(TestStateType.Move, sm.CurrentStateType);
        }

        [Test]
        public void 동일상태로_중복전이시_무시된다()
        {
            var sm = new StateMachine<TestStateType>();
            var idle = new TestState();

            sm.AddState(TestStateType.Idle, idle);
            sm.ChangeState(TestStateType.Idle);
            sm.ChangeState(TestStateType.Idle);

            Assert.AreEqual(1, idle.EnterCount);
            Assert.AreEqual(0, idle.ExitCount);
        }

        [Test]
        public void Update호출시_현재상태의_Update만_수행된다()
        {
            var sm = new StateMachine<TestStateType>();
            var idle = new TestState();
            var move = new TestState();

            sm.AddState(TestStateType.Idle, idle);
            sm.AddState(TestStateType.Move, move);

            sm.ChangeState(TestStateType.Idle);
            sm.Update();
            sm.Update();

            Assert.AreEqual(2, idle.UpdateCount);
            Assert.AreEqual(0, move.UpdateCount);
        }

        [Test]
        public void 컨텍스트_기반_상태머신은_컨텍스트를_정상_전달한다()
        {
            const string context = "PlayerEntity";
            var sm = new StateMachine<TestStateType, string>(context);
            var idle = new ContextTestState();

            sm.AddState(TestStateType.Idle, idle);
            sm.ChangeState(TestStateType.Idle);

            Assert.AreEqual(1, idle.EnterCount);
            Assert.AreEqual(context, idle.LastContextReceived);
        }

        [Test]
        public void 등록되지_않은_상태_전이시_예외가_발생한다()
        {
            var sm = new StateMachine<TestStateType>();
            Assert.Throws<ArgumentException>(() => sm.ChangeState(TestStateType.Attack));
        }

        [Test]
        public void 상태변경_이벤트_발행을_정상_수신한다()
        {
            var sm = new StateMachine<TestStateType>();
            var idle = new TestState();
            var move = new TestState();

            sm.AddState(TestStateType.Idle, idle);
            sm.AddState(TestStateType.Move, move);

            (TestStateType prev, TestStateType next) lastTransition = default;
            int eventCount = 0;

            sm.StateChanged.Subscribe(transition =>
            {
                lastTransition = transition;
                eventCount++;
            });

            sm.ChangeState(TestStateType.Idle);
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual(TestStateType.Idle, lastTransition.next);

            sm.ChangeState(TestStateType.Move);
            Assert.AreEqual(2, eventCount);
            Assert.AreEqual(TestStateType.Idle, lastTransition.prev);
            Assert.AreEqual(TestStateType.Move, lastTransition.next);
        }
    }
}
