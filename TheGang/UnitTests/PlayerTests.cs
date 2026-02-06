using Model.Game.Card;
using Model.Game.Card.CardEnum;
using Model.Game.Cards;
using Model.Game.Tokens;
using Model.Game.Tokens.TokenEnum;
using Model.Players;

namespace UnitTests;

/// <summary>
/// Tests unitaires pour le joueur.
/// </summary>
public class PlayerTests
{
    [Fact]
    public void RecevoirCarte_DevraitAjouterLaCarteALaMain()
    {
        // Arrange
        var player = new Player("Alice");
        var card = new Card(CardColors.Carreau, 7);

        // Act
        player.ReceiveCard(card);

        // Assert
        Assert.Single(player.Hand);
        Assert.Equal(card, player.Hand[0]);
    }

    [Fact]
    public void AssignerJetonSignal_DevraitStockerLeJeton()
    {
        // Arrange
        var player = new Player("Alice");
        var token = new Token(1, TokenColors.White);

        // Act
        player.AssignSignalToken(token);

        // Assert
        Assert.NotNull(player.SignalToken);
        Assert.Equal(token, player.SignalToken);
    }

    [Fact]
    public void EffacerJetonSignal_DevraitRetirerLeJeton()
    {
        // Arrange
        var player = new Player("Alice");
        player.AssignSignalToken(new Token(1, TokenColors.White));

        // Act
        player.ClearSignalToken();

        // Assert
        Assert.Null(player.SignalToken);
    }
}
