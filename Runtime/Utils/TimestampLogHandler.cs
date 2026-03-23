using System;
using UnityEngine;

namespace Wonjeong.Utils
{
    /// <summary>
    /// Unity 기본 로그 시스템을 가로채어 타임스탬프를 부여하는 래퍼 클래스입니다.
    /// 운영 환경에서 에러 및 이벤트 발생 시점을 정확히 추적하기 위해 사용합니다.
    /// </summary>
    public class TimestampLogHandler : ILogHandler
    {
        private ILogHandler _defaultLogHandler;

        private long _lastSecondTicks = -1;
        private int _lastMillisecond = -1;
        private string _cachedTimePrefix;
        private string _cachedTimestamp;

        /// <summary>
        /// 생성자.
        /// 기존 로그 핸들러를 주입받아 보관하기 위함.
        /// </summary>
        /// <param name="defaultLogHandler">Unity의 기본 로그 핸들러</param>
        public TimestampLogHandler(ILogHandler defaultLogHandler)
        {
            if (defaultLogHandler == null)
            {
                Debug.LogError("[TimestampLogHandler] defaultLogHandler가 null입니다.");
            }
            _defaultLogHandler = defaultLogHandler;
        }

        /// <summary>
        /// 최적화된 타임스탬프 문자열을 반환합니다.
        /// 무거운 Date 포맷팅 연산을 최소화하고, 동일한 밀리초 내의 다중 로그 발생 시 기존 문자열을 재사용하여 GC(Garbage Collection) 부하를 방지하기 위함.
        /// </summary>
        private string GetOptimizedTimestamp()
        {
            DateTime now = DateTime.Now;
            long currentSecond = now.Ticks / TimeSpan.TicksPerSecond;

            if (currentSecond == _lastSecondTicks && now.Millisecond == _lastMillisecond)
            {
                return _cachedTimestamp;
            }

            if (currentSecond != _lastSecondTicks)
            {
                _lastSecondTicks = currentSecond;
                _cachedTimePrefix = now.ToString("yyyy-MM-dd HH:mm:ss.");
            }

            _lastMillisecond = now.Millisecond;
            _cachedTimestamp = _cachedTimePrefix + now.Millisecond.ToString("000");

            return _cachedTimestamp;
        }

        /// <summary>
        /// 일반 로그 출력 시 호출됩니다.
        /// 로그 원본 메시지 앞에 최적화된 타임스탬프를 덧붙이기 위함.
        /// </summary>
        /// <param name="logType">로그 수준</param>
        /// <param name="context">로그를 발생시킨 유니티 객체</param>
        /// <param name="format">로그 메시지 포맷</param>
        /// <param name="args">포맷 인자</param>
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (_defaultLogHandler == null) return;
            
            string timestamp = GetOptimizedTimestamp();
            _defaultLogHandler.LogFormat(logType, context, $"[{timestamp}] {format}", args);
        }

        /// <summary>
        /// 예외(Exception) 발생 시 호출됩니다.
        /// 예외 스택트레이스 기록 직전에 타임스탬프를 명시하기 위함.
        /// </summary>
        /// <param name="exception">발생한 예외 객체</param>
        /// <param name="context">예외를 발생시킨 유니티 객체</param>
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (_defaultLogHandler == null) return;

            string timestamp = GetOptimizedTimestamp();
            _defaultLogHandler.LogFormat(LogType.Exception, context, $"[{timestamp}] Exception Occurred:", Array.Empty<object>());
            _defaultLogHandler.LogException(exception, context);
        }
    }
}