using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wonjeong.UI;

namespace Wonjeong.Tests
{
    /// <summary>
    /// 진행 중인 로드를 공유하는 경로가 동시 요청에서 안전한지 검증.
    ///
    /// 배경: SoundManager와 UIManager는 같은 리소스를 동시에 요청받으면 진행 중인 작업을
    /// 공유하여 중복 I/O를 막는다. 공유 소스로 UniTask를 쓰면 완료 전에 두 번째 소비자가
    /// await할 때 continuation이 중복 등록되어 InvalidOperationException
    /// ("Already continuation registered")이 발생한다.
    ///
    /// 이 결함은 fire-and-forget(.Forget()) 경로에서 발생하므로 예외가 밖으로 드러나지 않고
    /// 두 번째 요청만 조용히 실패한다. 실제로 SoundManager에서 이 현상을 확인했다.
    /// </summary>
    public class ConcurrentLoadTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        /// <summary>
        /// 공유 소스가 UniTask면 실패하고 Task면 통과하는 조건을 그대로 재현함.
        /// 완료 전에 두 소비자가 동시에 대기하는 상황을 만든다.
        /// </summary>
        [UnityTest]
        public IEnumerator 진행중인_Task를_두_소비자가_동시에_대기해도_예외가_없다() => UniTask.ToCoroutine(async () =>
        {
            Task<int> shared = DelayedValueAsync(80, 7).AsTask();

            UniTask<int> first = WaitOn(shared);
            UniTask<int> second = WaitOn(shared);
            UniTask<int> third = WaitOn(shared);

            int a = await first;
            int b = await second;
            int c = await third;

            Assert.AreEqual(7, a);
            Assert.AreEqual(7, b, "두 번째 소비자가 결과를 받지 못함");
            Assert.AreEqual(7, c, "세 번째 소비자가 결과를 받지 못함");
        });

        /// <summary>
        /// SoundManager의 진행 중 로드 추적 딕셔너리가 Task 기반인지 확인함.
        /// UniTask 기반으로 되돌아가면 동시 요청 시 두 번째가 조용히 실패하므로 타입으로 고정함.
        /// </summary>
        [Test]
        public void SoundManager의_진행중_로드는_Task로_공유된다()
        {
            AssertActiveLoadDictionaryIsTaskBased(typeof(SoundManager), "_activeDownloads");
        }

        /// <summary>
        /// UIManager의 진행 중 스프라이트 로드도 동일하게 Task 기반이어야 함.
        /// </summary>
        [Test]
        public void UIManager의_진행중_스프라이트_로드는_Task로_공유된다()
        {
            AssertActiveLoadDictionaryIsTaskBased(typeof(UIManager), "_activeSpriteLoads");
        }

        /// <summary>
        /// 지정한 필드가 Dictionary&lt;string, Task&lt;T&gt;&gt; 형태인지 검사함.
        /// </summary>
        private static void AssertActiveLoadDictionaryIsTaskBased(Type owner, string fieldName)
        {
            FieldInfo field = owner.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{owner.Name}.{fieldName} 필드를 찾을 수 없음");

            Type valueType = field.FieldType.GetGenericArguments()[1];

            Assert.IsTrue(
                valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(Task<>),
                $"{owner.Name}.{fieldName}의 값 타입이 Task<>가 아님(실제: {valueType.Name}). " +
                "UniTask는 완료 전 다중 await를 지원하지 않아 동시 요청 시 두 번째가 실패함.");
        }

        private static async UniTask<int> DelayedValueAsync(int milliseconds, int value)
        {
            await UniTask.Delay(milliseconds, DelayType.UnscaledDeltaTime);
            return value;
        }

        private static async UniTask<int> WaitOn(Task<int> task)
        {
            return await task;
        }
    }
}
