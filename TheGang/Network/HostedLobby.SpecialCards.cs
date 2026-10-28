using Model.Game;
namespace Network;
public sealed partial class HostedLobby
{
    public LobbySnapshot ChangeDifficulty(string code,string token,GameDifficulty difficulty)
    {
        lock(_sync)
        {
            var player=EnsureMember(code,token); EnsureHost(player);
            if (_migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing or HostMigrationStage.Redirecting } || _migrationSetup?.WasStarted==true)
                throw new InvalidOperationException("La difficulté ne peut pas changer pendant une relève.");
            _manager.ConfigureDifficulty(Code,player,difficulty); return Snapshot();
        }
    }
    internal static void ApplySpecialRequest(Game game,Guid player,GameActionRequest request)
    {
        switch(request.Action)
        {
            case GameAction.ChooseSpecialSetup:game.ChooseSetup(player,request.CardIndex);break;
            case GameAction.ProposeComplice:game.ProposeComplice(player,request.TargetId??player,request.RecipientId,request.Rank,request.CardIndex);break;
            case GameAction.VoteComplice:game.VoteComplice(player,request.ProposalId??Guid.Empty,request.Accepted);break;
            case GameAction.DiscardSpecial:game.DiscardSpecial(player,request.CardIndex);break;
            case GameAction.SubmitSpecialGuess:game.SubmitGuess(player,request.GuessKind??"",request.Rank);break;
        }
    }
    public static SpecialGameSnapshot SpecialSnapshotFor(Game game,Guid viewer) => new(game.Difficulty,game.ActiveSpecialCards.ToArray(),game.PermanentEffraction,
        game.CompliceUsed,game.SetupKind,game.SetupChosen(viewer),game.DiscardPlayer,game.ForbiddenDiscardIndex,
        game.PublicHints.ToArray(),game.PrivateHints(viewer).ToArray(),game.Proposal is {} p?new(p.Id,p.Target,p.Recipient,p.Rank,p.Accepted):null,
        game.GuessTarget,game.Guesses(viewer).ToDictionary(x=>x.Key,x=>x.Value),new(){["E04"]=game.GuessVotes("E04"),["E09"]=game.GuessVotes("E09")},
        Enumerable.Range(1,game.Players.Count).Where(game.IsTokenLocked).ToArray(),game.AlarmLimit)
        { FailureReason=game.SpecialFailureReason, ShownCards=game.ShownCards(viewer).Select(c=>new ShownCardSnapshot(c.SourceId,c.SourceName,CardSnapshotFor(c.Card))).ToArray() };
}
