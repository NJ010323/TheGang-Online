using Model.Game.Factory.TokenFactory;
using Model.Game.Tokens.TokenEnum;

namespace UnitTests;

/// <summary>
/// Tests unitaires pour la fabrique de jetons.
/// </summary>
public class TokenFactoryTests
{
    [Fact]
    public void CreerJetons_DevraitRetournerJetonsAvecValeursCorrectes()
    {
        // Arrange
        var factory = new RoundTokenFactory(TokenColors.Yellow);

        // Act
        var tokens = factory.CreateTokens(3);

        // Assert
        Assert.Equal(3, tokens.Count);
        Assert.Collection(tokens,
            token => Assert.Equal(1, token.Value),
            token => Assert.Equal(2, token.Value),
            token => Assert.Equal(3, token.Value));
        Assert.All(tokens, token => Assert.Equal(TokenColors.Yellow, token.Color));
    }
}
