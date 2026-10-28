using System.Windows;
using Network;

namespace View;

/// <summary>Affiche les trois chances d'obtenir l'or et les trois alarmes possibles.</summary>
public partial class MainWindow
{
    // Nombre de cartes déjà retournées localement et score attendu selon le serveur.
    private int _shownGoldCards;
    private int _shownAlarmCards;
    private int _targetGoldCards;
    private int _targetAlarmCards;

    /// <summary>Synchronise les six cartes avec le score autoritaire, sans rejouer les retournements.</summary>
    private void RenderScoreCards(GameSnapshot game)
    {
        var alarmLimit=game.Special?.AlarmLimit??3;
        if (GoldResultCardsPanel.Children.Count != 3 || AlarmResultCardsPanel.Children.Count != alarmLimit ||
            game.SuccessfulHeists < _shownGoldCards || game.FailedHeists < _shownAlarmCards)
            ResetScoreCards(alarmLimit);

        _targetGoldCards = Math.Clamp(game.SuccessfulHeists, 0, 3);
        _targetAlarmCards = Math.Clamp(game.FailedHeists, 0, alarmLimit);

        // Le débrief recouvre le tapis : retourner les cartes après sa fermeture.
        if (HeistOutcomeOverlay.Visibility == Visibility.Collapsed) FlipPendingScoreCards();
    }

    /// <summary>Retourne seulement les cartes nouvellement gagnées ou perdues.</summary>
    private void FlipPendingScoreCards()
    {
        for (var index = _shownGoldCards; index < _targetGoldCards; index++)
            ((ScoreCardSlot)GoldResultCardsPanel.Children[index]).Reveal(
                TimeSpan.FromMilliseconds((index - _shownGoldCards) * 180));
        for (var index = _shownAlarmCards; index < _targetAlarmCards; index++)
            ((ScoreCardSlot)AlarmResultCardsPanel.Children[index]).Reveal(
                TimeSpan.FromMilliseconds((index - _shownAlarmCards) * 180));
        _shownGoldCards = _targetGoldCards;
        _shownAlarmCards = _targetAlarmCards;
    }

    /// <summary>Remet les six cartes face cachée pour une nouvelle partie.</summary>
    private void ResetScoreCards(int alarmLimit=3)
    {
        GoldResultCardsPanel.Children.Clear();
        AlarmResultCardsPanel.Children.Clear();
        for (var index = 0; index < 3; index++)
        {
            GoldResultCardsPanel.Children.Add(ResultCardVisuals.Slot(gold: true));
            if(index<alarmLimit) AlarmResultCardsPanel.Children.Add(ResultCardVisuals.Slot(gold: false));
        }
        _shownGoldCards = _shownAlarmCards = _targetGoldCards = _targetAlarmCards = 0;
    }
}
