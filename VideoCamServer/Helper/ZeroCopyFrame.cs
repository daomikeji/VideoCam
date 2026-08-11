using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoCamServer.Helper
{
    public sealed class ZeroCopyFrame
    {
        private byte[] _renderBuffer;
        private byte[] _writeBuffer;
        private int _writeLength;
        private readonly object _gate = new();
        private bool _isReading = false;
        public ZeroCopyFrame(int size)
        {
            _renderBuffer = GC.AllocateUninitializedArray<byte>(size, pinned: true);
            _writeBuffer = GC.AllocateUninitializedArray<byte>(size, pinned: true);
        }

        public void Push(Span<byte> data)
        {
            //if (!_isReading)
            //{
                data.CopyTo(_writeBuffer);
                _writeLength = data.Length;

                lock (_gate)
                {
                    (_renderBuffer, _writeBuffer) = (_writeBuffer, _renderBuffer);
                }
            //}
           
        }

        public (byte[] Buffer, int Length) GetRenderData()
        {
            lock (_gate)
            {
                _isReading = true;
                var copy = new byte[_writeLength];
                Buffer.BlockCopy(_renderBuffer, 0, copy, 0, _writeLength);
                return (copy,_writeLength);
            }
        }
        public void ReadEnd()
        {
            _isReading = false;
        }
    }
}
