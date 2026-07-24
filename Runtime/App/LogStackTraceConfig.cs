using UnityEngine;

namespace Wonjeong.App
{
    /// <summary>
    /// 로그 타입별 스택 트레이스 정책을 전역으로 설정함.
    /// <para>
    /// 일반 로그(Info/<see cref="LogType.Log"/>)는 스택 트레이스를 제거하고(메시지 자체는 유지),
    /// Warning 이상(Warning/Error/Exception/Assert)만 스택 트레이스를 남김(<see cref="StackTraceLogType.ScriptOnly"/>).
    /// </para>
    /// <para>
    /// 이유:
    /// (1) 정보성 로그(플레이 기록 등)에 매번 붙는 스택은 노이즈이고 원인 파악에 도움이 안 되는 반면,
    ///     Warning 이상에는 발생 라인·호출 경로가 필요함.
    /// (2) 성능: 로그마다 스택을 캡처하면 CPU(스택 워크) + GC 할당이 발생해, 로그가 잦거나
    ///     WebGL/모바일 타깃일수록 프레임 히칭 요인이 됨. 일반 로그의 스택만 꺼서 이를 없앰.
    /// </para>
    /// <para>
    /// ZLogger의 LogLevel은 Unity <see cref="LogType"/>으로 매핑되므로(Information→Log, Warning→Warning, Error→Error)
    /// <see cref="Application.SetStackTraceLogType"/>을 LogType별로 걸면 ZLog* 호출에 그대로 적용됨.
    /// </para>
    /// <para>
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/>로 모든 프로젝트에서 부팅 시(에디터 플레이·빌드 모두)
    /// 자동 1회 적용됨. 특정 프로젝트가 다른 정책을 원하면 이후 <see cref="Application.SetStackTraceLogType"/>을
    /// 다시 호출해 덮어쓰면 됨.
    /// </para>
    /// <para>
    /// ZLogger 연동 주의: ZLogger의 Unity 콘솔 프로바이더는 이 값을
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>(가장 이른 RuntimeInitialize 단계)에서
    /// 한 번 캐싱해 "스택을 직접 붙일지"를 결정한다. 따라서 이 정적 메서드의 실행 시점을 아무리 앞당겨도
    /// ZLogger의 캐싱을 앞지를 수 없어, ZLog* 콘솔 출력에는 일반 로그 스택 제거가 반영되지 않는다.
    /// 이 문제는 <c>RootLifetimeScope.ConfigureLogging</c>에서 ZLogger의 수동 스택 기능(PrettyStacktrace)을
    /// 꺼서 해결하며, 그렇게 하면 ZLogger도 여기서 정한 Unity 네이티브 정책을 그대로 따르게 된다.
    /// (Unity 네이티브는 이 값을 런타임에 매 로그마다 읽으므로, 직접 호출하는 UnityEngine.Debug.Log 계열은
    ///  실행 시점의 영향을 받지 않는다. 여기서는 가장 이른 AfterAssembliesLoaded에 걸어 초기 로그까지 커버한다.)
    /// </para>
    /// </summary>
    public static class LogStackTraceConfig
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Apply()
        {
            Application.SetStackTraceLogType(LogType.Log,       StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning,   StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Error,     StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Assert,    StackTraceLogType.ScriptOnly);
        }
    }
}
