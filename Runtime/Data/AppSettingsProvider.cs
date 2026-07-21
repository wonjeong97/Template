using System;
using System.Threading;
using System.Threading.Tasks;
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

        // 공유 소스로 UniTask 대신 Task를 사용함.
        // UniTask는 Preserve()를 써도 완료 전에 여러 소비자가 동시에 await하면
        // continuation이 중복 등록되어 InvalidOperationException이 발생함
        // ("Already continuation registered"). Task는 다중 awaiter를 기본 지원함.
        private Task<Settings> _loadTask;
        private bool _isLoadStarted;

        /// <summary>
        /// 설정을 비동기로 반환함. 최초 호출 시에만 실제 로드가 발생하고
        /// 이후 호출은 동일한 결과를 공유함. 여러 소비자가 같은 프레임에 동시 호출해도 안전함.
        /// </summary>
        /// <param name="cancellationToken">
        /// 호출자 고유의 취소 토큰. 이 토큰은 '대기(await)'만 취소하며,
        /// 공유 중인 로드 작업 자체는 취소하지 않으므로 다른 소비자에게 영향을 주지 않음.
        /// </param>
        public async UniTask<Settings> GetAsync(CancellationToken cancellationToken = default)
        {
            if (!_isLoadStarted)
            {
                _isLoadStarted = true;
                _loadTask = JsonLoader.LoadAsync<Settings>(SettingsFileName, _cts.Token).AsTask();
            }

            Settings settings = await _loadTask.AsUniTask().AttachExternalCancellation(cancellationToken);

            // 메인 스레드 컨텍스트를 보장하여 호출자가 곧바로 Unity API를 사용할 수 있게 함.
            await UniTask.SwitchToMainThread(cancellationToken);

            return settings;
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
