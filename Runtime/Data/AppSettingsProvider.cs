using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Wonjeong.Utils;

namespace Wonjeong.Data
{
    /// <summary>
    /// Settings.json을 단 한 번만 로드하여 모든 소비자에게 공유하는 싱글톤 제공자.
    /// 각 매니저가 개별적으로 로드하면 WebGL에서 동일 파일에 대한 HTTP 요청이 중복 발생하고
    /// 로드 완료 시점이 서로 달라 초기화 순서가 비결정적이 되므로, 로드를 이곳으로 일원화함.
    /// VContainer에 Lifetime.Singleton으로 등록하여 사용함.
    /// </summary>
    public class AppSettingsProvider : IDisposable
    {
        private const string SettingsFileName = "Settings.json";

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private UniTask<Settings> _loadTask;
        private bool _isLoadStarted;

        /// <summary>
        /// 설정을 비동기로 반환함. 최초 호출 시에만 실제 로드가 발생하고
        /// 이후 호출은 동일한 결과를 공유함.
        /// </summary>
        /// <param name="cancellationToken">
        /// 호출자 고유의 취소 토큰. 이 토큰은 '대기(await)'만 취소하며,
        /// 공유 중인 로드 작업 자체는 취소하지 않으므로 다른 소비자에게 영향을 주지 않음.
        /// </param>
        public UniTask<Settings> GetAsync(CancellationToken cancellationToken = default)
        {
            if (!_isLoadStarted)
            {
                _isLoadStarted = true;

                // UniTask는 기본적으로 1회만 await 가능하므로, 다중 소비자 공유를 위해 Preserve() 처리함.
                _loadTask = JsonLoader.LoadAsync<Settings>(SettingsFileName, _cts.Token).Preserve();
            }

            return cancellationToken.CanBeCanceled
                ? _loadTask.AttachExternalCancellation(cancellationToken)
                : _loadTask;
        }

        /// <summary>
        /// 컨테이너 파기 시 진행 중인 로드를 취소하고 리소스를 해제함.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
