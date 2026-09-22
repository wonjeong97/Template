namespace HuliacDev.Core
{
    /// <summary>
    /// 컨텍스트를 전달받는 유한 상태 머신(FSM)의 개별 상태 인터페이스.
    /// </summary>
    /// <typeparam name="TContext">상태를 소유하거나 제어할 대상 객체 타입</typeparam>
    public interface IState<in TContext>
    {
        /// <summary> 상태 진입 시 1회 호출. </summary>
        void Enter(TContext context);

        /// <summary> 상태 유지 중 매 프레임 호출. </summary>
        void Update(TContext context);

        /// <summary> 상태 종료(이탈) 시 1회 호출. </summary>
        void Exit(TContext context);
    }

    /// <summary>
    /// 컨텍스트가 필요 없는 독립적인 상태 인터페이스.
    /// </summary>
    public interface IState
    {
        /// <summary> 상태 진입 시 1회 호출. </summary>
        void Enter();

        /// <summary> 상태 유지 중 매 프레임 호출. </summary>
        void Update();

        /// <summary> 상태 종료(이탈) 시 1회 호출. </summary>
        void Exit();
    }
}
