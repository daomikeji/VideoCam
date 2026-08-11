using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoCamServer.Helper
{
    public class UdpLatestBuffer
    {
        private readonly byte[][] _buffer;
        private int _writeIndex;
        private int _readIndex;
        private bool _hasData;
        private readonly object _lock = new();

        public UdpLatestBuffer(int capacity = 1024, int packetSize = 1472)
        {
            _buffer = new byte[capacity][];
            for (int i = 0; i < capacity; i++)
                _buffer[i] = new byte[packetSize];
        }

        public void Write(byte[] data)
        {
            lock (_lock)
            {
                Array.Copy(data, _buffer[_writeIndex], data.Length);
                _writeIndex = (_writeIndex + 1) % _buffer.Length;

                // 如果写指针追上了读指针，说明读得太慢，丢弃旧数据
                if (_writeIndex == _readIndex && _hasData)
                    _readIndex = (_readIndex + 1) % _buffer.Length;

                _hasData = true;
            }
        }

        public bool TryReadLatest(out byte[] data)
        {
            lock (_lock)
            {
                if (!_hasData)
                {
                    data = null;
                    return false;
                }

                // 直接读最新写入的那一条
                int latestIndex = (_writeIndex - 1 + _buffer.Length) % _buffer.Length;
                data = _buffer[latestIndex].ToArray();
                return true;
            }
        }
    }
}
