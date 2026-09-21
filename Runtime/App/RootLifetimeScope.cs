using System;
using System.IO;
using MessagePipe;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Wonjeong.Core;
using Wonjeong.Data;
using Wonjeong.Hardware;
using Wonjeong.Network;
using Wonjeong.UI;
using Wonjeong.Utils;
using ZLogger;
using ZLogger.Providers;
using ZLogger.Unity;

namespace Wonjeong.App
{
    public struct InspectorEvent { }

    public struct InactivityTimeoutEvent { }

    public struct BeforeShutdownEvent { }

    public struct MoveIdleEvent { }

    /// <summary>
    /// 외부 API(wavespeed, gpt 등) 호출 시작 시 서버 로그 전송용 이벤트.
    /// ApiManagerBase가 이를 구독해 "Call API {RequestUrl}" 로그를 전송함.
    /// </summary>
    public readonly struct ExternalApiCallEvent
    {
        public string RequestUrl { get; }

        public ExternalApiCallEvent(string requestUrl)
        {
            RequestUrl = requestUrl;
        }
    }

    /// <summary>
    /// 외부 API(wavespeed, gpt 등) 호출 완료 시 서버 로그 전송용 이벤트.
    /// ApiManagerBase가 이를 구독해 "Return OK" 또는 "Return fail" 로그를 전송함.
    /// </summary>
    public readonly struct ExternalApiReturnEvent
    {
        public bool IsSuccess { get; }

        public ExternalApiReturnEvent(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }
    }

    public class RootLifetimeScope : LifetimeScope
    {
        /// <summary>
        /// VContainer의 의존성 주입 컨테이너를 구성함.
        /// 로깅 및 메시지 파이프 시스템을 초기화함.
        /// </summary>
        protected override void Configure(IContainerBuilder builder)
        {
            ConfigureLogging(builder);
            ConfigureLogRetention(builder);
            ConfigureMessagePipe(builder);
            ConfigureSettings(builder);
            ConfigureNetwork(builder);
            ConfigureCoreComponents(builder);
            ConfigureOptionalComponents(builder);
        }

        /// <summary>
        /// 실시간 네트워크 모니터링 서비스를 등록함.
        /// </summary>
        protected virtual void ConfigureNetwork(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<NetworkStatusService>(Lifetime.Singleton).AsSelf();
        }

        /// <summary>
        /// 모든 프로젝트에 공통으로 포함되는 씬 컴포넌트를 등록함.
        /// 각 프로젝트의 파생 스코프마다 반복 등록하던 것을 베이스로 일원화함.
        /// <para>
        /// 주의: RegisterComponentInHierarchy는 해당 컴포넌트가 씬에 없으면
        /// 컨테이너 빌드 시점에 예외가 발생함. 예외적으로 이 컴포넌트들을 쓰지 않는
        /// 프로젝트는 이 메서드를 override하여 등록을 제외할 것.
        /// </para>
        /// </summary>
        protected virtual void ConfigureCoreComponents(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<SystemCanvas>();
            builder.RegisterComponentInHierarchy<GameCloser>();
        }

        /// <summary>
        /// 프로젝트에 따라 쓰거나 안 쓰는 선택적 매니저를 씬 존재 여부로 자동 등록함.
        /// 씬에 배치되어 있으면 등록·주입되고, 없으면 조용히 건너뜀.
        /// "씬에 배치하는 행위" 자체가 사용 선언이므로, 파생 스코프에서 등록 목록을
        /// 관리하다 누락 시 발생하던 미주입 NRE와, 씬에 없는 컴포넌트를 등록해
        /// 컨테이너 빌드가 실패하는 문제를 모두 방지함.
        /// </summary>
        protected virtual void ConfigureOptionalComponents(IContainerBuilder builder)
        {
            RegisterIfPresentInScene<FadeManager>(builder);
            RegisterIfPresentInScene<UIManager>(builder);
            RegisterIfPresentInScene<SoundManager>(builder);
            RegisterIfPresentInScene<VideoManager>(builder);
            RegisterIfPresentInScene<ArduinoManager>(builder);
            RegisterIfPresentInScene<ApiManagerBase>(builder);
            RegisterIfPresentInScene<InactivityTimer>(builder);
            RegisterIfPresentInScene<ShutdownScheduler>(builder);
        }

        /// <summary>
        /// 컴포넌트가 이 스코프가 속한 씬에 존재할 때만 RegisterComponentInHierarchy를 수행함.
        /// 등록했는지 여부를 반환해, 호출부가 필요하면 이를 바탕으로 추가 판단을 할 수 있게 함.
        /// <para>
        /// FindAnyObjectByType 대신 스코프 씬의 루트만 검사하는 이유:
        /// RegisterComponentInHierarchy는 '스코프가 속한 씬'에서만 컴포넌트를 찾으므로,
        /// 전역 검색으로 다른 씬(DontDestroyOnLoad 포함)의 객체를 발견해 등록하면
        /// 해석 시점에 발견 실패 예외가 나는 불일치가 생길 수 있음. 검사 범위를
        /// 등록 메커니즘과 동일하게 맞춰 이 불일치를 원천 차단함.
        /// </para>
        /// </summary>
        protected bool RegisterIfPresentInScene<T>(IContainerBuilder builder) where T : MonoBehaviour
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<T>(true) != null)
                {
                    builder.RegisterComponentInHierarchy<T>();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Settings.json 로드를 일원화하는 제공자를 등록함.
        /// 각 매니저가 개별 로드하던 구조를 대체하여 중복 I/O와 초기화 순서 비결정성을 제거함.
        /// </summary>
        protected virtual void ConfigureSettings(IContainerBuilder builder)
        {
            builder.Register<AppSettingsProvider>(Lifetime.Singleton);
        }
        
        /// <summary>
        /// ZLogger를 기반으로 전역 로깅 시스템을 설정함.
        /// 에디터에서는 콘솔, 빌드 환경에서는 파일 형태로 로그를 출력하도록 분기 처리함.
        /// </summary>
        protected virtual void ConfigureLogging(IContainerBuilder builder)
        {
            builder.Register<ILoggerFactory>(resolver =>
            {
                return LoggerFactory.Create(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);

                    logging.AddZLoggerUnityDebug(options =>
                    {
                        // ZLogger의 Unity 콘솔 프로바이더는 기본적으로 로그마다 스택을 직접 캡처해(new StackTrace)
                        // 메시지 문자열에 붙임(PrettyStacktrace). 문제는 이 "붙일지 여부"를 매 호출이 아니라
                        // RuntimeInitializeLoadType.SubsystemRegistration 시점에 GetStackTraceLogType 값을 한 번
                        // 캐싱해 결정한다는 점. SubsystemRegistration은 RuntimeInitialize 단계 중 가장 이르므로,
                        // LogStackTraceConfig(우리 정책)가 어떤 단계에서 실행돼도 그 캐싱을 앞지를 수 없어
                        // 일반 로그(Log→None) 설정이 ZLog* 콘솔 출력에는 반영되지 않는다.
                        // 그래서 ZLogger의 수동 스택을 아예 끄고, 스택 정책을 전적으로 Unity 네이티브
                        // Application.SetStackTraceLogType(LogStackTraceConfig에서 설정)에 위임한다.
                        // Unity는 이 값을 런타임에 매 로그마다 읽으므로 Info=None/Warning·Error=ScriptOnly가
                        // ZLogger가 내부적으로 호출하는 Debug.Log/LogWarning/LogError에도 그대로 적용된다.
                        // (ZLogger의 Post는 호출 스레드에서 동기 실행되고 [HideInCallstack]이 붙어 있어,
                        //  네이티브 ScriptOnly 스택도 ZLogger 내부 프레임을 숨긴 실제 호출부를 가리킨다.)
                        options.PrettyStacktrace = false;

                        options.UsePlainTextFormatter(formatter =>
                        {
                            formatter.SetPrefixFormatter($"{0:yyyy-MM-dd HH:mm:ss} | ", (in MessageTemplate template, in LogInfo info) =>
                            {
                                template.Format(DateTime.Now);
                            });
                        });
                    });

// WebGL에서는 파일 시스템 및 백그라운드 스레드 기반 파일 로깅이 불가능하므로 Unity 콘솔 로그만 사용함.
#if !UNITY_EDITOR && !UNITY_WEBGL
                    // 회전(Rolling) 파일로 출력함. 단일 GameLog.txt에 무한 append하던 기존 방식은
                    // 몇 주~몇 달 무중단으로 도는 키오스크에서 파일 하나가 계속 커져 디스크를 잠식했음.
                    // 날짜(일) 단위로 회전하고, 같은 날에도 용량 상한을 넘기면 시퀀스로 분할하여
                    // 개별 파일 크기를 제한함. 파일명: GameLog_yyyy-MM-dd_000.txt
                    // (오래된 파일 정리는 책임을 분리해 LogRetentionService가 담당함 - ConfigureLogRetention 참고)
                    logging.AddZLoggerRollingFile(options =>
                    {
                        // timestamp: 회전 시점, sequenceNo: 같은 구간 내 용량 분할 번호(0부터).
                        // 프리픽스 포맷(DateTime.Now, 로컬)과 일 경계를 맞추기 위해 LocalDateTime으로 명명함.
                        options.FilePathSelector = (timestamp, sequenceNo) =>
                            Path.Combine(LogDirectory, $"{LogRetentionService.LogFilePrefix}_{timestamp.LocalDateTime:yyyy-MM-dd}_{sequenceNo:000}.txt");
                        options.RollingInterval = RollingInterval.Day;
                        options.RollingSizeKB = LogRollingSizeKB;

                        options.UsePlainTextFormatter(formatter =>
                        {
                            formatter.SetPrefixFormatter($"{0:yyyy-MM-dd HH:mm:ss} | ", (in MessageTemplate template, in LogInfo info) =>
                            {
                                template.Format(DateTime.Now);
                            });
                        });
                    });
#endif
                });
            }, Lifetime.Singleton);

            builder.Register(typeof(ILogger<>), typeof(Logger<>), Lifetime.Singleton);
        }

        /// <summary>
        /// 빌드 환경의 파일 로그가 위치하는 디렉터리. ConfigureLogging(회전 설정)과
        /// ConfigureLogRetention(정리 대상 지정)이 동일 경로를 참조하도록 단일 소스로 둠.
        /// </summary>
        protected static string LogDirectory => Path.Combine(Application.persistentDataPath, "Logs");

        /// <summary>개별 로그 파일 용량 상한(KB). 초과 시 같은 날짜 안에서 시퀀스로 분할됨.</summary>
        protected virtual int LogRollingSizeKB => 10 * 1024; // 10 MB

        /// <summary>로그 파일 보관 기간(일). 이보다 오래된 GameLog 파일은 정리 대상.</summary>
        protected virtual int LogRetentionDays => 30;

        /// <summary>
        /// 오래된 로그 파일을 주기적으로 정리하는 LogRetentionService를 VContainer 엔트리포인트로
        /// 등록함. 로깅 프로바이더 구성(ConfigureLogging)과 파일 정리 책임을 분리하여, 정리 정책
        /// (주기·보관일)을 로깅 설정과 독립적으로 바꿀 수 있고 순수 로직을 단위 테스트할 수 있게 함.
        /// 에디터/WebGL은 애초에 파일 로깅을 하지 않으므로 등록 자체를 생략함.
        /// </summary>
        protected virtual void ConfigureLogRetention(IContainerBuilder builder)
        {
#if !UNITY_EDITOR && !UNITY_WEBGL
            builder.RegisterEntryPoint<LogRetentionService>(
                resolver => new LogRetentionService(LogDirectory, LogRetentionDays, resolver.Resolve<ILogger<LogRetentionService>>()),
                Lifetime.Singleton);
#endif
        }

        /// <summary>
        /// MessagePipe를 기반으로 전역 이벤트 시스템을 설정함.
        /// 인스펙터 토글 이벤트 등의 브로커를 등록함.
        /// </summary>
        protected virtual void ConfigureMessagePipe(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<InspectorEvent>(options);
            builder.RegisterMessageBroker<InactivityTimeoutEvent>(options);
            builder.RegisterMessageBroker<BeforeShutdownEvent>(options);
            builder.RegisterMessageBroker<MoveIdleEvent>(options);
            builder.RegisterMessageBroker<ExternalApiCallEvent>(options);
            builder.RegisterMessageBroker<ExternalApiReturnEvent>(options);
        }
    }
}