using System;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;
using HuliacDev.Data;
using ZLogger;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace HuliacDev.Core
{
    /// <summary>
    /// 앱 창이 포커스를 잃으면 설정한 시간 뒤 창을 다시 앞으로 가져오는 서비스(Windows 스탠드얼론 빌드 전용).
    /// <para>
    /// Windows는 키보드 입력을 포커스가 있는 창에만 보내므로, 키보드처럼 입력하는 USB 바코드·QR 스캐너는
    /// 알림·업데이트 창·다른 프로그램이 포커스를 가져가면 누가 화면을 터치할 때까지 앱에 입력되지 않음.
    /// Input System의 Background Behavior로는 해결되지 않음(키보드 장치는 백그라운드 입력을 받지 못함).
    /// </para>
    /// <para>
    /// 앱 시작 시 켜져 있고, 운영자가 유지보수 중 다른 프로그램을 쓸 수 있도록 TemplateInputActions의
    /// System/ToggleFocusRestore(기본 F 키)로 끄고 켬. 토글하면 화면 위쪽에 바뀐 상태를 잠깐 보여 줌.
    /// Settings.json에서는 대기 시간·재시도 간격과 항상 위 사용 여부(alwaysOnTop)를 정함.
    /// 앱 종료 중에는 시도하지 않으며, 에디터와 다른 플랫폼에서는 아무것도 하지 않음.
    /// 언제 시도할지는 FocusRestoreSchedule이 정하고, 이 클래스는 포커스 이벤트·키 입력 연결과 user32 호출만 담당함.
    /// </para>
    /// <para>
    /// alwaysOnTop이 true면 앱 창을 항상 위(Topmost)로 두어, 나중에 뜬 다른 프로그램 창이 화면을 가리지 않게 함.
    /// Display.Activate로 연 보조 디스플레이 창도 같은 창 클래스라 함께 적용하며, 나중에 열린 창이나 풀린 상태를 잡도록
    /// 주기적으로 다시 확인함. ToggleFocusRestore 키로 끄면 항상 위도 함께 풀어, 점검할 때 작업 관리자·다른 프로그램을 볼 수 있게 함.
    /// </para>
    /// </summary>
    public sealed class WindowFocusRestorer : IStartable, ITickable, IDisposable
    {
        /// <summary>항상 위가 켜져 있을 때, 이 앱의 창이 모두 항상 위인지 다시 확인하는 간격(초).</summary>
        private const double TopmostCheckIntervalSeconds = 1d;

        private readonly AppSettingsProvider _settingsProvider;
        private readonly ILogger<WindowFocusRestorer> _logger;
        private readonly TemplateInputActions _inputActions;

        private CancellationTokenSource _cts;
        private bool _isActive;
        private bool _isRestoreOn = true;
        private bool _useAlwaysOnTop;
        private StatusMessageOverlay _statusOverlay;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private FocusRestoreSchedule _schedule;
        private IntPtr _windowHandle;
        private double _nextTopmostCheckTime;
#endif

        /// <summary>
        /// 설정 제공자와 로거를 주입받고, 토글 키에 쓸 TemplateInputActions를 조회함.
        /// TemplateInputActions는 ConfigureCoreComponents를 override한 프로젝트에서 빠질 수 있어
        /// 필수 의존성 대신 ResolveOrDefault로 조회함(없으면 토글 키 없이 켜진 채로 동작).
        /// </summary>
        public WindowFocusRestorer(AppSettingsProvider settingsProvider, ILogger<WindowFocusRestorer> logger,
            IObjectResolver resolver)
        {
            _settingsProvider = settingsProvider;
            _logger = logger;
            _inputActions = resolver.ResolveOrDefault<TemplateInputActions>();
        }

        /// <summary>
        /// VContainer 컨테이너 빌드 완료 시 자동 호출됨. 토글 키를 연결하고 앱 종료와 연결된 토큰으로 설정 로드를 시작함.
        /// </summary>
        void IStartable.Start()
        {
            if (_inputActions != null)
            {
                _inputActions.System.ToggleFocusRestore.performed += OnToggleFocusRestore;
                // GameManagerBase가 입력 액션 전체를 켜기 전이거나 GameManagerBase가 없는 씬에서도 키가 동작하도록 이 액션만 켬.
                _inputActions.System.ToggleFocusRestore.Enable();
            }
            else if (_logger != null)
            {
                _logger.ZLogWarning($"[WindowFocusRestorer] TemplateInputActions is not registered, so the ToggleFocusRestore key is unavailable. Focus restore stays on.");
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(Application.exitCancellationToken);
            LoadSettingsAsync(_cts.Token).Forget();
        }

        /// <summary>
        /// Settings.json에서 대기 시간·재시도 간격과 항상 위 사용 여부를 읽어, Windows 스탠드얼론 빌드에서만 감시를 시작함.
        /// Settings.json을 읽지 못하면 AppSettingsProvider가 대체 설정을 돌려주므로 기본 타이밍으로, 항상 위 없이 동작함.
        /// </summary>
        private async UniTaskVoid LoadSettingsAsync(CancellationToken cancellationToken)
        {
            Settings settings;
            try
            {
                settings = await _settingsProvider.GetAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 컨테이너 파기나 앱 종료로 정상 취소됨
                return;
            }

            // 토글 키의 상태 표시·로그가 항상 위도 함께 끄고 켠다고 알리도록 플랫폼과 상관없이 기억함(적용은 Windows 스탠드얼론 빌드만 함).
            _useAlwaysOnTop = settings != null && settings.alwaysOnTop;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Activate(settings ?? new Settings());
#else
            if (_logger != null) _logger.ZLogInformation($"[WindowFocusRestorer] Focus restore only runs in Windows standalone builds. Skipped on this platform.");
#endif
        }

        /// <summary>
        /// 운영자 키 입력으로 포커스 복구(항상 위를 켰으면 항상 위도 함께)를 끄거나 켜고, 바뀐 상태를 화면에 잠깐 보여 줌.
        /// </summary>
        private void OnToggleFocusRestore(InputAction.CallbackContext _)
        {
            _isRestoreOn = !_isRestoreOn;

            if (_logger != null)
            {
                _logger.ZLogInformation($"[WindowFocusRestorer] Focus restore{(_useAlwaysOnTop ? " and always on top" : "")} turned {(_isRestoreOn ? "on" : "off")} by the ToggleFocusRestore key.");
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // 끌 때는 그 자리에서 풀어 다른 프로그램 창이 앱 위로 올라올 수 있게 하고, 켤 때는 다음 Tick에서 바로 다시 적용함.
            if (_isActive && _useAlwaysOnTop)
            {
                if (_isRestoreOn)
                {
                    _nextTopmostCheckTime = 0d;
                }
                else
                {
                    SetAlwaysOnTop(false);
                }
            }
#endif

            ShowToggleStatus();
        }

        /// <summary>
        /// 포커스 복구(항상 위를 켰으면 항상 위도 함께)의 켜짐·꺼짐을 화면 위쪽에 잠깐 표시함. 상태가 로그에만 남으면
        /// 유지보수 중 꺼 둔 채 잊기 쉬워, 끌 때는 다시 켜는 키도 함께 보여 줌. 키 이름은 ConfigureInputBindings로 바꾼 바인딩을 그대로 따름.
        /// </summary>
        private void ShowToggleStatus()
        {
            if (!_statusOverlay)
            {
                _statusOverlay = StatusMessageOverlay.Create("FocusRestoreStatusOverlay");
            }

            string featureName = _useAlwaysOnTop ? "Focus restore and always on top" : "Focus restore";

            if (_isRestoreOn)
            {
                _statusOverlay.Show(ZString.Concat(featureName, " ON"));
            }
            else
            {
                string keyName = _inputActions.System.ToggleFocusRestore.GetBindingDisplayString();
                string pronoun = _useAlwaysOnTop ? "them" : "it";
                _statusOverlay.Show(ZString.Concat(featureName, " OFF - press ", keyName, " to turn ", pronoun, " back on"));
            }
        }

        /// <summary>
        /// 다음 시도 시각이 되면 창을 앞으로 가져오도록 시도하고, 항상 위를 켰으면 주기적으로 다시 적용함.
        /// 키로 꺼져 있거나 앱 종료가 시작됐으면 둘 다 하지 않음.
        /// </summary>
        void ITickable.Tick()
        {
            if (!_isActive || !_isRestoreOn || _cts == null || _cts.IsCancellationRequested) return;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            double now = Time.realtimeSinceStartupAsDouble;

            if (_schedule.TryBeginAttempt(now))
            {
                TryRestoreFocus();
            }

            // Display.Activate로 나중에 열린 보조 디스플레이 창과, 어떤 이유로든 풀린 항상 위(보조 창을 열면 메인 창도 풀림)를
            // 다시 잡도록 주기적으로 확인함.
            if (_useAlwaysOnTop && now >= _nextTopmostCheckTime)
            {
                _nextTopmostCheckTime = now + TopmostCheckIntervalSeconds;
                SetAlwaysOnTop(true);
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>
        /// 설정 값으로 시도 일정을 만들고 포커스 변경 이벤트 구독을 시작함.
        /// 시작 시점에 이미 포커스가 없으면(예: 부팅 직후 다른 창이 앞에 있음) 곧바로 대기를 시작함.
        /// </summary>
        private void Activate(Settings settings)
        {
            _schedule = new FocusRestoreSchedule(settings.focusRestoreDelay, settings.focusRestoreRetryInterval);

            if (!Application.runInBackground && _logger != null)
            {
                _logger.ZLogWarning($"[WindowFocusRestorer] Run In Background is off. The player pauses while unfocused, so focus restore cannot run until focus returns. Turn on Player Settings > Run In Background.");
            }

            if (Application.isFocused)
            {
                RememberActiveWindow();
            }
            else
            {
                _schedule.OnFocusLost(Time.realtimeSinceStartupAsDouble);
            }

            Application.focusChanged += OnFocusChanged;
            _isActive = true;

            if (_logger != null)
            {
                _logger.ZLogInformation($"[WindowFocusRestorer] Started ({(_isRestoreOn ? "on" : "off")}). Brings the window back {_schedule.DelaySeconds}s after focus is lost and retries every {_schedule.RetryIntervalSeconds}s. Always on top is {(_useAlwaysOnTop ? "enabled" : "disabled")} (alwaysOnTop).");

                // 토글 키가 없으면 점검할 때 항상 위를 끌 방법이 없어 작업 관리자·오류 창이 계속 가려지므로 미리 알림.
                if (_useAlwaysOnTop && _inputActions == null)
                {
                    _logger.ZLogWarning($"[WindowFocusRestorer] Always on top is enabled but the ToggleFocusRestore key is unavailable, so it cannot be turned off for maintenance. Close the app with Alt+F4 or the hidden close button instead.");
                }
            }
        }

        /// <summary>
        /// 포커스를 잃으면 대기를 시작하고, 돌아오면 시도를 멈추고 창 핸들을 다시 기억함.
        /// </summary>
        private void OnFocusChanged(bool hasFocus)
        {
            if (hasFocus)
            {
                RememberActiveWindow();

                if (_schedule.OnFocusGained() && _schedule.AttemptCount > 0 && _logger != null)
                {
                    _logger.ZLogInformation($"[WindowFocusRestorer] Focus returned after {_schedule.AttemptCount} restore attempt(s).");
                }
                return;
            }

            if (_schedule.IsWaitingForFocus) return;

            _schedule.OnFocusLost(Time.realtimeSinceStartupAsDouble);

            if (_logger == null) return;

            // 키로 꺼 둔 상태에서는 Tick이 시도하지 않으므로, 되돌아온다는 로그를 남기면 로그만 보고 원인을 찾을 수 없음.
            if (_isRestoreOn)
            {
                _logger.ZLogInformation($"[WindowFocusRestorer] Focus lost. Will bring the window back in {_schedule.DelaySeconds}s.");
            }
            else
            {
                _logger.ZLogInformation($"[WindowFocusRestorer] Focus lost. Focus restore is off (ToggleFocusRestore key), so the window will not be brought back.");
            }
        }

        /// <summary>
        /// 앱 창을 앞으로 가져오도록 한 번 시도하고, 이번 포커스 이탈에서 처음 실패했을 때만 경고를 남김.
        /// </summary>
        private void TryRestoreFocus()
        {
            IntPtr windowHandle = ResolveOwnWindow();
            bool succeeded = windowHandle != IntPtr.Zero && NativeMethods.BringToForeground(windowHandle);

            if (succeeded || !_schedule.RecordFailure() || _logger == null) return;

            if (windowHandle == IntPtr.Zero)
            {
                _logger.ZLogWarning($"[WindowFocusRestorer] Could not find this app's window. Will keep retrying every {_schedule.RetryIntervalSeconds}s.");
            }
            else
            {
                _logger.ZLogWarning($"[WindowFocusRestorer] Windows did not bring the window to the foreground (attempt {_schedule.AttemptCount}). The foreground lock, the lock screen, or an elevated window in front can block it. Will keep retrying every {_schedule.RetryIntervalSeconds}s.");
            }
        }

        /// <summary>
        /// 이 앱의 창을 모두 항상 위로 두거나 풀고, 상태가 바뀐 창이 있을 때만 로그를 남김.
        /// 이미 항상 위인 창은 건드리지 않으므로, 나중에 항상 위로 뜬 다른 창(화상 키보드 등)과 순서를 다투지 않음.
        /// </summary>
        private void SetAlwaysOnTop(bool isOnTop)
        {
            int changedCount = NativeMethods.SetOwnWindowsTopmost(isOnTop);
            if (changedCount == 0 || _logger == null) return;

            _logger.ZLogInformation($"[WindowFocusRestorer] Always on top {(isOnTop ? "applied to" : "removed from")} {changedCount} window(s).");
        }

        /// <summary>
        /// 포커스가 있을 때 현재 스레드의 활성 창(이 앱의 창)을 기억함.
        /// </summary>
        private void RememberActiveWindow()
        {
            IntPtr activeWindow = NativeMethods.GetActiveWindow();
            if (activeWindow != IntPtr.Zero)
            {
                _windowHandle = activeWindow;
            }
        }

        /// <summary>
        /// 기억한 창 핸들이 유효하면 그대로 쓰고, 없거나 무효하면 이 프로세스의 Unity 창을 찾아 기억함.
        /// 앱이 한 번도 포커스를 얻지 못한 채 시작된 경우를 위한 대체 경로임.
        /// </summary>
        private IntPtr ResolveOwnWindow()
        {
            if (_windowHandle != IntPtr.Zero && NativeMethods.IsWindow(_windowHandle))
            {
                return _windowHandle;
            }

            _windowHandle = NativeMethods.FindOwnUnityWindow();
            return _windowHandle;
        }

        /// <summary>
        /// 창을 앞으로 가져오고 항상 위로 두는 데 쓰는 user32·kernel32 호출 모음.
        /// </summary>
        private static class NativeMethods
        {
            /// <summary>Unity 스탠드얼론 플레이어 창의 클래스 이름. Display.Activate로 연 보조 디스플레이 창도 같은 클래스를 씀.</summary>
            private const string UnityWindowClassName = "UnityWndClass";

            private const int SwRestore = 9;

            /// <summary>Unity가 지원하는 최대 디스플레이 수. 이 프로세스의 Unity 창은 이보다 많지 않음.</summary>
            private const int MaxOwnWindowCount = 8;

            /// <summary>CollectOwnUnityWindows가 호출마다 배열을 만들지 않도록 재사용하는 버퍼(메인 스레드에서만 씀).</summary>
            private static readonly IntPtr[] OwnWindowBuffer = new IntPtr[MaxOwnWindowCount];

            private const int GwlExStyle = -20;
            private const int WsExTopmost = 0x00000008;
            private const uint SwpNoSize = 0x0001;
            private const uint SwpNoMove = 0x0002;
            private const uint SwpNoActivate = 0x0010;
            private static readonly IntPtr HwndTopmost = new IntPtr(-1);
            private static readonly IntPtr HwndNoTopmost = new IntPtr(-2);

            [DllImport("user32.dll")]
            public static extern IntPtr GetActiveWindow();

            [DllImport("user32.dll")]
            public static extern bool IsWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            private static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern bool BringWindowToTop(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            private static extern bool IsIconic(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

            [DllImport("user32.dll")]
            private static extern bool IsHungAppWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

            [DllImport("user32.dll")]
            private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

            // 확장 스타일(32비트 값)만 읽으므로 64비트에서도 GetWindowLongPtr가 필요 없음. A/W 접미사 탐색에 기대지 않도록 이름을 직접 지정함.
            [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
            private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentThreadId();

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentProcessId();

            /// <summary>
            /// 창을 포그라운드로 가져오고 성공 여부를 돌려줌.
            /// 포그라운드 잠금 때문에 SetForegroundWindow만으로는 거부되는 경우가 많아, 현재 포그라운드 창의
            /// 스레드에 입력 상태를 잠시 붙인 채 호출하고 바로 분리함. 단 포그라운드 창이 응답 없음(멈춤) 상태면 붙이지 않음.
            /// 최소화된 경우에만 SW_RESTORE를 써서, 최소화되지 않은 창의 크기·상태는 건드리지 않음.
            /// </summary>
            public static bool BringToForeground(IntPtr windowHandle)
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == windowHandle) return true;

                uint currentThreadId = GetCurrentThreadId();
                uint foregroundThreadId = foregroundWindow != IntPtr.Zero
                    ? GetWindowThreadProcessId(foregroundWindow, out uint _)
                    : 0;

                // 입력을 붙이면 두 스레드가 입력 상태를 공유해, 상대 스레드가 멈춰 있으면 이 앱의 메인 스레드도 입력 처리에서
                // 함께 멈출 수 있음. 응답 없는 창이면 붙이지 않고 시도만 하며, 거부되면 다음 재시도 때 다시 확인함.
                bool canAttach = foregroundThreadId != 0 && foregroundThreadId != currentThreadId
                    && !IsHungAppWindow(foregroundWindow);
                bool isAttached = canAttach && AttachThreadInput(currentThreadId, foregroundThreadId, true);

                try
                {
                    if (IsIconic(windowHandle))
                    {
                        ShowWindow(windowHandle, SwRestore);
                    }

                    BringWindowToTop(windowHandle);
                    SetForegroundWindow(windowHandle);
                }
                finally
                {
                    if (isAttached)
                    {
                        AttachThreadInput(currentThreadId, foregroundThreadId, false);
                    }
                }

                return GetForegroundWindow() == windowHandle;
            }

            /// <summary>
            /// 이 프로세스가 만든 Unity 플레이어 창을 찾음. 같은 PC에서 도는 다른 Unity 앱의 창은 프로세스 ID로 걸러냄.
            /// </summary>
            public static IntPtr FindOwnUnityWindow()
            {
                return CollectOwnUnityWindows() > 0 ? OwnWindowBuffer[0] : IntPtr.Zero;
            }

            /// <summary>
            /// 이 프로세스의 Unity 창(보조 디스플레이 창 포함)을 모두 찾아, 항상 위 상태가 원하는 값과 다른 창만 바꾸고
            /// 바꾼 창 수를 돌려줌. 위치·크기·활성 상태는 건드리지 않음.
            /// </summary>
            public static int SetOwnWindowsTopmost(bool isTopmost)
            {
                IntPtr insertAfter = isTopmost ? HwndTopmost : HwndNoTopmost;
                int windowCount = CollectOwnUnityWindows();
                int changedCount = 0;

                for (int i = 0; i < windowCount; i++)
                {
                    IntPtr window = OwnWindowBuffer[i];
                    bool isCurrentlyTopmost = (GetWindowLong(window, GwlExStyle) & WsExTopmost) != 0;
                    if (isCurrentlyTopmost == isTopmost) continue;

                    if (SetWindowPos(window, insertAfter, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate))
                    {
                        changedCount++;
                    }
                }

                return changedCount;
            }

            /// <summary>
            /// 이 프로세스의 Unity 창을 위에서부터 z-order 순서로 OwnWindowBuffer에 모으고 개수를 돌려줌.
            /// 같은 PC에서 도는 다른 Unity 앱의 창은 프로세스 ID로 걸러냄. FindWindowEx는 넘긴 창 다음 z-order부터 찾으므로,
            /// 찾는 도중 창을 옮기면 이미 지난 위치로 간 다른 창을 건너뛰게 됨. 그래서 창을 바꾸는 쪽은 먼저 목록을 다 모음.
            /// </summary>
            private static int CollectOwnUnityWindows()
            {
                uint currentProcessId = GetCurrentProcessId();
                IntPtr candidate = IntPtr.Zero;
                int count = 0;

                while (count < OwnWindowBuffer.Length
                    && (candidate = FindWindowEx(IntPtr.Zero, candidate, UnityWindowClassName, null)) != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(candidate, out uint processId);
                    if (processId == currentProcessId)
                    {
                        OwnWindowBuffer[count++] = candidate;
                    }
                }

                return count;
            }
        }
#endif

        /// <summary>
        /// 컨테이너 파기 시 VContainer가 자동 호출함. 포커스 이벤트·키 입력 구독과 설정 로드, 상태 표시 오버레이를 정리하고,
        /// 이 서비스가 켠 항상 위를 풂.
        /// </summary>
        void IDisposable.Dispose()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Application.focusChanged -= OnFocusChanged;

            // 앱은 계속 도는데 컨테이너만 파기되면 끄는 키도 함께 사라지므로, 항상 위가 남지 않게 풂.
            if (_isActive && _useAlwaysOnTop)
            {
                NativeMethods.SetOwnWindowsTopmost(false);
            }
#endif
            if (_inputActions != null)
            {
                _inputActions.System.ToggleFocusRestore.performed -= OnToggleFocusRestore;
            }

            // 오버레이는 씬을 넘어 유지되도록 만들었으므로, 이 서비스가 사라질 때 함께 파괴해야 남지 않음.
            if (_statusOverlay)
            {
                UnityEngine.Object.Destroy(_statusOverlay.gameObject);
                _statusOverlay = null;
            }

            _isActive = false;

            if (_cts == null) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }
}
