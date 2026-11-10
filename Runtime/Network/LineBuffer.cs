using System.Collections.Generic;
using System.Text;

namespace HuliacDev.Network
{
    /// <summary>
    /// TCP로 받은 바이트를 줄로 나누는 버퍼. TCP는 보낸 단위를 지켜 주지 않아 한 번에 여러 줄이나 줄의 일부가 올 수 있음.
    /// <para>'\n'과 '\r'을 모두 줄 끝으로 보고 빈 줄은 넘기지 않음. 정한 길이를 넘는 줄은 메모리가 늘지 않게 버리고
    /// 다음 줄 끝부터 다시 읽음. 글자는 ASCII로 보고(바이트 하나가 글자 하나) ASCII 밖 바이트는 '?'로 바꿈(보내기의 Encoding.ASCII와
    /// 같게). 수신 스레드 하나에서만 씀.</para>
    /// </summary>
    internal sealed class LineBuffer
    {
        private readonly StringBuilder _current = new StringBuilder();
        private readonly int _maxLineLength;
        private bool _isDiscarding;

        /// <summary>
        /// 줄 하나의 최대 글자 수를 정해 빈 버퍼를 만듦. 1보다 작으면 1로 봄.
        /// </summary>
        public LineBuffer(int maxLineLength)
        {
            _maxLineLength = maxLineLength < 1 ? 1 : maxLineLength;
        }

        /// <summary>
        /// 받은 바이트를 더하고 완성된 줄을 lines에 넣음. 길이를 넘겨 버린 줄의 수를 돌려줌.
        /// </summary>
        public int Append(byte[] buffer, int count, List<string> lines)
        {
            int discardedLines = 0;
            for (int i = 0; i < count; i++)
            {
                byte value = buffer[i];
                char c = value < 0x80 ? (char)value : '?';
                if (c == '\n' || c == '\r')
                {
                    if (_isDiscarding) discardedLines++;
                    else if (_current.Length > 0) lines.Add(_current.ToString());

                    _current.Clear();
                    _isDiscarding = false;
                    continue;
                }

                if (_isDiscarding) continue;

                if (_current.Length >= _maxLineLength)
                {
                    _current.Clear();
                    _isDiscarding = true;
                    continue;
                }

                _current.Append(c);
            }

            return discardedLines;
        }

        /// <summary>
        /// 모으던 줄을 버림. 새 연결을 시작할 때 이전 연결에서 끊긴 줄 조각이 붙지 않게 부름.
        /// </summary>
        public void Clear()
        {
            _current.Clear();
            _isDiscarding = false;
        }
    }
}
