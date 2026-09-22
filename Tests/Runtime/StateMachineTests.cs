using System.Collections.Generic;
using NUnit.Framework;
using R3;
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

        [Test]
        public void 초기상태를_생성자로_넘기면_Enter가_호출된다()
        {
            TestState idle = new TestState();
            StateMachine sm = new StateMachine(idle);

            Assert.AreEqual(1, idle.EnterCount);
            Assert.AreSame(idle, sm.CurrentState);

            sm.Dispose();
        }

        [Test]
        public void 상태_전이시_이전상태_Exit와_새상태_Enter가_순차적으로_호출된다()
        {
            TestState idle = new TestState();
            TestState move = new TestState();
            StateMachine sm = new StateMachine(idle);

            sm.ChangeState(move);

            Assert.AreEqual(1, idle.ExitCount);
            Assert.AreEqual(1, move.EnterCount);
            Assert.AreSame(move, sm.CurrentState);
            Assert.AreSame(idle, sm.PreviousState);

            sm.Dispose();
        }

        [Test]
        public void 동일상태로_중복전이시_무시된다()
        {
            TestState idle = new TestState();
            StateMachine sm = new StateMachine(idle);

            sm.ChangeState(idle);

            Assert.AreEqual(1, idle.EnterCount);
            Assert.AreEqual(0, idle.ExitCount);

            sm.Dispose();
        }

        [Test]
        public void null로_전이하면_무시된다()
        {
            TestState idle = new TestState();
            StateMachine sm = new StateMachine(idle);

            sm.ChangeState(null);

            Assert.AreSame(idle, sm.CurrentState);
            Assert.AreEqual(0, idle.ExitCount);

            sm.Dispose();
        }

        [Test]
        public void Update호출시_현재상태의_Update만_수행된다()
        {
            TestState idle = new TestState();
            TestState move = new TestState();
            StateMachine sm = new StateMachine(idle);

            sm.Update();
            sm.Update();

            Assert.AreEqual(2, idle.UpdateCount);
            Assert.AreEqual(0, move.UpdateCount);

            sm.Dispose();
        }

        [Test]
        public void 컨텍스트_기반_상태머신은_컨텍스트를_정상_전달한다()
        {
            const string context = "PlayerEntity";
            ContextTestState idle = new ContextTestState();
            StateMachine<string> sm = new StateMachine<string>(context, idle);

            Assert.AreEqual(1, idle.EnterCount);
            Assert.AreEqual(context, idle.LastContextReceived);

            sm.Dispose();
        }

        [Test]
        public void 상태변경_스트림을_정상_수신한다()
        {
            TestState idle = new TestState();
            TestState move = new TestState();
            StateMachine sm = new StateMachine();

            List<(IState Previous, IState Current)> transitions = new List<(IState, IState)>();

            using (sm.StateChanged.Subscribe(transition => transitions.Add(transition)))
            {
                sm.ChangeState(idle);
                Assert.AreEqual(1, transitions.Count);
                Assert.IsNull(transitions[0].Previous);
                Assert.AreSame(idle, transitions[0].Current);

                sm.ChangeState(move);
                Assert.AreEqual(2, transitions.Count);
                Assert.AreSame(idle, transitions[1].Previous);
                Assert.AreSame(move, transitions[1].Current);

                // 동일 상태 재진입은 발행되지 않아야 함
                sm.ChangeState(move);
                Assert.AreEqual(2, transitions.Count);
            }

            sm.Dispose();
        }
    }
}
