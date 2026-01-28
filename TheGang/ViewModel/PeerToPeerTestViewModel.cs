using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Model.Networking;

namespace ViewModel;

public sealed class PeerToPeerTestViewModel : BaseVm, IDisposable
{
    private readonly ObservableCollection<string> _peers = new();
    private readonly ObservableCollection<string> _chatMessages = new();
    private readonly ReadOnlyObservableCollection<string> _readonlyPeers;
    private readonly ReadOnlyObservableCollection<string> _readonlyChatMessages;
    private readonly SynchronizationContext? _uiContext;
    private readonly object _clientSync = new();
    private readonly List<ClientConnection> _clientConnections = new();
    private CancellationTokenSource? _listenerCts;
    private TcpListener? _listener;
    private TcpClient? _clientConnection;
    private StreamWriter? _clientWriter;
    private CancellationTokenSource? _clientMonitorCts;
    private PeerToPeerSession? _session;
    private bool _isHosting;
    private bool _isConnected;
    private string _hostDisplayName = "Host";
    private string _joinDisplayName = "Guest";
    private string _joinAddress = "127.0.0.1";
    private int _hostPort = 7777;
    private int _joinPort = 7777;
    private int _maxPeers = 2;
    private string _chatInput = string.Empty;
    private string _statusMessage = "Prêt à tester.";

    public PeerToPeerTestViewModel()
    {
        _readonlyPeers = new ReadOnlyObservableCollection<string>(_peers);
        _readonlyChatMessages = new ReadOnlyObservableCollection<string>(_chatMessages);
        _uiContext = SynchronizationContext.Current;

        StartHostCommand = new AsyncRelayCommand(StartHostAsync, () => !IsHosting);
        StopHostCommand = new RelayCommand(StopHost, () => IsHosting);
        JoinCommand = new AsyncRelayCommand(JoinSessionAsync, () => !IsHosting && !IsConnected);
        DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
        SendChatCommand = new AsyncRelayCommand(SendChatAsync, CanSendChat);
    }

    public ReadOnlyObservableCollection<string> Peers => _readonlyPeers;

    public ReadOnlyObservableCollection<string> ChatMessages => _readonlyChatMessages;

    public string HostDisplayName
    {
        get => _hostDisplayName;
        set
        {
            if (_hostDisplayName == value)
            {
                return;
            }

            _hostDisplayName = value;
            OnPropertyChanged();
        }
    }

    public string JoinDisplayName
    {
        get => _joinDisplayName;
        set
        {
            if (_joinDisplayName == value)
            {
                return;
            }

            _joinDisplayName = value;
            OnPropertyChanged();
        }
    }

    public string JoinAddress
    {
        get => _joinAddress;
        set
        {
            if (_joinAddress == value)
            {
                return;
            }

            _joinAddress = value;
            OnPropertyChanged();
        }
    }

    public int HostPort
    {
        get => _hostPort;
        set
        {
            if (_hostPort == value)
            {
                return;
            }

            _hostPort = value;
            OnPropertyChanged();
        }
    }

    public int JoinPort
    {
        get => _joinPort;
        set
        {
            if (_joinPort == value)
            {
                return;
            }

            _joinPort = value;
            OnPropertyChanged();
        }
    }

    public int MaxPeers
    {
        get => _maxPeers;
        set
        {
            if (_maxPeers == value)
            {
                return;
            }

            _maxPeers = value;
            OnPropertyChanged();
        }
    }

    public string ChatInput
    {
        get => _chatInput;
        set
        {
            if (_chatInput == value)
            {
                return;
            }

            _chatInput = value;
            OnPropertyChanged();
            SendChatCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsHosting
    {
        get => _isHosting;
        private set
        {
            if (_isHosting == value)
            {
                return;
            }

            _isHosting = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand StartHostCommand { get; }

    public RelayCommand StopHostCommand { get; }

    public AsyncRelayCommand JoinCommand { get; }

    public RelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand SendChatCommand { get; }

    public void Dispose()
    {
        StopHost();
        CloseClientConnection();
    }

    /// <summary>
    /// Informe les commandes que l'état a changé.
    /// </summary>
    private void RaiseCommandStates()
    {
        StartHostCommand.RaiseCanExecuteChanged();
        StopHostCommand.RaiseCanExecuteChanged();
        JoinCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        SendChatCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Démarre l'hôte après validation des paramètres.
    /// </summary>
    private Task StartHostAsync()
    {
        if (!ValidateHostInputs(out var validationMessage))
        {
            StatusMessage = validationMessage;
            return Task.CompletedTask;
        }

        _peers.Clear();
        _chatMessages.Clear();
        _session = PeerToPeerSession.CreateHost(HostDisplayName.Trim(), MaxPeers);
        _peers.Add(PeerToPeerSessionProtocol.FormatHostDisplayName(_session.Host.DisplayName));

        _listenerCts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, HostPort);
        _listener.Start();
        IsHosting = true;

        StatusMessage = $"Hôte lancé sur le port {HostPort}.";

        _ = Task.Run(() => AcceptLoopAsync(_listenerCts.Token));
        return Task.CompletedTask;
    }

    private void StopHost()
    {
        _listenerCts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _listenerCts = null;
        _session = null;
        CloseHostConnections();
        IsHosting = false;
        _peers.Clear();
        _chatMessages.Clear();
        StatusMessage = "Hôte arrêté.";
    }

    /// <summary>
    /// Accepte les connexions entrantes tant que l'écoute est active.
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null || _session is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                client?.Close();
                break;
            }
            catch (ObjectDisposedException)
            {
                client?.Close();
                break;
            }
        }
    }

    /// <summary>
    /// Gère un client et surveille sa déconnexion.
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var displayName = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                await writer.WriteLineAsync("ERROR:Nom invalide").ConfigureAwait(false);
                return;
            }

            if (_session is null)
            {
                await writer.WriteLineAsync("ERROR:Session indisponible").ConfigureAwait(false);
                return;
            }

            if (!_session.TryJoin(displayName, out var newPeer, out var errorMessage))
            {
                await writer.WriteLineAsync($"ERROR:{errorMessage}").ConfigureAwait(false);
                return;
            }

            var joinedPeer = newPeer!;
            var connection = new ClientConnection(joinedPeer.DisplayName, client, writer);
            AddClientConnection(connection);
            await writer.WriteLineAsync(PeerToPeerSessionProtocol.BuildSessionInfoMessage(_session)).ConfigureAwait(false);
            PostToUi(() =>
            {
                _peers.Add(joinedPeer.DisplayName);
                StatusMessage = $"{joinedPeer.DisplayName} a rejoint la session.";
            });

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (PeerToPeerSessionProtocol.TryParseChatMessage(line, out _, out var message))
                    {
                        await HandleChatMessageAsync(connection, joinedPeer.DisplayName, message).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                RemoveClientConnection(connection);
                if (_session?.TryLeave(joinedPeer.Id, out _) == true)
                {
                    PostToUi(() =>
                    {
                        _peers.Remove(joinedPeer.DisplayName);
                        StatusMessage = $"{joinedPeer.DisplayName} s'est déconnecté.";
                    });
                }
            }
        }
    }

    /// <summary>
    /// Se connecte à un hôte et récupère l'état de la session.
    /// </summary>
    private async Task JoinSessionAsync()
    {
        if (!ValidateJoinInputs(out var validationMessage))
        {
            StatusMessage = validationMessage;
            return;
        }

        try
        {
            CloseClientConnection();

            var client = new TcpClient();
            await client.ConnectAsync(JoinAddress.Trim(), JoinPort).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync(JoinDisplayName.Trim()).ConfigureAwait(false);
            var response = await reader.ReadLineAsync().ConfigureAwait(false);

            if (response is null)
            {
                StatusMessage = "Connexion interrompue par l'hôte.";
                client.Close();
                return;
            }

            if (response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = response.Replace("ERROR:", "Erreur: ", StringComparison.OrdinalIgnoreCase);
                client.Close();
                return;
            }

            if (!PeerToPeerSessionProtocol.TryParseSessionInfo(response, out var sessionInfo))
            {
                StatusMessage = "Connexion acceptée, mais les détails de la session sont invalides.";
                client.Close();
                return;
            }

            _clientConnection = client;
            _clientWriter = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            StartClientMonitor(stream);
            PostToUi(() =>
            {
                IsConnected = true;
                UpdatePeersFromSessionInfo(sessionInfo);
                _chatMessages.Clear();
                StatusMessage = $"Connecté à {sessionInfo.HostName} ({sessionInfo.PeerCount} joueur(s)) : {sessionInfo.PeerList}.";
            });
        }
        catch (SocketException)
        {
            StatusMessage = "Impossible de se connecter à l'hôte. Vérifiez l'adresse et le port.";
        }
    }

    /// <summary>
    /// Déconnecte le client local.
    /// </summary>
    private void Disconnect()
    {
        CloseClientConnection();
        PostToUi(() =>
        {
            IsConnected = false;
            _peers.Clear();
            _chatMessages.Clear();
            StatusMessage = "Déconnecté.";
        });
    }

    private bool ValidateHostInputs(out string message)
    {
        if (string.IsNullOrWhiteSpace(HostDisplayName))
        {
            message = "Le nom de l'hôte est requis.";
            return false;
        }

        if (MaxPeers < 2)
        {
            message = "Le nombre maximal de joueurs doit être au moins 2.";
            return false;
        }

        if (!IsValidPort(HostPort))
        {
            message = "Le port hôte doit être compris entre 1 et 65535.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool ValidateJoinInputs(out string message)
    {
        if (string.IsNullOrWhiteSpace(JoinDisplayName))
        {
            message = "Le nom du joueur est requis.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(JoinAddress))
        {
            message = "L'adresse de l'hôte est requise.";
            return false;
        }

        if (!IsValidPort(JoinPort))
        {
            message = "Le port de connexion doit être compris entre 1 et 65535.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool IsValidPort(int port) => port is > 0 and <= 65535;

    /// <summary>
    /// Poste une action sur le thread UI si nécessaire.
    /// </summary>
    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    /// <summary>
    /// Ferme proprement la connexion client locale.
    /// </summary>
    private void CloseClientConnection()
    {
        _clientMonitorCts?.Cancel();
        _clientMonitorCts?.Dispose();
        _clientMonitorCts = null;
        _clientWriter?.Dispose();
        _clientWriter = null;
        _clientConnection?.Close();
        _clientConnection = null;
    }

    /// <summary>
    /// Surveille la connexion client afin de détecter la déconnexion côté hôte.
    /// </summary>
    private void StartClientMonitor(NetworkStream stream)
    {
        _clientMonitorCts?.Cancel();
        _clientMonitorCts?.Dispose();
        _clientMonitorCts = new CancellationTokenSource();

        var token = _clientMonitorCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (PeerToPeerSessionProtocol.TryParseChatMessage(line, out var sender, out var message))
                    {
                        AddChatMessage(sender, message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (token.IsCancellationRequested)
                {
                }

                PostToUi(() =>
                {
                    IsConnected = false;
                    _peers.Clear();
                    _chatMessages.Clear();
                    StatusMessage = "Déconnecté.";
                });
            }
        }, token);
    }

    /// <summary>
    /// Met à jour la liste locale des pairs à partir des données reçues.
    /// </summary>
    private void UpdatePeersFromSessionInfo(PeerToPeerSessionProtocol.SessionSnapshot sessionInfo)
    {
        _peers.Clear();

        foreach (var name in sessionInfo.PeerNames)
        {
            _peers.Add(name.Equals(sessionInfo.HostName, StringComparison.OrdinalIgnoreCase)
                ? PeerToPeerSessionProtocol.FormatHostDisplayName(name)
                : name);
        }
    }

    private bool CanSendChat()
        => (IsHosting || IsConnected) && !string.IsNullOrWhiteSpace(ChatInput);

    private async Task SendChatAsync()
    {
        var message = ChatInput.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (IsHosting)
        {
            var sender = HostDisplayName.Trim();
            AddChatMessage(sender, message);
            await BroadcastChatMessageAsync(sender, message).ConfigureAwait(false);
        }
        else if (IsConnected && _clientWriter is not null)
        {
            try
            {
                var sender = JoinDisplayName.Trim();
                await _clientWriter.WriteLineAsync(PeerToPeerSessionProtocol.BuildChatMessage(sender, message))
                    .ConfigureAwait(false);
                AddChatMessage(sender, message);
            }
            catch (IOException)
            {
                StatusMessage = "Message non envoyé : connexion perdue.";
            }
        }

        PostToUi(() => ChatInput = string.Empty);
    }

    private async Task HandleChatMessageAsync(ClientConnection senderConnection, string sender, string message)
    {
        AddChatMessage(sender, message);
        await BroadcastChatMessageAsync(sender, message, senderConnection).ConfigureAwait(false);
    }

    private void AddChatMessage(string sender, string message)
    {
        var time = DateTime.Now.ToString("HH:mm");
        var safeMessage = message.ReplaceLineEndings(" ").Trim();
        var safeSender = sender.Trim();

        if (string.IsNullOrWhiteSpace(safeSender) || string.IsNullOrWhiteSpace(safeMessage))
        {
            return;
        }

        PostToUi(() => _chatMessages.Add($"[{time}] {safeSender} : {safeMessage}"));
    }

    private async Task BroadcastChatMessageAsync(
        string sender,
        string message,
        ClientConnection? excludedConnection = null)
    {
        List<ClientConnection> connections;
        lock (_clientSync)
        {
            connections = _clientConnections.ToList();
        }

        if (connections.Count == 0)
        {
            return;
        }

        var payload = PeerToPeerSessionProtocol.BuildChatMessage(sender, message);
        var toRemove = new List<ClientConnection>();

        foreach (var connection in connections)
        {
            if (excludedConnection is not null && ReferenceEquals(connection, excludedConnection))
            {
                continue;
            }

            try
            {
                await connection.Writer.WriteLineAsync(payload).ConfigureAwait(false);
            }
            catch (IOException)
            {
                toRemove.Add(connection);
            }
        }

        if (toRemove.Count == 0)
        {
            return;
        }

        lock (_clientSync)
        {
            foreach (var connection in toRemove)
            {
                _clientConnections.Remove(connection);
            }
        }
    }

    private void AddClientConnection(ClientConnection connection)
    {
        lock (_clientSync)
        {
            _clientConnections.Add(connection);
        }
    }

    private void RemoveClientConnection(ClientConnection connection)
    {
        lock (_clientSync)
        {
            _clientConnections.Remove(connection);
        }
    }

    private void CloseHostConnections()
    {
        lock (_clientSync)
        {
            foreach (var connection in _clientConnections)
            {
                connection.Client.Close();
            }

            _clientConnections.Clear();
        }
    }

    private sealed record ClientConnection(string DisplayName, TcpClient Client, StreamWriter Writer);
}
