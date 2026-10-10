namespace HuliacDev.Utils
{
    /// <summary>
    /// 첫 클릭부터 제한 시간 안에 목표 횟수만큼 눌렸는지 판정하는 연속 클릭 카운터.
    /// GameCloser의 숨은 종료 버튼처럼 "N초 안에 M번 누르면 동작"하는 운영자용 숨은 버튼(예: 설정 화면 진입)에 재사용함.
    /// 현재 시각을 호출부가 넘기므로(보통 Time.unscaledTime) Unity 시간 없이 단위 테스트할 수 있음.
    /// 목표 횟수와 제한 시간은 호출할 때마다 넘겨, 설정 파일로 값이 바뀌어도 카운터와 어긋나지 않게 함.
    /// </summary>
    public sealed class ConsecutiveClickCounter
    {
        private int _clickCount;
        private float _firstClickTime;
        private bool _isCompleted;

        /// <summary>현재 연속 클릭 수. 목표에 도달한 클릭 뒤에는 그 횟수를 유지하다가 다음 클릭에서 1부터 다시 셈.</summary>
        public int ClickCount => _clickCount;

        /// <summary>현재 연속 클릭의 첫 클릭 시각.</summary>
        public float FirstClickTime => _firstClickTime;

        /// <summary>
        /// 클릭 한 번을 기록하고, 이번 클릭으로 목표 횟수에 도달했으면 true를 반환함.
        /// 첫 클릭부터 timeWindow초가 지난 뒤의 클릭은 이전 클릭을 버리고 새 1회차로 셈(정확히 timeWindow초째 클릭은 인정함).
        /// true를 반환한 다음 클릭도 새 1회차로 셈. targetCount는 1 이상이어야 하며, 1 이하면 클릭할 때마다 true임.
        /// </summary>
        public bool RegisterClick(float time, int targetCount, float timeWindow)
        {
            if (_isCompleted || _clickCount == 0 || time - _firstClickTime > timeWindow)
            {
                _clickCount = 1;
                _firstClickTime = time;
                _isCompleted = false;
            }
            else
            {
                _clickCount++;
            }

            if (_clickCount < targetCount)
            {
                return false;
            }

            _isCompleted = true;
            return true;
        }
    }
}
