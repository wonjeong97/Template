using System;
using UnityEngine;

namespace Wonjeong.Data
{
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

    [Serializable]
    public class CloseSetting
    {
        public Vector2 position;
        public int numToClose;
        public float resetClickTime;
        public float imageAlpha;
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