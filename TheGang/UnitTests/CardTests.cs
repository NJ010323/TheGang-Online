

using Model.Game.Card;
using Model.Game.Card.CardEnum;

namespace TheGang.Tests;

/// <summary>
/// Tests unitaires pour la classe Card
/// </summary>
public class CardTests
{
    /// <summary>
    /// Génère toutes les combinaisons possibles de cartes (52 cartes)
    /// </summary>
    /// <returns>Une collection de tableaux d'objets contenant une carte et sa représentation attendue</returns>
    public static IEnumerable<object[]> GetAllPossibleCardsWithExpectedStrings()
    {
        foreach (CardColors suit in Enum.GetValues(typeof(CardColors)))
        {
            for (int rank = 1; rank <= 13; rank++)
            {
                string expectedString = GetExpectedCardString(suit, rank);
                yield return new object[] { suit, rank, expectedString };
            }
        }
    }

    /// <summary>
    /// Retourne la représentation en chaîne attendue pour une carte donnée
    /// </summary>
    private static string GetExpectedCardString(CardColors suit, int rank)
    {
        string rankStr = rank switch
        {
            1 => "As",
            11 => "Valet",
            12 => "Dame",
            13 => "Roi",
            _ => rank.ToString()
        };
        
        return $"{rankStr} de {suit}";
    }

    /// <summary>
    /// Vérifie que la création d'une carte avec une couleur et une valeur spécifiques
    /// initialise correctement les propriétés de la carte.
    /// </summary>
    [Fact]
    public void Card_WhenCreated_ShouldHaveCorrectSuitAndRank()
    {
        // Arrange - Préparation des données de test
        var expectedSuit = CardColors.Coeur;
        var expectedRank = 10;

        // Act - Exécution de l'action à tester
        var card = new Card(expectedSuit, expectedRank);

        // Assert - Vérification du résultat
        Assert.Equal(expectedSuit, card.Suit);
        Assert.Equal(expectedRank, card.Rank);
    }

    /// <summary>
    /// Vérifie que toutes les cartes possibles (52) sont correctement créées
    /// et que leur représentation en chaîne est correcte.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetAllPossibleCardsWithExpectedStrings))]
    public void Card_ToString_ShouldReturnCorrectStringForAllCards(CardColors suit, int rank, string expectedString)
    {
        // Arrange
        var card = new Card(suit, rank);

        // Act
        var result = card.ToString();

        // Assert
        Assert.Equal(expectedString, result);
    }

    /// <summary>
    /// Vérifie que la création d'une carte avec une valeur invalide ne génère pas d'exception.
    /// </summary>
    [Theory]
    [InlineData(0)]   // En dessous de la plage valide
    [InlineData(14)]  // Au-dessus de la plage valide
    [InlineData(100)] // Bien au-dessus de la plage valide
    [InlineData(-1)]  // Valeur négative
    public void Card_WhenCreatedWithInvalidRank_ShouldNotThrow(int invalidRank)
    {
        // Arrange & Act
        var exception = Record.Exception(() => new Card(CardColors.Carreau, invalidRank));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// Vérifie que la propriété Suit peut être modifiée après la création de la carte.
    /// </summary>
    [Fact]
    public void Suit_WhenModified_ShouldUpdateCardSuit()
    {
        // Arrange
        var card = new Card(CardColors.Pique, 5);
        var newSuit = CardColors.Coeur;

        // Act
        card.Suit = newSuit;

        // Assert
        Assert.Equal(newSuit, card.Suit);
    }

    /// <summary>
    /// Vérifie que la propriété Rank peut être modifiée après la création de la carte.
    /// </summary>
    [Fact]
    public void Rank_WhenModified_ShouldUpdateCardRank()
    {
        // Arrange
        var card = new Card(CardColors.Pique, 5);
        var newRank = 10;

        // Act
        card.Rank = newRank;

        // Assert
        Assert.Equal(newRank, card.Rank);
    }

    /// <summary>
    /// Vérifie que deux cartes avec les mêmes valeurs mais des instances différentes
    /// ne sont pas considérées comme égales par défaut.
    /// </summary>
    [Fact]
    public void Equals_WhenComparingTwoCardsWithSameValues_ShouldReturnFalse()
    {
        // Arrange
        var card1 = new Card(CardColors.Carreau, 7);
        var card2 = new Card(CardColors.Carreau, 7);

        // Act & Assert
        Assert.False(card1.Equals(card2));
        Assert.False(card1 == card2);
    }

    /// <summary>
    /// Vérifie que la méthode GetHashCode retourne des valeurs différentes
    /// pour des instances différentes.
    /// </summary>
    [Fact]
    public void GetHashCode_ForDifferentInstances_ShouldReturnDifferentValues()
    {
        // Arrange
        var card1 = new Card(CardColors.Coeur, 1);
        var card2 = new Card(CardColors.Coeur, 1);

        // Act & Assert
        Assert.NotEqual(card1.GetHashCode(), card2.GetHashCode());
    }
}
