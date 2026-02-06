using Model.Game.Deck;

namespace UnitTests;

/// <summary>
/// Tests unitaires pour la classe Deck
/// </summary>
public class DeckTests
{
    /// <summary>
    /// Génère des données de test pour les tests de distribution de cartes
    /// </summary>
    public static IEnumerable<object[]> GetDealCardTestData()
    {
        // Tester avec différents nombres de cartes à distribuer
        yield return new object[] { 1 };    // Une seule carte
        yield return new object[] { 5 };    // Plusieurs cartes
        yield return new object[] { 52 };   // Toutes les cartes
    }

    /// <summary>
    /// Vérifie qu'un nouveau deck contient exactement 52 cartes
    /// </summary>
    [Fact]
    public void NouveauDeck_DevraitContenir52Cartes()
    {
        // Arrange
        var deck = new Deck();

        // Act
        int cardCount = deck.CardsRemaining;

        // Assert
        Assert.Equal(52, cardCount);
    }

    /// <summary>
    /// Vérifie que le deck contient toutes les cartes attendues
    /// </summary>
    [Fact]
    public void NouveauDeck_DevraitContenirToutesLesCartes()
    {
        // Arrange
        var deck = new Deck();
        var allCards = new HashSet<string>();

        // Act - Distribuer toutes les cartes
        while (deck.CardsRemaining > 0)
        {
            allCards.Add(deck.DealCard().ToString());
        }

        // Assert - Vérifier qu'il y a bien 52 cartes uniques
        Assert.Equal(52, allCards.Count);
    }

    /// <summary>
    /// Vérifie que la distribution d'une carte réduit le nombre de cartes restantes
    /// </summary>
    [Theory]
    [MemberData(nameof(GetDealCardTestData))]
    public void DistribuerCarte_DevraitReduireLesCartesRestantes(int cardsToDeal)
    {
        // Arrange
        var deck = new Deck();
        int initialCount = deck.CardsRemaining;

        // Act
        for (int i = 0; i < cardsToDeal; i++)
        {
            deck.DealCard();
        }

        // Assert
        Assert.Equal(initialCount - cardsToDeal, deck.CardsRemaining);
    }

    /// <summary>
    /// Vérifie que le mélange change l'ordre des cartes
    /// </summary>
    [Fact]
    public void Melanger_DevraitChangerLordreDesCartes()
    {
        // Arrange
        var deck1 = new Deck();
        var deck2 = new Deck();
        var initialOrder = deck1.ToString();

        // Act
        deck2.Shuffle();
        var shuffledOrder = deck2.ToString();

        // Assert
        Assert.NotEqual(initialOrder, shuffledOrder);
    }

    /// <summary>
    /// Vérifie que la réinitialisation du deck restaure toutes les cartes
    /// </summary>
    [Fact]
    public void Reinitialiser_DevraitRestaurerToutesLesCartes()
    {
        // Arrange
        var deck = new Deck();
        var initialCards = new List<string>();
        
        // Distribuer quelques cartes
        for (int i = 0; i < 10; i++)
        {
            initialCards.Add(deck.DealCard().ToString());
        }

        // Act
        deck.Reset();

        // Assert
        Assert.Equal(52, deck.CardsRemaining);
        
        // Vérifier que les cartes initiales sont de retour dans le deck
        var remainingCards = new List<string>();
        while (deck.CardsRemaining > 0)
        {
            remainingCards.Add(deck.DealCard().ToString());
        }
        
        foreach (var card in initialCards)
        {
            Assert.Contains(card, remainingCards);
        }
    }

    /// <summary>
    /// Vérifie que la distribution d'une carte depuis un deck vide lève une exception
    /// </summary>
    [Fact]
    public void DistribuerCarte_DeckVide_DevraitLancerException()
    {
        // Arrange
        var deck = new Deck();
        while (deck.CardsRemaining > 0)
        {
            deck.DealCard();
        }

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => deck.DealCard());
        Assert.Contains("vide", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Vérifie que le mélange ne perd ni ne duplique de cartes
    /// </summary>
    [Fact]
    public void Melanger_NeDevraitPasPerdreNiDupliquerDeCartes()
    {
        // Arrange
        var deck = new Deck();
        var beforeShuffle = GetAllCards(deck);
        
        // Act
        deck.Shuffle();
        var afterShuffle = GetAllCards(deck);

        // Assert
        Assert.Equal(52, afterShuffle.Count);
        Assert.True(beforeShuffle.SetEquals(afterShuffle));
    }

    /// <summary>
    /// Récupère toutes les cartes du deck sous forme de HashSet
    /// </summary>
    private static HashSet<string> GetAllCards(Deck deck)
    {
        var cards = new HashSet<string>();
        var tempDeck = new Deck(); // Crée une copie pour ne pas modifier le deck original
        
        while (tempDeck.CardsRemaining > 0)
        {
            cards.Add(tempDeck.DealCard().ToString());
        }
        
        return cards;
    }
}
