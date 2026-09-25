using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Logging;
using HuliacDev.Data;
using HuliacDev.UI;
using ZLogger.Unity;

namespace HuliacDev.Tests
{
    /// <summary>
    /// DI와 Settings.json 없이 생성한 SoundManager를 테스트 가능한 상태로 만드는 공통 헬퍼.
    /// UIManagerTests와 SoundManagerTests가 함께 쓰도록 UIManagerTests에서 옮겨옴.
    /// </summary>
    internal static class SoundManagerTestHelper
    {
        private static readonly BindingFlags Nonpublic = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// 설정 파일 로드 없이 사운드 키와 클립 경로를 직접 등록함.
        /// </summary>
        public static void SetSoundSetting(SoundManager soundManager, string key, string clipPath)
        {
            FieldInfo field = typeof(SoundManager).GetField("_soundSettings", Nonpublic);
            Dictionary<string, SoundSetting> settings = (Dictionary<string, SoundSetting>)field.GetValue(soundManager);
            settings[key] = new SoundSetting { key = key, clipPath = clipPath, volume = 1f };
        }

        /// <summary>
        /// SoundManager의 로그가 실제로 콘솔에 출력되도록, DI 없이 생성된 인스턴스에
        /// ZLogger 기반 로거를 직접 주입함(RootLifetimeScope.ConfigureLogging의 최소 재현).
        /// </summary>
        public static void SetLogger(SoundManager soundManager)
        {
            ILoggerFactory factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddZLoggerUnityDebug();
            });

            ILogger<SoundManager> logger = factory.CreateLogger<SoundManager>();
            typeof(SoundManager).GetField("_logger", Nonpublic).SetValue(soundManager, logger);
        }
    }
}
