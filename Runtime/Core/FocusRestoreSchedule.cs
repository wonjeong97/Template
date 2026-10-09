using UnityEngine;

namespace HuliacDev.Core
{
    /// <summary>
    /// 창 포커스 복구를 언제 시도할지 정하는 순수 계산 클래스.
    /// 포커스를 잃으면 대기 시간 뒤 첫 시도를 하고, 포커스가 돌아올 때까지 재시도 간격마다 다시 시도함.
    /// 현재 시각을 호출부가 넘기므로 Unity 시간이나 user32 호출 없이 단위 테스트할 수 있음
    /// (WindowFocusRestorer는 실제 창 조작만 담당함).
    /// </summary>
    internal sealed class FocusRestoreSchedule
    {
        /// <summary>focusRestoreDelay가 미지정(0 이하)일 때 쓰는 대기 시간(초).</summary>
        public const float DefaultDelaySeconds = 3f;

        /// <summary>focusRestoreRetryInterval이 미지정(0 이하)일 때 쓰는 재시도 간격(초).</summary>
        public const float DefaultRetryIntervalSeconds = 3f;

        /// <summary>대기 시간과 재시도 간격의 최소값(초). 너무 짧으면 운영자가 다른 창을 잠깐 열어 볼 틈도 없이 창을 빼앗김.</summary>
        public const float MinIntervalSeconds = 1f;

        private bool _isWaitingForFocus;
        private double _nextAttemptTime;
        private int _attemptCount;
        private bool _hasReportedFailure;

        /// <summary>실제로 적용한 대기 시간(초).</summary>
        public float DelaySeconds { get; }

        /// <summary>실제로 적용한 재시도 간격(초).</summary>
        public float RetryIntervalSeconds { get; }

        /// <summary>포커스를 잃고 아직 돌아오지 않은 상태인지.</summary>
        public bool IsWaitingForFocus => _isWaitingForFocus;

        /// <summary>마지막으로 포커스를 잃은 뒤 시작한 복구 시도 횟수.</summary>
        public int AttemptCount => _attemptCount;

        /// <summary>
        /// 설정 값으로 대기 시간과 재시도 간격을 정해 구성함.
        /// </summary>
        public FocusRestoreSchedule(float configuredDelaySeconds, float configuredRetryIntervalSeconds)
        {
            DelaySeconds = ResolveSeconds(configuredDelaySeconds, DefaultDelaySeconds);
            RetryIntervalSeconds = ResolveSeconds(configuredRetryIntervalSeconds, DefaultRetryIntervalSeconds);
        }

        /// <summary>
        /// 설정 값이 0 이하(미지정)면 기본값을, 최소값보다 작은 양수면 최소값을 돌려줌.
        /// </summary>
        public static float ResolveSeconds(float configuredSeconds, float defaultSeconds)
        {
            if (configuredSeconds <= 0f) return defaultSeconds;
            return Mathf.Max(MinIntervalSeconds, configuredSeconds);
        }

        /// <summary>
        /// 포커스를 잃었음을 기록하고 첫 시도 시각을 정함. 이미 기다리는 중이면 첫 시도 시각을 미루지 않음.
        /// </summary>
        public void OnFocusLost(double now)
        {
            if (_isWaitingForFocus) return;

            _isWaitingForFocus = true;
            _nextAttemptTime = now + DelaySeconds;
            _attemptCount = 0;
            _hasReportedFailure = false;
        }

        /// <summary>
        /// 포커스가 돌아왔음을 기록하고 시도를 멈춤. 포커스를 기다리던 중이었으면 true를 돌려줌.
        /// </summary>
        public bool OnFocusGained()
        {
            bool wasWaiting = _isWaitingForFocus;
            _isWaitingForFocus = false;
            return wasWaiting;
        }

        /// <summary>
        /// 지금 복구를 시도할 차례면 true를 돌려주고 다음 시도 시각을 재시도 간격 뒤로 예약함.
        /// </summary>
        public bool TryBeginAttempt(double now)
        {
            if (!_isWaitingForFocus || now < _nextAttemptTime) return false;

            _attemptCount++;
            _nextAttemptTime = now + RetryIntervalSeconds;
            return true;
        }

        /// <summary>
        /// 시도 실패를 기록함. 이번 포커스 이탈에서 처음 실패한 것이면 true를 돌려줘, 경고를 시도마다가 아니라 한 번만 남기게 함.
        /// </summary>
        public bool RecordFailure()
        {
            if (_hasReportedFailure) return false;

            _hasReportedFailure = true;
            return true;
        }
    }
}
