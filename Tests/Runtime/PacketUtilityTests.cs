using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using HuliacDev.Network;

namespace HuliacDev.Tests
{
    public class PacketUtilityTests
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TestSensorPacket
        {
            public byte Header;
            public ushort SensorId;
            public float Value;
            public byte Checksum;
        }

        [Test]
        public void Pack1_구조체_크기가_패딩없이_정확히_계산된다()
        {
            // byte(1) + ushort(2) + float(4) + byte(1) = 8 bytes
            int size = PacketUtility.GetPacketSize<TestSensorPacket>();
            Assert.AreEqual(8, size, "Pack=1이 적용되지 않아 패딩 바이트가 포함됨");
        }

        [Test]
        public void 구조체_직렬화_후_역직렬화시_데이터가_손실없이_일치한다()
        {
            var original = new TestSensorPacket
            {
                Header = 0xAA,
                SensorId = 1234,
                Value = 98.76f,
                Checksum = 0x55
            };

            byte[] bytes = PacketUtility.ToBytes(in original);
            Assert.AreEqual(8, bytes.Length);

            TestSensorPacket restored = PacketUtility.FromBytes<TestSensorPacket>(bytes);

            Assert.AreEqual(original.Header, restored.Header);
            Assert.AreEqual(original.SensorId, restored.SensorId);
            Assert.AreEqual(original.Value, restored.Value, 0.0001f);
            Assert.AreEqual(original.Checksum, restored.Checksum);
        }

        [Test]
        public void 기존_버퍼_오프셋을_활용한_직렬화_역직렬화가_정상_동작한다()
        {
            var packet = new TestSensorPacket
            {
                Header = 0x02,
                SensorId = 500,
                Value = 12.34f,
                Checksum = 0x03
            };

            // 앞뒤에 더미 헤더/트레일러가 있는 패킷 버퍼 시뮬레이션
            byte[] buffer = new byte[16];
            buffer[0] = 0xFF; // prefix dummy
            buffer[1] = 0xFE;

            int written = PacketUtility.ToBytes(in packet, buffer, offset: 2);
            Assert.AreEqual(8, written);

            TestSensorPacket parsed = PacketUtility.FromBytes<TestSensorPacket>(buffer, offset: 2);

            Assert.AreEqual(packet.Header, parsed.Header);
            Assert.AreEqual(packet.SensorId, parsed.SensorId);
            Assert.AreEqual(packet.Value, parsed.Value, 0.0001f);
            Assert.AreEqual(packet.Checksum, parsed.Checksum);
        }

        [Test]
        public void 버퍼크기가_부족하면_예외가_발생한다()
        {
            byte[] tooSmallBuffer = new byte[4];
            Assert.Throws<ArgumentException>(() =>
            {
                PacketUtility.FromBytes<TestSensorPacket>(tooSmallBuffer);
            });
        }
    }
}
