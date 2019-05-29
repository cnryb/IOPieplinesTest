using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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

                ThreadPool.QueueUserWorkItem(async q =>
                {
                    await ProcessHandshakeAsync(socket);
                });
            }

        }

        private static async Task ProcessHandshakeAsync(Socket socket)
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
            if (line.First.Span[0] == 5)
            {
                socket.Send(new byte[] { 5, 0 });
                //var s = await socket.();
                await ProcessProtocolAsync(socket);
            }
        }

        private static async Task ProcessProtocolAsync(Socket socket)
        {
            var pipe = new Pipe();
            const int minimumBufferSize = 1024;

            Memory<byte> memory = pipe.Writer.GetMemory(minimumBufferSize);
            int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);
            pipe.Writer.Advance(bytesRead);
            FlushResult r = await pipe.Writer.FlushAsync();

            ReadResult result = await pipe.Reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;
            var line = buffer.Slice(0, buffer.Length);
            var bs = line.ToArray();
            switch (bs[3])
            {
                case 0x03:
                    var domainLength = bs[4];
                    string domainName = System.Text.Encoding.ASCII.GetString(bs, 5, domainLength);
                    Console.WriteLine($"{domainName}");
                    var ipadderss = Dns.GetHostAddresses(domainName).FirstOrDefault();
                    Array.Reverse(bs, bytesRead - 2, 2);
                    var port = BitConverter.ToInt16(bs, bytesRead - 2);
                    Socket remote = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    remote.Connect(ipadderss, port);

                    List<byte> res = new List<byte>();
                    res.Add(0x05);
                    res.Add(0x00);
                    res.Add(0x00);
                    res.Add(0x01);
                    IPEndPoint localEP = (IPEndPoint)socket.LocalEndPoint;
                    res.AddRange(localEP.Address.MapToIPv4().GetAddressBytes());
                    res.AddRange(BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder(localEP.Port)));
                    socket.Send(res.ToArray());

                    Task.WaitAll(ReadLocalToServer(socket, remote), ReadServerToLocal(remote, socket));


                    break;

                default:
                    break;
            }
        }
        private static async Task ReadLocalToServer(Socket local, Socket server)
        {
            var pipe = new Pipe();
            const int minimumBufferSize = 10240;

            Memory<byte> memory = pipe.Writer.GetMemory(minimumBufferSize);
            int bytesRead = await local.ReceiveAsync(memory, SocketFlags.None);
            if (bytesRead <= 0) return;
            pipe.Writer.Advance(bytesRead);
            FlushResult r = await pipe.Writer.FlushAsync();

            ReadResult result = await pipe.Reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;
            var line = buffer.Slice(0, buffer.Length);
            var bs = line.ToArray();
            server.Send(bs);

            await ReadLocalToServer(local, server);
        }

        private static async Task ReadServerToLocal(Socket local, Socket server)
        {
            var pipe = new Pipe();
            const int minimumBufferSize = 10240;

            Memory<byte> memory = pipe.Writer.GetMemory(minimumBufferSize);
            int bytesRead = await local.ReceiveAsync(memory, SocketFlags.None);
            if (bytesRead <= 0) return;
            pipe.Writer.Advance(bytesRead);
            FlushResult r = await pipe.Writer.FlushAsync();

            ReadResult result = await pipe.Reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;
            var line = buffer.Slice(0, buffer.Length);
            var bs = line.ToArray();
            server.Send(bs);

            await ReadLocalToServer(local, server);

        }
    }
}
