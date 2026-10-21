using Model.Game;
using Model.Game.Tokens.TokenEnum;
using ViewModel;

namespace UnitTests;

public class GameViewModelTests
{
    [Fact]
    public void ViewerSeesEveryonesSignalsButOnlyOwnAndRevealedHands()
    {
        var game = new Game(new Random(11));
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        game.AddPlayer("Chloe");
        var viewModel = new GameViewModel(game, game.Players[0].Id);

        viewModel.StartGame();
        Assert.Equal(2, viewModel.Players[0].Hand.Count);
        Assert.Empty(viewModel.Players[1].Hand);
        Assert.Empty(viewModel.Players[2].Hand);

        AssignAndConfirmInPlayerOrder(viewModel);
        Assert.True(viewModel.AllPlayersHaveTokens);
        viewModel.RevealFlop();
        Assert.Equal(new[] { 1, 2, 3 },
            viewModel.Players.Select(player => player.Signals[TokenColors.White].Value));
        AssignAndConfirmInPlayerOrder(viewModel);
        viewModel.RevealTurn();
        AssignAndConfirmInPlayerOrder(viewModel);
        viewModel.RevealRiver();

        viewModel.TakeToken(game.Players[1].Id, 1);
        viewModel.TakeToken(game.Players[2].Id, 2);
        viewModel.TakeToken(game.Players[0].Id, 3);
        ConfirmAll(viewModel);
        Assert.True(viewModel.AllChoicesConfirmed);
        Assert.All(viewModel.Players, player => Assert.Equal(4, player.Signals.Count));

        viewModel.GoToShowdown();
        Assert.Equal(game.Players[1].Id, viewModel.NextPlayerToRevealId);
        Assert.Empty(viewModel.Players[1].Hand);
        viewModel.RevealNextHand();
        Assert.Equal(2, viewModel.Players[1].Hand.Count);
        Assert.Empty(viewModel.Players[2].Hand);
    }

    private static void AssignAndConfirmInPlayerOrder(GameViewModel viewModel)
    {
        for (var i = 0; i < viewModel.Players.Count; i++)
            viewModel.TakeToken(viewModel.Players[i].Id, i + 1);
        ConfirmAll(viewModel);
    }

    private static void ConfirmAll(GameViewModel viewModel)
    {
        foreach (var player in viewModel.Players)
            viewModel.ConfirmChoice(player.Id);
    }
}
