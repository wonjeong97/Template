using System;

namespace HuliacDev.Core
{
    /// <summary>
    /// 상태 전이 및 생명주기를 관리하는 제네릭 상태 머신.
    /// 상태 인스턴스를 재사용하여 런타임 힙 할당(GC)을 발생시키지 않습니다.
    /// </summary>
    /// <typeparam name="TContext">상태 머신을 소유하는 주체 타입</typeparam>
    public class StateMachine<TContext>
    {
        private readonly TContext _context;

        /// <summary> 현재 활성화된 상태. </summary>
        public IState<TContext> CurrentState { get; private set; }

        /// <summary> 직전 상태. </summary>
        public IState<TContext> PreviousState { get; private set; }

        /// <summary> 상태 변경 시 발생하는 이벤트 (이전 상태, 새 상태). </summary>
        public event Action<IState<TContext>, IState<TContext>> OnStateChanged;

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
        /// <param name="newState">전환할 새로운 상태 인스턴스</param>
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

            OnStateChanged?.Invoke(PreviousState, CurrentState);
        }

        /// <summary>
        /// 매 프레임 호출되어 현재 상태의 로직을 실행합니다.
        /// </summary>
        public void Update()
        {
            CurrentState?.Update(_context);
        }
    }

    /// <summary>
    /// 컨텍스트가 필요 없는 독립적인 상태 머신.
    /// </summary>
    public class StateMachine
    {
        /// <summary> 현재 활성화된 상태. </summary>
        public IState CurrentState { get; private set; }

        /// <summary> 직전 상태. </summary>
        public IState PreviousState { get; private set; }

        /// <summary> 상태 변경 시 발생하는 이벤트 (이전 상태, 새 상태). </summary>
        public event Action<IState, IState> OnStateChanged;

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

            OnStateChanged?.Invoke(PreviousState, CurrentState);
        }

        /// <summary>
        /// 매 프레임 호출되어 현재 상태의 로직을 실행합니다.
        /// </summary>
        public void Update()
        {
            CurrentState?.Update();
        }
    }
}
