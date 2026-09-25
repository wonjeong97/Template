using System;
using UnityEngine;

namespace HuliacDev.Data
{
    // "JSON에서 미지정" 표기 규칙 (JsonUtility는 null·키 없음을 표현하지 못하고, 빠진 필드에 C# 필드 초기값을 남김):
    // - 0이 의미 없는 값인 필드(글자 크기, 클릭 횟수 등)는 "0 이하면 미지정"으로 봄. 초기값은 따로 두지 않음.
    // - 0이 정상 값일 수 있는 필드(투명도, 정규화 좌표 등)는 필드 초기값을 표시값 -1로 두고, 그 값이면 미지정으로 봄.
    // 새 설정 필드를 추가할 때도 이 둘 중 하나를 따르고, 필드 summary에 어느 규칙인지 적을 것.

    [Serializable]
    public class UISettingBase
    {
        public string name;
        public Vector2 position;
        public Vector2 size;
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;
    }

    [Serializable]
    public enum UIImageType
    {
        Simple = 0,
        Sliced,
        Tiled,
        Filled
    }

    /// <summary>
    /// GameCloser 설정. 필드 초기값 -1은 "JSON에서 미지정"을 뜻하는 표시값임(파일 상단 표기 규칙 참고).
    /// JsonUtility는 JSON에 없는 필드(중첩 객체 키가 통째로 없는 경우 포함)에 초기값을 그대로 남기므로,
    /// GameCloser는 미지정이거나 잘못 지정된 필드를 건너뛰고 인스펙터 기본값을 유지함(잘못 지정된 경우 경고).
    /// </summary>
    [Serializable]
    public class CloseSetting
    {
        /// <summary>
        /// "JSON에서 미지정"을 뜻하는 표시값. 필드 초기값과 CloseSettingResolver의 판정이 모두 이 상수를 씀.
        /// JSON에 이 값(-1)을 직접 적어도 미지정과 구분되지 않음.
        /// </summary>
        public const float UnsetValue = -1f;

        /// <summary>
        /// 버튼 모서리 위치(0~1 정규화 좌표). 표시값 (-1, -1)이면 미지정.
        /// 한 성분만 적었거나 0~1을 벗어나면 적용하지 않고 경고함.
        /// </summary>
        public Vector2 position = new Vector2(UnsetValue, UnsetValue);

        /// <summary>앱 종료에 필요한 연속 클릭 횟수. 0 이하면 미지정이며, 이때는 closeSetting 전체를 무시함. 3 미만이면 오터치로 꺼질 수 있어 경고함(적용은 함).</summary>
        public int numToClose;

        /// <summary>연속 클릭으로 인정하는 제한 시간(초, 최소 1초). 표시값 -1이면 미지정. 0 이하를 적으면 적용하지 않고 경고하며, 1초 미만 양수는 1초로 올려 적용하고 경고함.</summary>
        public float resetClickTime = UnsetValue;

        /// <summary>버튼 이미지 투명도(0~1). 표시값 -1이면 미지정. 0은 완전히 투명한 정상 값이며, 0~1을 벗어나면 적용하지 않고 경고함.</summary>
        public float imageAlpha = UnsetValue;
    }

    /// <summary>
    /// 요일 하나에 대한 종료 스케줄. time은 "HH:mm" 형식(예: "21:30")이며,
    /// enabled가 false면 이 요일에는 예약 종료를 하지 않음.
    /// </summary>
    [Serializable]
    public class ShutdownDaySchedule
    {
        public bool enabled;
        public string time;
    }

    /// <summary>
    /// 특정 날짜 하나에 대한 종료 스케줄 재정의. date는 "yyyy-MM-dd" 형식이며,
    /// 해당 날짜의 요일별 기본 스케줄(ShutdownSetting의 monday~sunday)보다 우선 적용됨.
    /// </summary>
    [Serializable]
    public class ShutdownDateOverride
    {
        public string date;
        public bool enabled;
        public string time;
    }

    /// <summary>
    /// 예약 종료 스케줄. Settings.json과 별개로 StreamingAssets의 ShutdownSettings.json에
    /// 단독 루트 오브젝트로 저장되며, 전용 편집 도구(Tools~/ShutdownScheduleEditor)로 편집함.
    /// dateOverrides에 등록된 날짜는 같은 날짜의 요일별 기본 스케줄보다 우선 적용됨.
    /// </summary>
    [Serializable]
    public class ShutdownSetting
    {
        public ShutdownDaySchedule monday;
        public ShutdownDaySchedule tuesday;
        public ShutdownDaySchedule wednesday;
        public ShutdownDaySchedule thursday;
        public ShutdownDaySchedule friday;
        public ShutdownDaySchedule saturday;
        public ShutdownDaySchedule sunday;
        public ShutdownDateOverride[] dateOverrides;

        /// <summary>
        /// 종료 시각에 실행할 Windows shutdown 명령의 인수. 기본값 "-s -f -t 45"는
        /// 45초 뒤 강제 종료를 뜻함(-s 종료, -f 실행 중인 앱 강제 종료, -t 지연 시간(초)).
        /// 지연 시간을 두는 이유는 종료 로그 전송이 끝날 시간을 확보하기 위함이며,
        /// 재부팅으로 바꾸려면 -s 대신 -r을 쓰면 됨.
        /// </summary>
        public string shutdownArguments;
    }

    // ---------------------- UISettingBase 상속-------------------------------
    
    [Serializable]
    public class ImageSetting : UISettingBase
    {
        public string sourceImage;
        public Color color = Color.white;
        public UIImageType type = UIImageType.Simple;
    }

    [Serializable]
    public class TextSetting : UISettingBase
    {
        public string text;
        public string fontName;
        /// <summary>
        /// 글자 크기. 0 이하면 미지정으로 보고 UIManager가 기존 글자 크기를 유지함
        /// (JSON에서 이 필드를 빼면 기본값 0이 들어오기 때문).
        /// </summary>
        public int fontSize;
        public Color fontColor = Color.white;
        public TextAnchor alignment = TextAnchor.MiddleCenter;
        public bool isBold;
    }

    [Serializable]
    public class VideoSetting : UISettingBase
    {
        public string fileName;
        public float volume;
    }

    [Serializable]
    public class ButtonSetting : UISettingBase
    {
        public ImageSetting buttonBackgroundImage;
        public TextSetting buttonText;
        public string buttonSound;
    }
    
    // --------------------------------------------------------------------

    /// <summary>
    /// 폰트 키와 Addressables 주소의 매핑.
    /// key는 TextSetting.fontName에서 참조하는 이름이며 자유롭게 명명할 수 있음.
    /// </summary>
    [Serializable]
    public class FontSetting
    {
        public string key;
        public string address;
    }

    [Serializable]
    public class SoundSetting
    {
        public string key;
        public string clipPath;
        public float volume = 1.0f;
    }

    [Serializable]
    public class Settings
    {
        public bool useInactivityTimer;
        public float warningTime;
        public float resetTime;
        public float fadeTime;

        /// <summary>
        /// 목표 프레임 레이트(FPS). 0 이하(미설정)면 아무것도 변경하지 않으며, 이 경우 실제 동작은
        /// 현재 활성 품질 레벨(QualitySettings)의 vSyncCount에 그대로 좌우됨
        /// (이 템플릿 기준 Performant=0→무제한, Balanced/High Fidelity=1→디스플레이 주사율 고정).
        /// 품질 레벨이 바뀌면 미설정 시의 동작도 함께 바뀌어 예측하기 어려우므로, 특정 FPS를
        /// 보장하려면 이 값을 명시적으로 지정할 것. 전시/키오스크처럼 장시간 구동되는 환경에서는
        /// 디스플레이 주사율을 넘는 프레임을 그려 낭비되는 GPU 자원·발열·전력 소모를 줄이기 위해
        /// 30~60 사이 값으로 명시적으로 캡을 거는 것을 권장함.
        /// </summary>
        public int targetFrameRate;

        public CloseSetting closeSetting;
        public FontSetting[] fonts;
        public SoundSetting[] sounds;

        /// <summary>
        /// 프로그램 시작 로그를 전송할 서버 API URL.
        /// idx_content_device, uid 등 쿼리 파라미터가 서버 측에서 콘텐츠별로 이미 발급되어
        /// message= 까지 포함된 형태로 전달되므로, 여기에는 그 URL 전체를 그대로 저장하고
        /// ApiManager가 message 값만 이어붙여 GET 요청을 보냄. 비어 있으면 전송하지 않음.
        /// </summary>
        public string apiUrl;
    }
}