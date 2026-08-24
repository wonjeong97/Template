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

        // _isLoadStarted 확인과 _loadTask 생성을 원자적으로 묶기 위한 잠금 객체.
        // 백그라운드 스레드에서 동시에 최초 호출이 들어오면(예: 여러 매니저가 서로 다른
        // 스레드에서 초기화를 시작하는 경우) 락 없이는 두 스레드가 모두 !_isLoadStarted를
        // 통과해 LoadAsync를 중복 시작할 수 있음. await는 lock 블록 안에서 쓸 수 없으므로
        // (CS1996) 잠금 범위는 동기적인 확인·대입 구간으로만 한정함.
        private readonly object _lock = new object();

        /// <summary>
        /// 설정을 비동기로 반환함. 최초 호출 시에만 실제 로드가 발생하고
        /// 이후 호출은 동일한 결과를 공유함. 여러 소비자가 같은 프레임(또는 다른 스레드)에서
        /// 동시 호출해도 안전함.
        /// </summary>
        /// <param name="cancellationToken">
        /// 호출자 고유의 취소 토큰. 이 토큰은 '대기(await)'만 취소하며,
        /// 공유 중인 로드 작업 자체는 취소하지 않으므로 다른 소비자에게 영향을 주지 않음.
        /// </param>
        public async UniTask<Settings> GetAsync(CancellationToken cancellationToken = default)
        {
            Task<Settings> loadTask;

            lock (_lock)
            {
                if (!_isLoadStarted)
                {
                    _isLoadStarted = true;
                    _loadTask = JsonLoader.LoadAsync<Settings>(SettingsFileName, _cts.Token).AsTask();
                }

                loadTask = _loadTask;
            }

            Settings settings = await loadTask.AsUniTask().AttachExternalCancellation(cancellationToken);

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
