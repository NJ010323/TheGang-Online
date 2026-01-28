using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Model.Networking;

namespace ViewModel;

public sealed class PeerToPeerTestViewModel : BaseVm, IDisposable
{
    private readonly ObservableCollection<string> _peers = new();
    private readonly ReadOnlyObservableCollection<string> _readonlyPeers;
    private readonly SynchronizationContext? _uiContext;
    private CancellationTokenSource? _listenerCts;
    private TcpListener? _listener;
    private TcpClient? _clientConnection;
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
    private string _statusMessage = "Prêt à tester.";

    public PeerToPeerTestViewModel()
    {
        _readonlyPeers = new ReadOnlyObservableCollection<string>(_peers);
        _uiContext = SynchronizationContext.Current;

        StartHostCommand = new AsyncRelayCommand(StartHostAsync, () => !IsHosting);
        StopHostCommand = new RelayCommand(StopHost, () => IsHosting);
        JoinCommand = new AsyncRelayCommand(JoinSessionAsync, () => !IsHosting && !IsConnected);
        DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
    }

    public ReadOnlyObservableCollection<string> Peers => _readonlyPeers;

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
        IsHosting = false;
        _peers.Clear();
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
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
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
            StartClientMonitor(stream);
            PostToUi(() =>
            {
                IsConnected = true;
                UpdatePeersFromSessionInfo(sessionInfo);
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
}
