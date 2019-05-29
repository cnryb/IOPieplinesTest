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
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.SetMinThreads(100, 100);
            Console.WriteLine("Hello World!");
            var listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(new IPEndPoint(IPAddress.Loopback, 1081));
            listenSocket.Listen(120);
            Console.WriteLine();
            while (true)
            {
                var socket = await listenSocket.AcceptAsync();

                ThreadPool.QueueUserWorkItem(async q =>
                {
                    try
                    {
                        await ProcessHandshakeAsync(socket);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
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

                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
                    var rec = remote.ConnectAsync(ipadderss, port);

                    var completedTask = await Task.WhenAny(timeoutTask, rec);
                    if (completedTask == timeoutTask)
                    {
                        socket.Send(new byte[] { 5, 4 });
                        return;
                    }

                    List<byte> res = new List<byte>();
                    res.Add(0x05);
                    res.Add(0x00);
                    res.Add(0x00);
                    res.Add(0x01);
                    IPEndPoint localEP = (IPEndPoint)socket.LocalEndPoint;
                    res.AddRange(localEP.Address.MapToIPv4().GetAddressBytes());
                    res.AddRange(BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder(localEP.Port)));
                    socket.Send(res.ToArray());

                    try
                    {
                        Task.WaitAny(ReplayToServer(socket, remote), ReplayToLcoal(remote, socket));
                    }
                    catch { }


                    break;

                default:
                    break;
            }
        }

        static void Close(Socket socket)
        {
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
            socket.Dispose();
        }

        private static async Task ReplayToServer(Socket local, Socket server)
        {
            const int minimumBufferSize = 2048;

            while (true)
            {
                var pipe = new Pipe();
                try
                {
                    Memory<byte> memory = pipe.Writer.GetMemory(minimumBufferSize);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
                    var rec = local.ReceiveAsync(memory, SocketFlags.None).AsTask();

                    var completedTask = await Task.WhenAny(timeoutTask, rec);
                    if (completedTask == timeoutTask)
                    {
                        Close(local);
                        Console.WriteLine("                     local   Close");
                        return;
                    }
                    int bytesRead = rec.Result;
                    if (bytesRead <= 0)
                    {
                        Close(local);
                        Console.WriteLine("                     local   Close");
                        return;
                    }
                    pipe.Writer.Advance(bytesRead);
                    FlushResult r = await pipe.Writer.FlushAsync();

                    ReadResult result = await pipe.Reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    var line = buffer.Slice(0, buffer.Length);
                    var bs = line.ToArray();

                    await server.SendAsync(buffer.First, SocketFlags.None);
                }
                catch (Exception ex)
                {
                    Close(server);
                    Console.WriteLine("                     server  Close");
                    return;
                }
            }
        }

        private static async Task ReplayToLcoal(Socket server, Socket local)
        {
            const int minimumBufferSize = 2048;
            while (true)
            {
                var pipe = new Pipe();
                try
                {
                    Memory<byte> memory = pipe.Writer.GetMemory(minimumBufferSize);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
                    var rec = server.ReceiveAsync(memory, SocketFlags.None).AsTask();

                    var completedTask = await Task.WhenAny(timeoutTask, rec);
                    if (completedTask == timeoutTask)
                    {
                        Close(server);
                        Console.WriteLine("                     server   Close");
                        return;
                    }
                    int bytesRead = rec.Result;

                    if (bytesRead <= 0)
                    {
                        Close(server);
                        Console.WriteLine("                     server  Close");
                        return;
                    }
                    pipe.Writer.Advance(bytesRead);
                    FlushResult r = await pipe.Writer.FlushAsync();

                    ReadResult result = await pipe.Reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    await local.SendAsync(buffer.First, SocketFlags.None);
                }
                catch (Exception ex)
                {
                    Close(local);
                    Console.WriteLine("                     local   Close");
                    return;
                }

            }
        }

    }
}
