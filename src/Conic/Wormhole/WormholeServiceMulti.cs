﻿using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NamedPipeWrapper;
using NLog;

namespace Conic.Wormhole
{
    public class WormholeServiceMulti
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        //private static readonly Logger _log = LogManager.CreateNullLogger();
        private readonly string _pipeName;


        public WormholeServiceMulti(string pipeName)
        {
            _pipeName = pipeName;
        }

        public void StartServer()
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            token.ThrowIfCancellationRequested();


            var task = Task.Run(() =>
            {
                _log.Debug($"Start pipe server. Pipe name: '{_pipeName}'");
                var server = new NamedPipeServer<string>(_pipeName);

                Action end = () =>
                {
                    server.ClientConnected += OnClientConnected;
                    server.ClientDisconnected += OnClientDisconnected;
                    server.ClientMessage += OnClientMessage;
                };


                server.ClientConnected += OnClientConnected;
                server.ClientDisconnected += OnClientDisconnected;
                server.ClientMessage += OnClientMessage;

                try
                {
                    server.Start();
                }
                catch (Exception e)
                {
                    _log.Error(e);
                    end();
                    return;
                }
                while (true)
                {
                    try
                    {
                        if (token.IsCancellationRequested)
                        {
                            _log.Debug("Another thread decided to cancel");
                            break;
                        }
                        var r = Read();
                        if (r.ImputLength == 0) break;
                        server.PushMessage(r.Value);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e);
                    }
                }
                server.Stop();
                end();
            }, token);

            task.Wait(token);
            _log.Debug($"Stop pipe server. Pipe name: {_pipeName}");
        }

        private void OnClientMessage(NamedPipeConnection<string, string> connection, string message)
        {
            _log.Trace(
                $"OnClientMessage; ConnectionName: {connection.Name}, ConnectionId: {connection.Id}, MessageLength: {message.Length}");
            Write(message);
        }


        private void OnClientDisconnected(NamedPipeConnection<string, string> connection)
        {
            _log.Trace($"Client {connection.Id} is now disconnected!");
        }

        private void OnClientConnected(NamedPipeConnection<string, string> conn)
        {
            _log.Debug($"Client {conn.Id} is now connected!");
        }

        /// <summary>
        /// Write message to standard output
        /// Remember: First 4 bytes contains message length
        /// </summary>
        /// <param name="message"></param>
        private void Write(string message)
        {
            _log.Trace($"Begin write to standardOutput message; Length: {message.Length}");
            var buff = Encoding.UTF8.GetBytes(message);
            using (var stdout = Console.OpenStandardOutput())
            {
                stdout.Write(BitConverter.GetBytes(buff.Length), 0, 4); //Write the length
                stdout.Write(buff, 0, buff.Length); //Write the message
                stdout.Flush();
            }
            _log.Trace("End write to standardOutput message");
        }

        /// <summary>
        /// Read  message from standard input
        /// Remember: First 4 bytes contains message length
        /// </summary>
        private ReadResult Read()
        {
            _log.Trace("Begin read from StandardInput");
            int length;
            string input;
            using (var stdin = Console.OpenStandardInput())
            {
                var buff = new byte[4];
                stdin.Read(buff, 0, 4);
                length = BitConverter.ToInt32(buff, 0);
                buff = new byte[length];
                stdin.Read(buff, 0, length);
                input = Encoding.UTF8.GetString(buff);
                _log.Trace($"End read from StandardInput. Message length: {input.Length}");
            }
            return new ReadResult(length, input);
        }
    }
}