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

        /// <summary>
        /// TimestampLogHandler 생성자입니다.
        /// 핸들러의 이중 래핑(Decorator Leak)을 방지합니다.
        /// </summary>
        /// <param name="defaultLogHandler">래핑할 원본 로그 핸들러</param>
        public TimestampLogHandler(ILogHandler defaultLogHandler)
        {
            if (defaultLogHandler == null)
            {
                Debug.LogError("[TimestampLogHandler] defaultLogHandler가 null입니다.");
                return;
            }

            // 기존 핸들러가 이미 TimestampLogHandler일 경우 원본 핸들러를 추출하여 중복 타임스탬프 방지
            if (defaultLogHandler is TimestampLogHandler existingHandler)
            {
                _defaultLogHandler = existingHandler._defaultLogHandler;
            }
            else
            {
                _defaultLogHandler = defaultLogHandler;
            }
        }

        /// <summary>
        /// 현재 시간의 타임스탬프 문자열을 반환합니다.
        /// 1초 단위로만 문자열을 포맷팅하여 메모리 가비지 할당을 극소화합니다.
        /// </summary>
        /// <returns>포맷팅된 타임스탬프 문자열 (예: 2026-03-27 10:30:00)</returns>
        private string GetOptimizedTimestamp()
        {
            long currentSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;

            if (currentSecond != _lastSecondTicks)
            {
                _lastSecondTicks = currentSecond;
                _cachedTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }

            return _cachedTimestamp;
        }

        /// <summary>
        /// 일반 로그 메시지를 포맷팅하여 출력합니다.
        /// </summary>
        /// <param name="logType">로그의 심각도 타입</param>
        /// <param name="context">로그를 발생시킨 객체 컨텍스트</param>
        /// <param name="format">출력할 로그 메시지 형식</param>
        /// <param name="args">포맷에 들어갈 인자 배열</param>
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (_defaultLogHandler == null) return;
            
            _defaultLogHandler.LogFormat(logType, context, $"[{GetOptimizedTimestamp()}] {format}", args);
        }

        /// <summary>
        /// 예외 로그 메시지를 포맷팅하여 출력합니다.
        /// </summary>
        /// <param name="exception">발생한 예외 객체</param>
        /// <param name="context">로그를 발생시킨 객체 컨텍스트</param>
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (_defaultLogHandler == null) return;

            _defaultLogHandler.LogFormat(LogType.Exception, context, $"[{GetOptimizedTimestamp()}] Exception Occurred:", Array.Empty<object>());
            _defaultLogHandler.LogException(exception, context);
        }

        /// <summary>
        /// 외부에서 안전하게 로그 핸들러를 시스템에 등록하기 위한 헬퍼 함수입니다.
        /// 이중 등록을 완전히 차단하기 위해 사용합니다.
        /// </summary>
        public static void Attach()
        {
            if (Debug.unityLogger.logHandler is TimestampLogHandler) return;
            Debug.unityLogger.logHandler = new TimestampLogHandler(Debug.unityLogger.logHandler);
        }
    }
}