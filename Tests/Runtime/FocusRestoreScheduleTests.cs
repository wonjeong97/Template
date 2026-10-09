using NUnit.Framework;
using UnityEngine;
using HuliacDev.Core;
using HuliacDev.Data;

namespace HuliacDev.Tests
{
    /// <summary>
    /// 창 포커스 복구 시도 시점 판단(FocusRestoreSchedule)과 설정 키 해석 검증.
    /// user32 호출은 Windows 스탠드얼론 빌드에서만 컴파일되어 테스트할 수 없으므로, '언제 다시 시도할지'만 분리해 검증함.
    /// </summary>
    public class FocusRestoreScheduleTests
    {
        private const float Delay = 3f;
        private const float Retry = 5f;

        /// <summary>
        /// 포커스를 잃기 전에는 시간이 아무리 지나도 시도하지 않아야 함.
        /// </summary>
        [Test]
        public void 포커스를_잃기_전에는_시도하지_않는다()
        {
            FocusRestoreSchedule schedule = new FocusRestoreSchedule(Delay, Retry);

            Assert.IsFalse(schedule.TryBeginAttempt(0d));
            Assert.IsFalse(schedule.TryBeginAttempt(1000d));
        }

        /// <summary>
        /// 포커스를 잃으면 대기 시간이 지나기 전에는 시도하지 않고, 지나면 첫 시도를 해야 함.
        /// </summary>
        [Test]
        public void 포커스를_잃으면_대기_시간이_지난_뒤에_첫_시도를_한다()
        {
            FocusRestoreSchedule schedule = new FocusRestoreSchedule(Delay, Retry);
            schedule.OnFocusLost(10d);

            Assert.IsFalse(schedule.TryBeginAttempt(12.9d), "대기 시간 전에 시도함");
            Assert.IsTrue(schedule.TryBeginAttempt(13d), "대기 시간이 지났는데 시도하지 않음");
            Assert.AreEqual(1, schedule.AttemptCount);
        }

        /// <summary>
        /// 첫 시도 뒤에는 매 프레임이 아니라 재시도 간격마다 한 번씩만 시도해야 함.
        /// </summary>
        [Test]
        public void 첫_시도_뒤에는_재시도_간격마다_한_번씩_시도한다()
        {
            FocusRestoreSchedule schedule = new FocusRestoreSchedule(Delay, Retry);
            schedule.OnFocusLost(0d);

            Assert.IsTrue(schedule.TryBeginAttempt(3d));
            Assert.IsFalse(schedule.TryBeginAttempt(3d), "같은 시각에 다시 시도함");
            Assert.IsFalse(schedule.TryBeginAttempt(7.9d), "재시도 간격 전에 시도함");
            Assert.IsTrue(schedule.TryBeginAttempt(8d), "재시도 간격이 지났는데 시도하지 않음");
            Assert.AreEqual(2, schedule.AttemptCount);
        }

        /// <summary>
        /// 포커스가 돌아오면 이후 시도를 멈추고, 기다리던 중이었음을 알려야 함.
        /// </summary>
        [Test]
        public void 포커스가_돌아오면_시도를_멈춘다()
        {
            FocusRestoreSchedule schedule = new FocusRestoreSchedule(Delay, Retry);
            schedule.OnFocusLost(0d);
            Assert.IsTrue(schedule.TryBeginAttempt(3d));

            Assert.IsTrue(schedule.OnFocusGained(), "기다리던 중이었음을 알리지 않음");
            Assert.IsFalse(schedule.IsWaitingForFocus);
            Assert.IsFalse(schedule.TryBeginAttempt(100d), "포커스가 돌아왔는데 계속 시도함");
            Assert.IsFalse(schedule.OnFocusGained(), "기다리지 않던 상태에서 다시 알림");
        }

        /// <summary>
        /// 기다리는 중에 포커스 이탈이 다시 기록돼도 첫 시도 시각이 뒤로 밀리면 안 됨.
        /// </summary>
        [Test]
        public void 기다리는_중에_포커스_이탈이_다시_와도_첫_시도를_미루지_않는다()
        {
            FocusRestoreSchedule schedule = new FocusRestoreSchedule(Delay, Retry);
            schedule.OnFocusLost(0d);
            schedule.OnFocusLost(2d);

            Assert.IsTrue(schedule.TryBeginAttempt(3d), "두 번째 이탈 기록으로 첫 시도가 밀림");
        }

        /// <summary>
        /// 포커스가 돌아온 뒤 다시 잃으면 대기 시간부터 다시 세고, 시도 횟수와 실패 보고 상태도 새로 시작해야 함.
        /// </summary>
        [Test]
        public void 포커스를_다시_잃으면_대기_시간과_실패_보고를_새로_시작한다()
        {
            FocusRestoreSchedule schedule = new FocusRestoreSchedule(Delay, Retry);
            schedule.OnFocusLost(0d);
            Assert.IsTrue(schedule.TryBeginAttempt(3d));
            Assert.IsTrue(schedule.RecordFailure());
            schedule.OnFocusGained();

            schedule.OnFocusLost(50d);

            Assert.AreEqual(0, schedule.AttemptCount, "시도 횟수가 초기화되지 않음");
            Assert.IsFalse(schedule.TryBeginAttempt(52.9d), "대기 시간 전에 시도함");
            Assert.IsTrue(schedule.TryBeginAttempt(53d));
            Assert.IsTrue(schedule.RecordFailure(), "새 이탈의 첫 실패를 보고하지 않음");
        }

        /// <summary>
        /// 실패 경고가 시도마다 남지 않도록, 한 번의 포커스 이탈에서 첫 실패만 보고해야 함.
        /// </summary>
        [Test]
        public void 실패는_포커스_이탈마다_처음_한_번만_보고한다()
        {
            FocusRestoreSchedule schedule = new FocusRestoreSchedule(Delay, Retry);
            schedule.OnFocusLost(0d);

            Assert.IsTrue(schedule.TryBeginAttempt(3d));
            Assert.IsTrue(schedule.RecordFailure(), "첫 실패를 보고하지 않음");
            Assert.IsTrue(schedule.TryBeginAttempt(8d));
            Assert.IsFalse(schedule.RecordFailure(), "두 번째 실패도 보고함");
        }

        /// <summary>
        /// 설정 값이 0 이하(미지정)면 기본값을, 1초 미만 양수면 1초를, 그 밖에는 설정 값을 그대로 써야 함.
        /// </summary>
        [Test]
        public void 설정_값은_미지정이면_기본값_최소값_미만이면_최소값을_쓴다()
        {
            FocusRestoreSchedule unset = new FocusRestoreSchedule(0f, -1f);
            Assert.AreEqual(FocusRestoreSchedule.DefaultDelaySeconds, unset.DelaySeconds);
            Assert.AreEqual(FocusRestoreSchedule.DefaultRetryIntervalSeconds, unset.RetryIntervalSeconds);

            FocusRestoreSchedule tooShort = new FocusRestoreSchedule(0.1f, 0.5f);
            Assert.AreEqual(FocusRestoreSchedule.MinIntervalSeconds, tooShort.DelaySeconds);
            Assert.AreEqual(FocusRestoreSchedule.MinIntervalSeconds, tooShort.RetryIntervalSeconds);

            FocusRestoreSchedule configured = new FocusRestoreSchedule(10f, 2.5f);
            Assert.AreEqual(10f, configured.DelaySeconds);
            Assert.AreEqual(2.5f, configured.RetryIntervalSeconds);
        }

        /// <summary>
        /// Settings.json 키 이름이 필드와 맞는지, 키가 없으면 미지정(0)으로 남아 기본 타이밍이 쓰이는지 확인함.
        /// </summary>
        [Test]
        public void Settings_json_타이밍_키를_읽고_키가_없으면_기본값을_쓴다()
        {
            Settings configured = JsonUtility.FromJson<Settings>("{\"focusRestoreDelay\":5,\"focusRestoreRetryInterval\":2}");
            FocusRestoreSchedule fromConfigured = new FocusRestoreSchedule(configured.focusRestoreDelay, configured.focusRestoreRetryInterval);
            Assert.AreEqual(5f, fromConfigured.DelaySeconds);
            Assert.AreEqual(2f, fromConfigured.RetryIntervalSeconds);

            Settings missing = JsonUtility.FromJson<Settings>("{}");
            FocusRestoreSchedule fromMissing = new FocusRestoreSchedule(missing.focusRestoreDelay, missing.focusRestoreRetryInterval);
            Assert.AreEqual(FocusRestoreSchedule.DefaultDelaySeconds, fromMissing.DelaySeconds);
            Assert.AreEqual(FocusRestoreSchedule.DefaultRetryIntervalSeconds, fromMissing.RetryIntervalSeconds);
        }
    }
}
