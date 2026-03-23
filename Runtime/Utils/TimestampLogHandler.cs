using System;
using UnityEngine;

namespace Wonjeong.Utils
{
    /// <summary>
    /// Unity 기본 로그 시스템을 가로채어 타임스탬프를 부여하는 래퍼 클래스입니다.
    /// </summary>
    public class TimestampLogHandler : ILogHandler
    {
        private ILogHandler _defaultLogHandler;

        private long _lastSecondTicks = -1;
        private string _cachedTimestamp;

        public TimestampLogHandler(ILogHandler defaultLogHandler)
        {
            if (defaultLogHandler == null)
            {
                Debug.LogError("[TimestampLogHandler] defaultLogHandler가 null입니다.");
            }
            _defaultLogHandler = defaultLogHandler;
        }

        /// <summary>
        /// 1초 단위로만 문자열을 포맷팅하여 가비지(GC) 할당을 극단적으로 최소화합니다.
        /// </summary>
        private string GetOptimizedTimestamp()
        {
            long currentSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;

            // 초 단위가 갱신되었을 때만 새로운 문자열을 캐싱합니다.
            if (currentSecond != _lastSecondTicks)
            {
                _lastSecondTicks = currentSecond;
                _cachedTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }

            return _cachedTimestamp;
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (_defaultLogHandler == null) return;
            
            _defaultLogHandler.LogFormat(logType, context, $"[{GetOptimizedTimestamp()}] {format}", args);
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (_defaultLogHandler == null) return;

            _defaultLogHandler.LogFormat(LogType.Exception, context, $"[{GetOptimizedTimestamp()}] Exception Occurred:", Array.Empty<object>());
            _defaultLogHandler.LogException(exception, context);
        }
    }
}