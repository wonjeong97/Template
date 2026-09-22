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

            // 배열을 고정해 직접 읽음. 비관리 힙 할당(AllocHGlobal)과 중간 복사를 모두 생략함.
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(IntPtr.Add(handle.AddrOfPinnedObject(), offset));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// 구조체를 바이트 배열로 직렬화함 (새 바이트 배열 할당).
        /// </summary>
        /// <typeparam name="T">구조체 타입</typeparam>
        public static byte[] ToBytes<T>(in T packet) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            ToBytes(in packet, bytes, 0);
            return bytes;
        }

        /// <summary>
        /// 구조체를 기존 바이트 배열에 직렬화하여 GC 할당을 방지함. 기록한 바이트 수를 반환함.
        /// </summary>
        /// <typeparam name="T">구조체 타입</typeparam>
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

            // 대상 배열을 고정해 그 자리에 바로 기록함. 비관리 힙 할당과 중간 복사가 없음.
            GCHandle handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
            try
            {
                // 제네릭 오버로드가 선택되므로 값 타입 박싱은 발생하지 않음.
                Marshal.StructureToPtr(packet, IntPtr.Add(handle.AddrOfPinnedObject(), offset), false);
                return size;
            }
            finally
            {
                handle.Free();
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
