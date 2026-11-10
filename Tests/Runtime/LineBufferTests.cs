using System.Collections.Generic;
using System.Text;
using HuliacDev.Network;
using NUnit.Framework;

namespace HuliacDev.Tests
{
    /// <summary>
    /// TCP로 받은 바이트를 줄로 나누는 LineBuffer 검증. TCP는 보낸 단위를 지켜 주지 않아 줄이 나뉘거나 붙어서 옴.
    /// </summary>
    public class LineBufferTests
    {
        /// <summary>
        /// 문자열을 ASCII 바이트로 바꿔 버퍼에 더하고, 이번에 완성된 줄을 돌려줌.
        /// </summary>
        private static List<string> Append(LineBuffer buffer, string text, out int discarded)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            List<string> lines = new List<string>();
            discarded = buffer.Append(bytes, bytes.Length, lines);
            return lines;
        }

        /// <summary>
        /// 한 줄이 여러 번에 나뉘어 오거나 여러 줄이 한 번에 와도 줄 단위로 나눠야 함. 받은 단위를 줄로 보면 값이 어긋남.
        /// </summary>
        [Test]
        public void 나뉘거나_붙어_온_바이트를_줄로_나눈다()
        {
            LineBuffer buffer = new LineBuffer(256);

            CollectionAssert.IsEmpty(Append(buffer, "ab", out _), "줄 끝이 오기 전에 줄을 내보냄");
            CollectionAssert.AreEqual(new[] { "abc", "de" }, Append(buffer, "c\nde\n", out _));
        }

        /// <summary>
        /// 아두이노 println의 "\r\n"은 줄 하나로 봐야 하고, 빈 줄은 넘기지 않아야 함.
        /// </summary>
        [Test]
        public void CRLF는_줄_하나로_보고_빈_줄은_넘기지_않는다()
        {
            LineBuffer buffer = new LineBuffer(256);

            CollectionAssert.AreEqual(new[] { "READY", "S2" }, Append(buffer, "READY\r\n\r\nS2\r\n", out _));
        }

        /// <summary>
        /// 줄 끝 없이 계속 오는 바이트로 메모리가 늘지 않게, 최대 길이를 넘는 줄은 버리고 다음 줄부터 다시 읽어야 함.
        /// </summary>
        [Test]
        public void 최대_길이를_넘는_줄은_버리고_다음_줄부터_읽는다()
        {
            LineBuffer buffer = new LineBuffer(4);

            List<string> lines = Append(buffer, "TOOLONG\nOK\n", out int discarded);

            CollectionAssert.AreEqual(new[] { "OK" }, lines);
            Assert.AreEqual(1, discarded, "버린 줄 수가 맞지 않음");
        }

        /// <summary>
        /// ASCII 밖 바이트는 '?'로 바꿔야 함. 그대로 글자로 바꾸면 보내기(Encoding.ASCII)와 다르게 엉뚱한 글자가 됨.
        /// </summary>
        [Test]
        public void ASCII_밖_바이트는_물음표로_바꾼다()
        {
            LineBuffer buffer = new LineBuffer(256);
            byte[] bytes = { (byte)'A', 0xEA, 0xB0, 0x80, 0x0A };
            List<string> lines = new List<string>();

            buffer.Append(bytes, bytes.Length, lines);

            CollectionAssert.AreEqual(new[] { "A???" }, lines);
        }

        /// <summary>
        /// 새 연결을 시작할 때 Clear하면 이전 연결에서 끊긴 줄 조각이 다음 줄 앞에 붙지 않아야 함.
        /// </summary>
        [Test]
        public void Clear하면_모으던_조각을_버린다()
        {
            LineBuffer buffer = new LineBuffer(256);
            Append(buffer, "half", out _);

            buffer.Clear();

            CollectionAssert.AreEqual(new[] { "S1" }, Append(buffer, "S1\n", out _));
        }
    }
}
