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
    // Connexion de contrôle utilisée par l'hôte pour recevoir les demandes JOIN.
    private TcpClient? _relayHostControlClient;
    private CancellationTokenSource? _relayHostCts;
    private PeerToPeerSession? _session;
    private bool _isHosting;
    private bool _isConnected;
    // Active le mode relais (utile quand les joueurs ne sont pas sur le même réseau).
    private bool _useRelay;
    private string _hostDisplayName = "Host";
    private string _joinDisplayName = "Guest";
    private string _joinAddress = "127.0.0.1";
    private int _hostPort = 7777;
    private int _joinPort = 7777;
    private int _maxPeers = 2;
    // Paramètres du relais.
    private string _relayAddress = "localhost";
    private int _relayPort = 9000;
    private string _relaySessionCode = "GANG-SESSION";
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

    public bool UseRelay
    {
        get => _useRelay;
        set
        {
            if (_useRelay == value)
            {
                return;
            }

            _useRelay = value;
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

    public string RelayAddress
    {
        get => _relayAddress;
        set
        {
            if (_relayAddress == value)
            {
                return;
            }

            _relayAddress = value;
            OnPropertyChanged();
        }
    }

    public int RelayPort
    {
        get => _relayPort;
        set
        {
            if (_relayPort == value)
            {
                return;
            }

            _relayPort = value;
            OnPropertyChanged();
        }
    }

    public string RelaySessionCode
    {
        get => _relaySessionCode;
        set
        {
            if (_relaySessionCode == value)
            {
                return;
            }

            _relaySessionCode = value;
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

        if (UseRelay)
        {
            // En mode relais, l'hôte ouvre une connexion de contrôle vers le serveur.
            _relayHostCts = new CancellationTokenSource();
            _ = Task.Run(() => StartRelayHostAsync(_relayHostCts.Token));
        }
        else
        {
            // En mode direct, on écoute localement sur un port TCP.
            _listenerCts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, HostPort);
            _listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_listenerCts.Token));
        }

        IsHosting = true;

        StatusMessage = UseRelay
            ? $"Hôte lancé via relais {RelayAddress}:{RelayPort} (code: {RelaySessionCode})."
            : $"Hôte lancé sur le port {HostPort}.";
        return Task.CompletedTask;
    }

    private void StopHost()
    {
        StopHost("Hôte arrêté.");
    }

    private void StopHost(string statusMessage)
    {
        _listenerCts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _listenerCts = null;
        _relayHostCts?.Cancel();
        _relayHostCts?.Dispose();
        _relayHostCts = null;
        _relayHostControlClient?.Close();
        _relayHostControlClient = null;
        _session = null;
        IsHosting = false;
        _peers.Clear();
        StatusMessage = statusMessage;
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
            if (UseRelay)
            {
                // En mode relais, on se connecte au serveur public plutôt qu'à l'hôte directement.
                await client.ConnectAsync(RelayAddress.Trim(), RelayPort).ConfigureAwait(false);
            }
            else
            {
                await client.ConnectAsync(JoinAddress.Trim(), JoinPort).ConfigureAwait(false);
            }
            var stream = client.GetStream();
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            if (UseRelay)
            {
                // Handshake relais: JOIN -> WAIT -> READY.
                await writer.WriteLineAsync($"JOIN {RelaySessionCode.Trim()}").ConfigureAwait(false);
                var relayResponse = await reader.ReadLineAsync().ConfigureAwait(false);
                if (relayResponse is null)
                {
                    StatusMessage = "Le relais a fermé la connexion.";
                    client.Close();
                    return;
                }

                if (relayResponse.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = relayResponse.Replace("ERROR:", "Erreur: ", StringComparison.OrdinalIgnoreCase);
                    client.Close();
                    return;
                }

                if (!relayResponse.Equals("READY", StringComparison.OrdinalIgnoreCase))
                {
                    relayResponse = await reader.ReadLineAsync().ConfigureAwait(false);
                }

                if (!string.Equals(relayResponse, "READY", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "Le relais n'a pas pu établir la connexion.";
                    client.Close();
                    return;
                }
            }

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
            StatusMessage = UseRelay
                ? "Impossible de se connecter au relais. Vérifiez l'adresse, le port et le code."
                : "Impossible de se connecter à l'hôte. Vérifiez l'adresse et le port.";
        }
    }

    private async Task StartRelayHostAsync(CancellationToken token)
    {
        try
        {
            // Connexion de contrôle de l'hôte vers le relais.
            var client = new TcpClient();
            await client.ConnectAsync(RelayAddress.Trim(), RelayPort).ConfigureAwait(false);
            _relayHostControlClient = client;

            var stream = client.GetStream();
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync($"HOST {RelaySessionCode.Trim()}").ConfigureAwait(false);
            var response = await reader.ReadLineAsync().ConfigureAwait(false);
            if (response is null)
            {
                PostToUi(() =>
                {
                    StopHost("Connexion au relais interrompue.");
                });
                return;
            }

            if (response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                PostToUi(() =>
                {
                    StopHost(response.Replace("ERROR:", "Erreur: ", StringComparison.OrdinalIgnoreCase));
                });
                return;
            }

            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
                {
                    var tokenValue = line["CONNECT ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(tokenValue))
                    {
                        // Chaque invité déclenche une connexion DATA dédiée.
                        _ = Task.Run(() => AcceptRelayClientAsync(tokenValue, token), token);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
            PostToUi(() => StopHost("Impossible de joindre le relais."));
        }
    }

    private async Task AcceptRelayClientAsync(string relayToken, CancellationToken token)
    {
        try
        {
            // Connexion DATA dédiée pour un invité spécifique.
            var client = new TcpClient();
            await client.ConnectAsync(RelayAddress.Trim(), RelayPort).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync($"DATA {relayToken}").ConfigureAwait(false);
            await HandleClientAsync(client, token).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            PostToUi(() => StatusMessage = "Le relais n'a pas pu établir une connexion client.");
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

        if (!UseRelay && !IsValidPort(HostPort))
        {
            message = "Le port hôte doit être compris entre 1 et 65535.";
            return false;
        }

        if (UseRelay && !ValidateRelayInputs(out message))
        {
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

        if (!UseRelay && string.IsNullOrWhiteSpace(JoinAddress))
        {
            message = "L'adresse de l'hôte est requise.";
            return false;
        }

        if (!UseRelay && !IsValidPort(JoinPort))
        {
            message = "Le port de connexion doit être compris entre 1 et 65535.";
            return false;
        }

        if (UseRelay && !ValidateRelayInputs(out message))
        {
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool ValidateRelayInputs(out string message)
    {
        if (string.IsNullOrWhiteSpace(RelayAddress))
        {
            message = "L'adresse du relais est requise.";
            return false;
        }

        if (!IsValidPort(RelayPort))
        {
            message = "Le port du relais doit être compris entre 1 et 65535.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RelaySessionCode))
        {
            message = "Le code de session relais est requis.";
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
