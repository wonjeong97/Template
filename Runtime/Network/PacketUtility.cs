using System;
using System.Runtime.InteropServices;

namespace HuliacDev.Network
{
    /// <summary>
    /// 하드웨어 센서, 시리얼 통신, 소켓 패킷 등 고정 크기 바이너리 데이터와 
    /// C# 구조체([StructLayout(LayoutKind.Sequential, Pack = 1)]) 간의 상호 변환을 지원하는 유틸리티.
    /// </summary>
    public static class PacketUtility
    {
        /// <summary>
        /// 바이트 배열을 구조체로 역직렬화함.
        /// </summary>
        /// <typeparam name="T">대상 구조체 타입</typeparam>
        /// <param name="bytes">패킷 바이트 데이터 배열</param>
        /// <param name="offset">읽기 시작할 인덱스 오프셋</param>
        /// <returns>역직렬화된 구조체 인스턴스</returns>
        public static T FromBytes<T>(byte[] bytes, int offset = 0) where T : struct
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            int size = Marshal.SizeOf<T>();
            if (offset < 0 || bytes.Length - offset < size)
            {
                throw new ArgumentException($"Buffer length from offset ({bytes.Length - offset}) is smaller than target struct size ({size}).");
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, offset, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 구조체를 바이트 배열로 직렬화함 (새 바이트 배열 할당).
        /// </summary>
        /// <typeparam name="T">구조체 타입</typeparam>
        /// <param name="packet">직렬화할 구조체 데이터</param>
        /// <returns>직렬화된 바이트 배열</returns>
        public static byte[] ToBytes<T>(in T packet) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            ToBytes(in packet, bytes, 0);
            return bytes;
        }

        /// <summary>
        /// 구조체를 기존 바이트 배열에 직렬화하여 GC 할당을 방지함.
        /// </summary>
        /// <typeparam name="T">구조체 타입</typeparam>
        /// <param name="packet">직렬화할 구조체 데이터</param>
        /// <param name="destination">결과를 기록할 대상 바이트 배열</param>
        /// <param name="offset">기록 시작 오프셋</param>
        /// <returns>기록된 바이트 수(구조체 크기)</returns>
        public static int ToBytes<T>(in T packet, byte[] destination, int offset = 0) where T : struct
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            int size = Marshal.SizeOf<T>();
            if (offset < 0 || destination.Length - offset < size)
            {
                throw new ArgumentException($"Destination buffer remaining length ({destination.Length - offset}) is smaller than struct size ({size}).");
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(packet, ptr, false);
                Marshal.Copy(ptr, destination, offset, size);
                return size;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 해당 구조체의 마샬링 크기를 반환함.
        /// </summary>
        public static int GetPacketSize<T>() where T : struct
        {
            return Marshal.SizeOf<T>();
        }
    }
}
