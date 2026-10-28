using Model.Game;
using Model.Game.Hand;
using Model.Game.Tokens.TokenEnum;
using Model.Players;

namespace UnitTests;

public class GameTests
{
    [Fact]
    public void StartGame_DealsTwoCardsAndWhiteTokens()
    {
        var game = CreateGame();

        game.StartGame();

        Assert.Equal(GamePhase.PreFlop, game.Phase);
        Assert.Equal(1, game.HeistNumber);
        Assert.Equal(1, game.CurrentRound);
        Assert.All(game.Players, player => Assert.Equal(2, player.Hand.Count));
        Assert.Empty(game.CommunityCards);
        Assert.Equal(new[] { 1, 2, 3 }, game.RoundTokens.Select(token => token.Value));
        Assert.All(game.RoundTokens, token => Assert.Equal(TokenColors.White, token.Color));
        Assert.Throws<InvalidOperationException>(game.StartGame);
    }

    [Fact]
    public void PlayerCountAndNamesAreValidated()
    {
        var game = new Game();
        Assert.Throws<InvalidOperationException>(game.StartGame);
        Assert.Throws<ArgumentException>(() => game.AddPlayer(" "));
        game.AddPlayer(" Alice ");
        Assert.Equal("Alice", game.Players[0].Name);
        Assert.Throws<InvalidOperationException>(() => game.AddPlayer("alice"));
        for (var i = 2; i <= 6; i++)
            game.AddPlayer($"Player {i}");
        Assert.Throws<InvalidOperationException>(() => game.AddPlayer("Player 7"));
        game.StartGame();
        Assert.Throws<InvalidOperationException>(() => game.AddPlayer("Late player"));
    }

    [Fact]
    public void TokenCannotBeReturnedOrReplacedButCanBeStolenByPlayerWithoutToken()
    {
        var game = CreateGame();
        game.StartGame();
        var first = game.Players[0];
        var second = game.Players[1];
        game.TakeToken(first.Id, 2);
        Assert.Throws<InvalidOperationException>(() => game.ReturnToken(first.Id));
        Assert.Throws<InvalidOperationException>(() => game.TakeToken(first.Id, 1));
        Assert.Equal(2, first.SignalToken!.Value);
        game.TakeToken(second.Id, 2);
        Assert.Null(first.SignalToken);
        Assert.False(first.Signals.ContainsKey(TokenColors.White));
        Assert.Equal(2, second.SignalToken!.Value);
        Assert.Throws<InvalidOperationException>(() => game.ReturnToken(second.Id));
        Assert.Equal(new[] { 1, 3 }, game.AvailableTokens.Select(token => token.Value));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.TakeToken(first.Id, 4));
        Assert.Throws<ArgumentException>(() => game.TakeToken(Guid.NewGuid(), 1));
        game.TakeToken(first.Id, 1);
        game.TakeToken(game.Players[2].Id, 3);
        game.ConfirmChoice(first.Id);
        Assert.Throws<InvalidOperationException>(() => game.ReturnToken(first.Id));
        Assert.Throws<InvalidOperationException>(() => game.TakeToken(first.Id, 2));
        Assert.True(first.IsChoiceConfirmed);
        Assert.True(game.AllPlayersHaveTokens);
        ConfirmAll(game);
        Assert.True(game.AllChoicesConfirmed);
        game.WithdrawConfirmation(first.Id);
        Assert.Throws<InvalidOperationException>(() => game.TakeToken(first.Id, 2));
    }

    [Fact]
    public void CardsAreRevealedOnlyAfterAllPlayersConfirmTheirChoices()
    {
        var game = CreateGame();
        game.StartGame();
        Assert.Throws<InvalidOperationException>(game.RevealFlop);
        Assert.Throws<InvalidOperationException>(() => game.ConfirmChoice(game.Players[0].Id));

        game.TakeToken(game.Players[0].Id, 1);
        Assert.False(game.AllPlayersHaveTokens);
        Assert.Throws<InvalidOperationException>(() => game.ConfirmChoice(game.Players[0].Id));
        Assert.All(game.Players, player => Assert.False(player.IsChoiceConfirmed));

        AssignTokensOnly(game);
        Assert.True(game.AllPlayersHaveTokens);
        Assert.Throws<InvalidOperationException>(game.RevealFlop);
        game.ConfirmChoice(game.Players[0].Id);
        Assert.Throws<InvalidOperationException>(() => game.TakeToken(game.Players[1].Id, 3));
        Assert.True(game.AllPlayersHaveTokens);
        Assert.True(game.Players[0].IsChoiceConfirmed);
        ConfirmAll(game);
        Assert.True(game.AllChoicesConfirmed);
        game.RevealFlop();
        Assert.Equal(3, game.CommunityCards.Count);
        Assert.Equal(GamePhase.Flop, game.Phase);
        Assert.All(game.Players, player =>
        {
            Assert.Null(player.SignalToken);
            Assert.Equal(TokenColors.White, player.Signals[TokenColors.White].Color);
        });
        Assert.Throws<InvalidOperationException>(game.RevealTurn);

        AssignTokensInPlayerOrder(game);
        game.RevealTurn();
        Assert.Equal(4, game.CommunityCards.Count);
        AssignTokensInPlayerOrder(game);
        game.RevealRiver();
        Assert.Equal(5, game.CommunityCards.Count);
        Assert.Throws<InvalidOperationException>(game.GoToShowdown);
        AssignTokensInPlayerOrder(game);
        game.GoToShowdown();
        Assert.Equal(GamePhase.Showdown, game.Phase);
        Assert.All(game.Players, player => Assert.Equal(4, player.Signals.Count));
    }

    [Fact]
    public void ShowdownRevealsHandsInAscendingRedTokenOrder()
    {
        var game = CreateGame();
        game.StartGame();
        AssignTokensInPlayerOrder(game);
        game.RevealFlop();
        AssignTokensInPlayerOrder(game);
        game.RevealTurn();
        AssignTokensInPlayerOrder(game);
        game.RevealRiver();

        game.TakeToken(game.Players[2].Id, 1);
        game.TakeToken(game.Players[0].Id, 2);
        game.TakeToken(game.Players[1].Id, 3);
        ConfirmAll(game);
        game.GoToShowdown();

        Assert.Throws<InvalidOperationException>(() => game.ResolveHeist());
        Assert.Throws<InvalidOperationException>(() => game.GetRanking());
        Assert.Equal(game.Players[2].Id, game.NextPlayerToRevealId);
        Assert.Empty(game.RevealedHands);

        var first = game.RevealNextHand();
        Assert.Equal(1, first.TokenValue);
        Assert.Equal(game.Players[2].Id, first.PlayerId);
        Assert.Equal(2, first.Cards.Count);
        Assert.Equal(2, game.RevealNextHand().TokenValue);
        Assert.Equal(3, game.RevealNextHand().TokenValue);
        Assert.Null(game.NextPlayerToRevealId);
        Assert.Equal(new[] { 1, 2, 3 }, game.RevealedHands.Select(hand => hand.TokenValue));
        Assert.Throws<InvalidOperationException>(() => game.RevealNextHand());
        Assert.Equal(3, game.GetRanking().Count);
        game.ResolveHeist();
    }

    [Fact]
    public void ThreeSuccessfulHeistsWinTheGame()
    {
        var game = CreateGame();
        game.StartGame();

        for (var heist = 1; heist <= 3; heist++)
        {
            Assert.True(CompleteHeist(game, shouldFail: false));
            Assert.Equal(heist, game.SuccessfulHeists);
            if (heist < 3)
            {
                Assert.Equal(GamePhase.HeistComplete, game.Phase);
                game.StartNextHeist();
                Assert.Equal(heist + 1, game.HeistNumber);
                Assert.Empty(game.CommunityCards);
                Assert.All(game.Players, player => Assert.Equal(2, player.Hand.Count));
            }
        }

        Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.Equal(GameResult.Won, game.Result);
        Assert.Throws<InvalidOperationException>(game.StartNextHeist);
        Assert.Throws<InvalidOperationException>(() => game.ResolveHeist());
    }

    [Fact]
    public void ThreeFailedHeistsLoseTheGame()
    {
        var game = CreateGame();
        game.StartGame();

        for (var heist = 1; heist <= 3; heist++)
        {
            Assert.False(CompleteHeist(game, shouldFail: true));
            Assert.Equal(heist, game.FailedHeists);
            if (heist < 3)
                game.StartNextHeist();
        }

        Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.Equal(GameResult.Lost, game.Result);
    }

    [Fact]
    public void MixedResultsCanReachAFifthHeist()
    {
        var game = CreateGame();
        game.StartGame();
        var outcomes = new[] { true, true, false, false, true };

        for (var i = 0; i < outcomes.Length; i++)
        {
            Assert.Equal(outcomes[i], CompleteHeist(game, shouldFail: !outcomes[i]));
            if (i < outcomes.Length - 1)
            {
                Assert.Equal(GameResult.InProgress, game.Result);
                game.StartNextHeist();
            }
        }

        Assert.Equal(5, game.HeistNumber);
        Assert.Equal(3, game.SuccessfulHeists);
        Assert.Equal(2, game.FailedHeists);
        Assert.Equal(GameResult.Won, game.Result);
    }

    private static Game CreateGame()
    {
        var game = new Game(new Random(42));
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        game.AddPlayer("Chloe");
        return game;
    }

    private static void AssignTokensInPlayerOrder(Game game)
    {
        AssignTokensOnly(game);
        ConfirmAll(game);
    }

    private static void AssignTokensOnly(Game game)
    {
        for (var i = 0; i < game.Players.Count; i++)
            game.TakeToken(game.Players[i].Id, i + 1);
    }

    private static void ConfirmAll(Game game)
    {
        foreach (var player in game.Players)
            game.ConfirmChoice(player.Id);
    }

    private static bool CompleteHeist(Game game, bool shouldFail)
    {
        AssignTokensInPlayerOrder(game);
        game.RevealFlop();
        AssignTokensInPlayerOrder(game);
        game.RevealTurn();
        AssignTokensInPlayerOrder(game);
        game.RevealRiver();

        var orderedPlayers = game.Players
            .OrderBy(player => PokerHandEvaluator.Evaluate(player.Hand, game.CommunityCards))
            .ToList();
        if (shouldFail)
            orderedPlayers.Reverse();

        for (var i = 0; i < orderedPlayers.Count; i++)
            game.TakeToken(orderedPlayers[i].Id, i + 1);

        ConfirmAll(game);
        game.GoToShowdown();
        foreach (var _ in game.Players)
            game.RevealNextHand();
        return game.ResolveHeist();
    }
}
