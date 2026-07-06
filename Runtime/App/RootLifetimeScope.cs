using System;
using System.IO;
using MessagePipe;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using ZLogger;
using ZLogger.Unity;

namespace Wonjeong.App
{
    public struct InspectorEvent { }

    public class RootLifetimeScope : LifetimeScope
    {
        /// <summary>
        /// VContainer의 의존성 주입 컨테이너를 구성함.
        /// 로깅 및 메시지 파이프 시스템을 초기화함.
        /// </summary>
        protected override void Configure(IContainerBuilder builder)
        {
            ConfigureLogging(builder);
            ConfigureMessagePipe(builder);
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
                    string logFilePath = Path.Combine(Application.persistentDataPath, "Logs", "GameLog.txt");
                    
                    logging.AddZLoggerFile(logFilePath, options =>
                    {
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
        /// MessagePipe를 기반으로 전역 이벤트 시스템을 설정함.
        /// 인스펙터 토글 이벤트 등의 브로커를 등록함.
        /// </summary>
        protected virtual void ConfigureMessagePipe(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<InspectorEvent>(options);
        }
    }
}