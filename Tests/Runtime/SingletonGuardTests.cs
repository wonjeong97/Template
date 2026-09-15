using NUnit.Framework;
using UnityEngine;
using Wonjeong.Utils;

namespace Wonjeong.Tests
{
    [TestFixture]
    public class SingletonGuardTests
    {
        private class TestComponentA : MonoBehaviour { }
        private class TestComponentB : MonoBehaviour { }

        [SetUp]
        public void SetUp()
        {
            SingletonGuard<TestComponentA>.ResetForTesting();
            SingletonGuard<TestComponentB>.ResetForTesting();
        }

        [TearDown]
        public void TearDown()
        {
            SingletonGuard<TestComponentA>.ResetForTesting();
            SingletonGuard<TestComponentB>.ResetForTesting();
        }

        [Test]
        public void CheckDuplicate_FirstInstance_ReturnsFalse_AndSetsOriginalTrue()
        {
            GameObject go = new GameObject("TestA_1");
            try
            {
                TestComponentA comp = go.AddComponent<TestComponentA>();
                bool isDuplicate = SingletonGuard<TestComponentA>.CheckDuplicate(comp, out bool isOriginal, dontDestroyOnLoad: false);

                Assert.IsFalse(isDuplicate);
                Assert.IsTrue(isOriginal);
                Assert.IsTrue(SingletonGuard<TestComponentA>.IsInstantiated);
                Assert.IsTrue(comp.enabled);
            }
            finally
            {
                DestroyUtil.SafeDestroy(go);
            }
        }

        [Test]
        public void CheckDuplicate_SecondInstance_ReturnsTrue_DisablesComponent_AndSetsOriginalFalse()
        {
            GameObject go1 = new GameObject("TestA_1");
            GameObject go2 = new GameObject("TestA_2");
            try
            {
                TestComponentA comp1 = go1.AddComponent<TestComponentA>();
                SingletonGuard<TestComponentA>.CheckDuplicate(comp1, out bool isOriginal1, dontDestroyOnLoad: false);

                TestComponentA comp2 = go2.AddComponent<TestComponentA>();
                bool isDuplicate2 = SingletonGuard<TestComponentA>.CheckDuplicate(comp2, out bool isOriginal2, dontDestroyOnLoad: false);

                Assert.IsTrue(isDuplicate2);
                Assert.IsFalse(isOriginal2);
                Assert.IsFalse(comp2.enabled); // 중복 컴포넌트는 즉시 비활성화되어야 함
            }
            finally
            {
                DestroyUtil.SafeDestroy(go1);
                DestroyUtil.SafeDestroy(go2);
            }
        }

        [Test]
        public void Release_WhenOriginalDestroyed_ResetsInstantiatedFlag()
        {
            GameObject go = new GameObject("TestA");
            try
            {
                TestComponentA comp = go.AddComponent<TestComponentA>();
                SingletonGuard<TestComponentA>.CheckDuplicate(comp, out bool isOriginal, dontDestroyOnLoad: false);
                Assert.IsTrue(SingletonGuard<TestComponentA>.IsInstantiated);

                SingletonGuard<TestComponentA>.Release(isOriginal);
                Assert.IsFalse(SingletonGuard<TestComponentA>.IsInstantiated);
            }
            finally
            {
                DestroyUtil.SafeDestroy(go);
            }
        }

        [Test]
        public void Release_WhenNonOriginalDestroyed_KeepsInstantiatedFlag()
        {
            GameObject go1 = new GameObject("TestA_1");
            GameObject go2 = new GameObject("TestA_2");
            try
            {
                TestComponentA comp1 = go1.AddComponent<TestComponentA>();
                SingletonGuard<TestComponentA>.CheckDuplicate(comp1, out bool isOriginal1, dontDestroyOnLoad: false);

                TestComponentA comp2 = go2.AddComponent<TestComponentA>();
                SingletonGuard<TestComponentA>.CheckDuplicate(comp2, out bool isOriginal2, dontDestroyOnLoad: false);

                SingletonGuard<TestComponentA>.Release(isOriginal2); // 중복 객체 파괴 시
                Assert.IsTrue(SingletonGuard<TestComponentA>.IsInstantiated); // 여전히 true 유지
            }
            finally
            {
                DestroyUtil.SafeDestroy(go1);
                DestroyUtil.SafeDestroy(go2);
            }
        }

        [Test]
        public void DifferentComponentTypes_HaveIndependentState()
        {
            GameObject goA = new GameObject("TestA");
            GameObject goB = new GameObject("TestB");
            try
            {
                TestComponentA compA = goA.AddComponent<TestComponentA>();
                TestComponentB compB = goB.AddComponent<TestComponentB>();

                bool dupA = SingletonGuard<TestComponentA>.CheckDuplicate(compA, out bool origA, dontDestroyOnLoad: false);
                bool dupB = SingletonGuard<TestComponentB>.CheckDuplicate(compB, out bool origB, dontDestroyOnLoad: false);

                Assert.IsFalse(dupA);
                Assert.IsFalse(dupB);
                Assert.IsTrue(origA);
                Assert.IsTrue(origB);
            }
            finally
            {
                DestroyUtil.SafeDestroy(goA);
                DestroyUtil.SafeDestroy(goB);
            }
        }

        [Test]
        public void CheckDuplicate_WhenPreviousInstanceDestroyedWithoutRelease_SelfHeals()
        {
            GameObject go1 = new GameObject("TestA_1");
            TestComponentA comp1 = go1.AddComponent<TestComponentA>();
            SingletonGuard<TestComponentA>.CheckDuplicate(comp1, out bool isOriginal1, dontDestroyOnLoad: false);
            Assert.IsTrue(isOriginal1);

            // Release 호출 없이 GameObject를 파괴 (도메인 리로드 꺼진 에디터에서 플레이 모드 종료 상황 모사)
            DestroyUtil.SafeDestroy(go1);

            // 다음 인스턴스가 생성됨
            GameObject go2 = new GameObject("TestA_2");
            try
            {
                TestComponentA comp2 = go2.AddComponent<TestComponentA>();
                bool isDuplicate2 = SingletonGuard<TestComponentA>.CheckDuplicate(comp2, out bool isOriginal2, dontDestroyOnLoad: false);

                // 파괴된 이전 인스턴스를 감지하고 자가 복구하여 정상적으로 원본으로 등록되어야 함
                Assert.IsFalse(isDuplicate2);
                Assert.IsTrue(isOriginal2);
                Assert.IsTrue(comp2.enabled);
            }
            finally
            {
                DestroyUtil.SafeDestroy(go2);
            }
        }
    }
}
