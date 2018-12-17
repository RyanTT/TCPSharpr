﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TcpSharpr.MethodInteraction;
using TcpSharpr.Network;
using TcpSharpr.Network.Events;
using TcpSharpr.Threading;

namespace TcpSharpr {
    public class Client : INetworkSender {
        public CommandManager CommandManager { get; private set; }
        public IPEndPoint RemoteIpEndpoint { get; private set; }
        public bool ReconnectOnDisconnect { get; set; } = true;
        public bool IsConnected { get; private set; } = false;
        public NetworkClient NetworkClient { get; private set; }

        public event EventHandler<ConnectedEventArgs> OnNetworkClientConnected;
        public event EventHandler<DisconnectedEventArgs> OnNetworkClientDisconnected;

        private CancellationTokenSource _clientCancellationToken;
        private Task _clientWorkerTask;

        public Client(IPEndPoint ipEndpoint) {
            RemoteIpEndpoint = ipEndpoint;
            CommandManager = new CommandManager();
        }

        public async Task<bool> ConnectAsync() {
            _clientCancellationToken = new CancellationTokenSource();

            return await AttemptConnectAsync(false);
        }

        public void Disconnect() {
            _clientCancellationToken?.Cancel();
        }

        public async Task<NetworkMessage> SendAsync(string command, params object[] args) {
            return await NetworkClient.SendAsync(command, args);
        }

        public async Task<NetworkRequest> SendRequestAsync(string command, params object[] args) {
            return await NetworkClient.SendRequestAsync(command, args);
        }

        internal async Task<bool> AttemptConnectAsync(bool isDedicatedContext) {
            try {
                if (_clientCancellationToken != null && _clientCancellationToken.IsCancellationRequested) {
                    return false;
                }

                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(RemoteIpEndpoint);

                NetworkClient = new NetworkClient(socket, _clientCancellationToken, CommandManager);
                NetworkClient.OnDisconnected += NetworkClient_OnDisconnected;

                if (!isDedicatedContext) {
#pragma warning disable CS4014
                    Task.Run(() => OnNetworkClientConnected?.Invoke(this, new ConnectedEventArgs(RemoteIpEndpoint)));
#pragma warning restore CS4014

                    _clientWorkerTask = Task.Run(async () => await NetworkClient.ReceiveAsync());
                } else {
                    await NetworkClient.ReceiveAsync();
                }

                return IsConnected = true;
            } catch (SocketException) {
                return false;
            }
        }

        private async void NetworkClient_OnDisconnected(object sender, Network.Events.DisconnectedEventArgs e) {
#pragma warning disable CS4014
            Task.Run(() => OnNetworkClientDisconnected(this, new DisconnectedEventArgs()));
#pragma warning restore CS4014

            IsConnected = false;

            try {
                while (!await AttemptConnectAsync(true)) await Task.Delay(1000).WithCancellation(_clientCancellationToken.Token);
            } catch (OperationCanceledException ex) {
                // Catch, consume, adapt, overcome
            }
        }
    }
}
