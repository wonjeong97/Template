using System;
using System.Threading;
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
    /// System/ToggleFocusRestore(기본 F 키)로 끄고 켬. Settings.json에서는 대기 시간과 재시도 간격만 조정함.
    /// 앱 종료 중에는 시도하지 않으며, 에디터와 다른 플랫폼에서는 아무것도 하지 않음.
    /// 언제 시도할지는 FocusRestoreSchedule이 정하고, 이 클래스는 포커스 이벤트·키 입력 연결과 user32 호출만 담당함.
    /// </para>
    /// </summary>
    public sealed class WindowFocusRestorer : IStartable, ITickable, IDisposable
    {
        private readonly AppSettingsProvider _settingsProvider;
        private readonly ILogger<WindowFocusRestorer> _logger;
        private readonly TemplateInputActions _inputActions;

        private CancellationTokenSource _cts;
        private bool _isActive;
        private bool _isRestoreOn = true;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private FocusRestoreSchedule _schedule;
        private IntPtr _windowHandle;
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
        /// Settings.json에서 대기 시간과 재시도 간격을 읽어, Windows 스탠드얼론 빌드에서만 감시를 시작함.
        /// Settings.json을 읽지 못하면 AppSettingsProvider가 기본 Settings를 돌려주므로 기본 타이밍으로 동작함.
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

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Activate(settings ?? new Settings());
#else
            if (_logger != null) _logger.ZLogInformation($"[WindowFocusRestorer] Focus restore only runs in Windows standalone builds. Skipped on this platform.");
#endif
        }

        /// <summary>
        /// 운영자 키 입력으로 포커스 복구를 끄거나 켬.
        /// </summary>
        private void OnToggleFocusRestore(InputAction.CallbackContext _)
        {
            _isRestoreOn = !_isRestoreOn;

            if (_logger != null)
            {
                _logger.ZLogInformation($"[WindowFocusRestorer] Focus restore turned {(_isRestoreOn ? "on" : "off")} by the ToggleFocusRestore key.");
            }
        }

        /// <summary>
        /// 다음 시도 시각이 되면 창을 앞으로 가져오도록 시도함. 키로 꺼져 있거나 앱 종료가 시작됐으면 시도하지 않음.
        /// </summary>
        void ITickable.Tick()
        {
            if (!_isActive || !_isRestoreOn || _cts == null || _cts.IsCancellationRequested) return;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_schedule.TryBeginAttempt(Time.realtimeSinceStartupAsDouble))
            {
                TryRestoreFocus();
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
                _logger.ZLogInformation($"[WindowFocusRestorer] Started ({(_isRestoreOn ? "on" : "off")}). Brings the window back {_schedule.DelaySeconds}s after focus is lost and retries every {_schedule.RetryIntervalSeconds}s.");
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

            if (_logger != null)
            {
                _logger.ZLogInformation($"[WindowFocusRestorer] Focus lost. Will bring the window back in {_schedule.DelaySeconds}s.");
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
        /// 창을 앞으로 가져오는 데 쓰는 user32·kernel32 호출 모음.
        /// </summary>
        private static class NativeMethods
        {
            /// <summary>Unity 스탠드얼론 플레이어 창의 클래스 이름.</summary>
            private const string UnityWindowClassName = "UnityWndClass";

            private const int SwRestore = 9;

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
            private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentThreadId();

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentProcessId();

            /// <summary>
            /// 창을 포그라운드로 가져오고 성공 여부를 돌려줌.
            /// 포그라운드 잠금 때문에 SetForegroundWindow만으로는 거부되는 경우가 많아, 현재 포그라운드 창의
            /// 스레드에 입력 상태를 잠시 붙인 채 호출하고 바로 분리함. 최소화된 경우에만 SW_RESTORE를 써서,
            /// 최소화되지 않은 창의 크기·상태는 건드리지 않음.
            /// </summary>
            public static bool BringToForeground(IntPtr windowHandle)
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == windowHandle) return true;

                uint currentThreadId = GetCurrentThreadId();
                uint foregroundThreadId = foregroundWindow != IntPtr.Zero
                    ? GetWindowThreadProcessId(foregroundWindow, out uint _)
                    : 0;
                bool isAttached = foregroundThreadId != 0 && foregroundThreadId != currentThreadId
                    && AttachThreadInput(currentThreadId, foregroundThreadId, true);

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
                uint currentProcessId = GetCurrentProcessId();
                IntPtr candidate = IntPtr.Zero;

                while ((candidate = FindWindowEx(IntPtr.Zero, candidate, UnityWindowClassName, null)) != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(candidate, out uint processId);
                    if (processId == currentProcessId) return candidate;
                }

                return IntPtr.Zero;
            }
        }
#endif

        /// <summary>
        /// 컨테이너 파기 시 VContainer가 자동 호출함. 포커스 이벤트·키 입력 구독과 설정 로드를 정리함.
        /// </summary>
        void IDisposable.Dispose()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Application.focusChanged -= OnFocusChanged;
#endif
            if (_inputActions != null)
            {
                _inputActions.System.ToggleFocusRestore.performed -= OnToggleFocusRestore;
            }

            _isActive = false;

            if (_cts == null) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }
}
