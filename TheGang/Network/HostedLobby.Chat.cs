namespace Network;

public sealed partial class HostedLobby
{
    private readonly List<ChatMessageSnapshot> _chatMessages = [];
    private readonly Dictionary<Guid,DateTimeOffset> _lastChat = [];
    private long _chatSequence;

    public GameSnapshot PostChat(string code,string sessionToken,ChatPostRequest request)
    {
        lock(_sync)
        {
            var player=EnsureMember(code,sessionToken);
            var game=_game??throw new InvalidOperationException("La partie n’a pas commencé.");
            var text=(request.Text??"").Trim();
            if(text.Length is 0 or >280 || text.Any(char.IsControl))
                throw new ArgumentException("Le message doit contenir de 1 à 280 caractères, sur une seule ligne.");
            var now=_clock.GetUtcNow();
            if(_lastChat.TryGetValue(player,out var last)&&now-last<TimeSpan.FromSeconds(1))
                throw new InvalidOperationException("Patientez une seconde avant le prochain message.");
            var name=game.Players.Single(p=>p.Id==player).Name;
            _chatMessages.Add(new(++_chatSequence,player,name,text,now));
            _lastChat[player]=now;
            if(_chatMessages.Count>80)_chatMessages.RemoveAt(0);
            return GameSnapshotFor(player);
        }
    }
}
