using Model.Game.Card.CardEnum;
using Model.Game.Cards;

namespace UnitTests;

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
    public void Carte_QuandCreee_DevraitAvoirBonneCouleurEtValeur()
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
    public void Carte_ToString_DevraitRetournerBonneChainePourToutesLesCartes(CardColors suit, int rank, string expectedString)
    {
        // Arrange
        var card = new Card(suit, rank);

        // Act
        var result = card.ToString();

        // Assert
        Assert.Equal(expectedString, result);
    }

    /// <summary>
    /// Vérifie que les valeurs invalides sont refusées.
    /// </summary>
    [Theory]
    [InlineData(0)]   // En dessous de la plage valide
    [InlineData(14)]  // Au-dessus de la plage valide
    [InlineData(100)] // Bien au-dessus de la plage valide
    [InlineData(-1)]  // Valeur négative
    public void Carte_QuandCreeeAvecValeurInvalide_DevraitLancerException(int invalidRank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card(CardColors.Carreau, invalidRank));
    }

    [Fact]
    public void Carte_QuandCreeeAvecCouleurInvalide_DevraitLancerException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card((CardColors)99, 5));
    }

    /// <summary>
    /// Vérifie que deux cartes avec les mêmes valeurs mais des instances différentes
    /// ne sont pas considérées comme égales par défaut.
    /// </summary>
    [Fact]
    public void Equals_QuandCompareDeuxCartesAvecValeursIdentiques_DevraitRetournerFaux()
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
    public void GetHashCode_PourInstancesDifferentes_DevraitRetournerValeursDifferentes()
    {
        // Arrange
        var card1 = new Card(CardColors.Coeur, 1);
        var card2 = new Card(CardColors.Coeur, 1);

        // Act & Assert
        Assert.NotEqual(card1.GetHashCode(), card2.GetHashCode());
    }
}
