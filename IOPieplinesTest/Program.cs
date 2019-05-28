using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IOPieplinesTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(new IPEndPoint(IPAddress.Loopback, 1081));
            listenSocket.Listen(120);

            while (true)
            {
                var socket = await listenSocket.AcceptAsync();
                _ = ProcessLinesAsync(socket);
            }

        }

        private static async Task ProcessLinesAsync(Socket socket)
        {
            var pipe = new Pipe();

            const int minimumBufferSize = 512;
            Memory<byte> memory = pipe.Writer.GetMemory(minimumBufferSize);

            int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);
            pipe.Writer.Advance(bytesRead);
            FlushResult r = await pipe.Writer.FlushAsync();

            ReadResult result = await pipe.Reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;

            var line = buffer.Slice(0, buffer.Length);
        }
    }
}
