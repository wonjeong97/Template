using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Wonjeong.UI;

namespace Wonjeong.Tests
{
    /// <summary>
    /// VideoManager의 RenderTexture 수명 관리 검증.
    ///
    /// 배경: VideoPlayer가 파괴되어도 그 targetTexture는 매니저의 추적 목록에 남으므로,
    /// 비디오 오브젝트를 동적으로 생성/파괴하는 화면에서는 VRAM이 계속 누적된다.
    /// ReleaseOrphanedRenderTextures()는 살아 있는 VideoPlayer가 참조하지 않는 것만
    /// 골라 해제해야 하며, 사용 중인 텍스처는 건드리면 안 된다.
    /// </summary>
    public class VideoManagerTests
    {
        private GameObject _managerGo;
        private VideoManager _videoManager;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _managerGo = new GameObject("VideoManagerTests");
            _videoManager = _managerGo.AddComponent<VideoManager>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _spawned.Clear();

            if (_managerGo != null) UnityEngine.Object.DestroyImmediate(_managerGo);
        }

        /// <summary>
        /// 파괴된 VideoPlayer의 텍스처만 회수하고, 사용 중인 것은 유지해야 함.
        /// </summary>
        [Test]
        public void 고아_RenderTexture만_회수하고_사용중인_것은_유지한다()
        {
            VideoPlayer keep = CreateWiredVideo("Keep");
            VideoPlayer dead1 = CreateWiredVideo("Dead1");
            VideoPlayer dead2 = CreateWiredVideo("Dead2");

            RenderTexture keepTexture = keep.targetTexture;
            Assert.AreEqual(3, GetTrackedCount(), "생성한 3개가 모두 추적되어야 함");

            DestroyVideo(dead1);
            DestroyVideo(dead2);

            // 파괴만으로는 목록에서 빠지지 않음 — 이것이 누수의 원인
            Assert.AreEqual(3, GetTrackedCount(), "VideoPlayer 파괴만으로는 목록이 줄지 않아야 함(현 설계)");

            int released = _videoManager.ReleaseOrphanedRenderTextures();

            Assert.AreEqual(2, released, "파괴된 2개가 회수되어야 함");
            Assert.AreEqual(1, GetTrackedCount(), "사용 중인 1개만 남아야 함");
            Assert.IsTrue(keepTexture != null, "사용 중인 RenderTexture가 파괴되면 화면이 깨짐");
            Assert.AreSame(keepTexture, keep.targetTexture, "사용 중인 VideoPlayer의 연결이 끊기면 안 됨");
        }

        /// <summary>
        /// 회수할 것이 없으면 아무것도 건드리지 않아야 함.
        /// </summary>
        [Test]
        public void 고아가_없으면_아무것도_회수하지_않는다()
        {
            CreateWiredVideo("Alive1");
            CreateWiredVideo("Alive2");

            int released = _videoManager.ReleaseOrphanedRenderTextures();

            Assert.AreEqual(0, released);
            Assert.AreEqual(2, GetTrackedCount());
        }

        /// <summary>
        /// 같은 VideoPlayer에 다시 연결하면 이전 텍스처가 즉시 정리되어야 함.
        /// (재연결 때마다 텍스처가 쌓이면 그것도 누수임)
        /// </summary>
        [Test]
        public void 같은_VideoPlayer에_재연결하면_이전_텍스처가_교체된다()
        {
            VideoPlayer vp = CreateWiredVideo("Rewire");
            RenderTexture first = vp.targetTexture;
            Assert.AreEqual(1, GetTrackedCount());

            _videoManager.WireRawImageAndRenderTexture(vp, vp.GetComponent<RawImage>(), new Vector2Int(32, 32));

            Assert.AreEqual(1, GetTrackedCount(), "재연결 시 이전 텍스처가 목록에서 빠져야 함");
            Assert.AreNotSame(first, vp.targetTexture, "새 텍스처로 교체되어야 함");
        }

        /// <summary>
        /// 크기가 0 이하로 들어와도 유효한 RenderTexture를 만들어야 함.
        /// (JSON 설정 오류로 size가 비어 오는 경우 대비)
        /// </summary>
        [Test]
        public void 크기가_0이어도_유효한_텍스처를_생성한다()
        {
            GameObject go = new GameObject("ZeroSize");
            _spawned.Add(go);
            RawImage raw = go.AddComponent<RawImage>();
            VideoPlayer vp = go.AddComponent<VideoPlayer>();

            RenderTexture rt = _videoManager.WireRawImageAndRenderTexture(vp, raw, new Vector2Int(0, 0));

            Assert.IsNotNull(rt);
            Assert.GreaterOrEqual(rt.width, 1);
            Assert.GreaterOrEqual(rt.height, 1);
        }

        /// <summary>
        /// VideoPlayer가 null이면 예외 없이 null을 반환해야 함.
        /// </summary>
        [Test]
        public void VideoPlayer가_null이면_null을_반환한다()
        {
            RenderTexture rt = _videoManager.WireRawImageAndRenderTexture(null, null, new Vector2Int(16, 16));

            Assert.IsNull(rt);
            Assert.AreEqual(0, GetTrackedCount());
        }

        private VideoPlayer CreateWiredVideo(string name)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);

            RawImage raw = go.AddComponent<RawImage>();
            VideoPlayer vp = go.AddComponent<VideoPlayer>();
            _videoManager.WireRawImageAndRenderTexture(vp, raw, new Vector2Int(32, 32));

            return vp;
        }

        private void DestroyVideo(VideoPlayer vp)
        {
            GameObject go = vp.gameObject;
            _spawned.Remove(go);
            UnityEngine.Object.DestroyImmediate(go);
        }

        private int GetTrackedCount()
        {
            FieldInfo field = typeof(VideoManager).GetField("_activeRenderTextures",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var list = (ICollection<RenderTexture>)field.GetValue(_videoManager);
            return list.Count;
        }
    }
}
