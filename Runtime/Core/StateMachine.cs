using System;
using R3;

namespace HuliacDev.Core
{
    /// <summary>
    /// 상태 전이 및 생명주기를 관리하는 제네릭 상태 머신.
    /// 상태 인스턴스를 재사용하여 런타임 힙 할당(GC)을 발생시키지 않습니다.
    /// </summary>
    /// <typeparam name="TContext">상태 머신을 소유하는 주체 타입</typeparam>
    public class StateMachine<TContext> : IDisposable
    {
        private readonly TContext _context;

        private readonly Subject<(IState<TContext> Previous, IState<TContext> Current)> _stateChanged =
            new Subject<(IState<TContext>, IState<TContext>)>();

        /// <summary> 현재 활성화된 상태. </summary>
        public IState<TContext> CurrentState { get; private set; }

        /// <summary> 직전 상태. </summary>
        public IState<TContext> PreviousState { get; private set; }

        /// <summary> 상태가 바뀔 때마다 (이전 상태, 새 상태)를 발행하는 스트림. </summary>
        public Observable<(IState<TContext> Previous, IState<TContext> Current)> StateChanged => _stateChanged;

        /// <summary>
        /// 주체와 초기 상태를 받아 상태 머신을 구성합니다.
        /// </summary>
        public StateMachine(TContext context, IState<TContext> initialState = null)
        {
            _context = context;
            if (initialState != null)
            {
                ChangeState(initialState);
            }
        }

        /// <summary>
        /// 새로운 상태로 전환합니다.
        /// 동일 상태로의 재진입은 무시됩니다.
        /// </summary>
        public void ChangeState(IState<TContext> newState)
        {
            if (newState == null || ReferenceEquals(CurrentState, newState))
            {
                return;
            }

            IState<TContext> fromState = CurrentState;
            CurrentState?.Exit(_context);

            PreviousState = fromState;
            CurrentState = newState;
            CurrentState.Enter(_context);

            _stateChanged.OnNext((PreviousState, CurrentState));
        }

        /// <summary>
        /// 매 프레임 호출되어 현재 상태의 로직을 실행합니다.
        /// </summary>
        public void Update()
        {
            CurrentState?.Update(_context);
        }

        /// <summary>
        /// 상태 변경 스트림을 완료 처리하고 해제합니다.
        /// </summary>
        public void Dispose()
        {
            _stateChanged.OnCompleted();
            _stateChanged.Dispose();
        }
    }

    /// <summary>
    /// 컨텍스트가 필요 없는 독립적인 상태 머신.
    /// </summary>
    public class StateMachine : IDisposable
    {
        private readonly Subject<(IState Previous, IState Current)> _stateChanged =
            new Subject<(IState, IState)>();

        /// <summary> 현재 활성화된 상태. </summary>
        public IState CurrentState { get; private set; }

        /// <summary> 직전 상태. </summary>
        public IState PreviousState { get; private set; }

        /// <summary> 상태가 바뀔 때마다 (이전 상태, 새 상태)를 발행하는 스트림. </summary>
        public Observable<(IState Previous, IState Current)> StateChanged => _stateChanged;

        /// <summary>
        /// 초기 상태를 받아 상태 머신을 구성합니다.
        /// </summary>
        public StateMachine(IState initialState = null)
        {
            if (initialState != null)
            {
                ChangeState(initialState);
            }
        }

        /// <summary>
        /// 새로운 상태로 전환합니다.
        /// 동일 상태로의 재진입은 무시됩니다.
        /// </summary>
        public void ChangeState(IState newState)
        {
            if (newState == null || ReferenceEquals(CurrentState, newState))
            {
                return;
            }

            IState fromState = CurrentState;
            CurrentState?.Exit();

            PreviousState = fromState;
            CurrentState = newState;
            CurrentState.Enter();

            _stateChanged.OnNext((PreviousState, CurrentState));
        }

        /// <summary>
        /// 매 프레임 호출되어 현재 상태의 로직을 실행합니다.
        /// </summary>
        public void Update()
        {
            CurrentState?.Update();
        }

        /// <summary>
        /// 상태 변경 스트림을 완료 처리하고 해제합니다.
        /// </summary>
        public void Dispose()
        {
            _stateChanged.OnCompleted();
            _stateChanged.Dispose();
        }
    }
}
