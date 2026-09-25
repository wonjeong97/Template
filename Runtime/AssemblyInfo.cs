using System.Runtime.CompilerServices;

// 순수 계산 로직(CloseSettingResolver 등)을 공개 API로 노출하지 않고 테스트하기 위해
// 테스트 어셈블리에만 internal 멤버 접근을 허용함(새 리플렉션 대신 사용).
[assembly: InternalsVisibleTo("HuliacDev.Template.Tests")]
