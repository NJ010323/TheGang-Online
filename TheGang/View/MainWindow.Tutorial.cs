using System.Windows;
using System.Windows.Media;
using Model.Game;
using Network;

namespace View;

/// <summary>Le mentor pilote un moteur local ; rendu et gestes sont ceux du plateau réseau.</summary>
public partial class MainWindow
{
    private TutorialSession? _tutorial;
    private Guid _tutorialGameId;
    private readonly List<GameEventSnapshot> _tutorialEvents = [];
    private Guid? TablePlayerId => _tutorial?.Game.Players[0].Id ?? _session?.Client.Identity?.PlayerId;
    private bool IsTableHost => _tutorial is not null || _session?.IsHost == true;

    private void Tutorial_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) StartTutorial();
    }

    private void StartTutorial()
    {
        if (_draggedTokenValue is not null) EndChipDrag();
        _refreshTimer.Stop();
        _tutorial = new TutorialSession();
        _tutorialGameId = Guid.NewGuid();
        _tutorialEvents.Clear();
        MenuPanel.Visibility = EntryPanel.Visibility = LobbyPanel.Visibility = Visibility.Collapsed;
        HideNotice();
        SetHistoryVisible(true);
        RenderTutorial();
    }

    private GameSnapshot TutorialSnapshot()
    {
        var game = _tutorial!.Game;
        var revealed = game.RevealedHands.Select(h => h.PlayerId).ToHashSet();
        return new GameSnapshot(game.Phase, game.Result, game.HeistNumber, game.SuccessfulHeists,
            game.FailedHeists, game.LastHeistSucceeded,
            game.CommunityCards.Select(c => new CardSnapshot(c.Suit, c.Rank)).ToArray(),
            game.Players.Select((p, i) => new PlayerSnapshot(p.Id, p.Name,
                i == 0 || revealed.Contains(p.Id) ? p.Hand.Select(c => new CardSnapshot(c.Suit, c.Rank)).ToArray() : [],
                p.Signals.Select(s => new SignalSnapshot(s.Key, s.Value.Value)).ToArray(),
                p.SignalToken?.Value, p.IsChoiceConfirmed)).ToArray(),
            game.AvailableTokens.Select(t => t.Value).ToArray(), game.AllPlayersHaveTokens, game.AllChoicesConfirmed,
            game.NextPlayerToRevealId, _tutorialEvents.ToArray(), [])
        {
            GameId = _tutorialGameId,
            RevealedHands = game.RevealedHands.Select(h => new RevealedHandSnapshot(h.PlayerId, h.TokenValue,
                h.Score.Rank, h.Score.Tiebreakers)).ToArray()
        };
    }

    private void MutateTutorial(Action action)
    {
        var game = _tutorial!.Game;
        var phase = game.Phase;
        var tokens = game.Players.ToDictionary(p => p.Id, p => p.SignalToken?.Value);
        var confirmations = game.Players.ToDictionary(p => p.Id, p => p.IsChoiceConfirmed);
        var revealedCount = game.RevealedHands.Count;
        action();
        foreach (var player in game.Players)
        {
            if (phase == game.Phase && confirmations[player.Id] != player.IsChoiceConfirmed)
                AddTutorialEvent(player.IsChoiceConfirmed ? GameEventKind.ChoiceConfirmed : GameEventKind.ConfirmationWithdrawn, player.Id);
            if (player.SignalToken is not { } token || tokens[player.Id] == token.Value || phase != game.Phase) continue;
            var from = game.Players.FirstOrDefault(p => p.Id != player.Id && tokens[p.Id] == token.Value);
            AddTutorialEvent(from is null ? GameEventKind.TokenTaken : GameEventKind.TokenStolen,
                player.Id, token.Value, from?.Id, from?.Name);
        }
        if (phase != game.Phase && game.Phase is GamePhase.Flop or GamePhase.Turn or GamePhase.River or GamePhase.Showdown)
            AddTutorialEvent(GameEventKind.RoundRevealed);
        if (game.RevealedHands.Count > revealedCount)
            AddTutorialEvent(GameEventKind.HandRevealed, game.RevealedHands[^1].PlayerId);
        if (game.Phase == GamePhase.HeistComplete && phase != game.Phase)
            AddTutorialEvent(GameEventKind.HeistResolved);
        RenderTutorial();
    }

    private void AddTutorialEvent(GameEventKind kind, Guid? actor = null, int? token = null, Guid? from = null, string? fromName = null)
    {
        var game = _tutorial!.Game;
        var player = game.Players.FirstOrDefault(p => p.Id == actor) ?? game.Players[0];
        _tutorialEvents.Add(new GameEventSnapshot(_tutorialEvents.Count + 1, kind, player.Id, player.Name,
            game.Phase, game.HeistNumber, token, from, fromName, game.LastHeistSucceeded));
    }

    private void ApplyTutorialAction(GameAction action, int? value)
    {
        var lesson = _tutorial!;
        MutateTutorial(() =>
        {
            if (action == GameAction.TakeToken && value is { } token) lesson.TakeToken(token);
            else if (action == GameAction.ConfirmChoice && lesson.ExpectsConfirmation) lesson.Continue();
            else if ((action == GameAction.WithdrawConfirmation || action == GameAction.ConfirmChoice) && lesson.Step == 5)
                lesson.ToggleConfirmation();
            else if (action == GameAction.AdvanceRound && lesson.Step is 5 or 8 or 11 or 14)
            {
                if (lesson.Step == 14 && lesson.Game.Phase == GamePhase.River) lesson.Game.GoToShowdown();
                else if (lesson.Step != 14) lesson.Continue();
                else throw new InvalidOperationException("Utilisez la roue de révélation pour ouvrir les mains.");
            }
            else if (action == GameAction.RevealNextHand && lesson.Step == 14) lesson.RevealNextHand();
            else if (action == GameAction.ResolveHeist && lesson.Step == 14) lesson.Resolve();
            else throw new InvalidOperationException("Suivez l’action indiquée par le mentor à droite du tapis.");
        });
    }

    private void RenderTutorial()
    {
        if (_tutorial is not { } lesson) return;
        RenderGame(TutorialSnapshot());
        TutorialGuide.Visibility = TutorialBadge.Visibility = Visibility.Visible;
        TutorialProgress.Value = lesson.Step;
        TutorialStepText.Text = $"ÉTAPE {lesson.Step + 1:00} / 17 · À VOTRE RYTHME";
        TutorialTitle.Text = TutorialCopy.Titles[lesson.Step];
        TutorialMessage.Text = TutorialCopy.Lessons[lesson.Step];
        TutorialAction.Text = lesson.ExpectsToken
            ? $"→ Glissez le jeton {(lesson.Step == 1 ? 1 : lesson.SuggestedToken)} vers votre main, en bas du tapis."
            : lesson.ExpectsConfirmation ? "→ Cliquez sur CONFIRMER, dans la roue près de votre main."
            : lesson.Step switch
            {
                5 => "→ Essayez RETIRER, confirmez à nouveau, puis utilisez la roue DÉVOILER LE FLOP.",
                8 => "→ Utilisez la roue DÉVOILER LE TURN.",
                11 => "→ Utilisez la roue DÉVOILER LA RIVER.",
                14 => lesson.Game.Phase == GamePhase.River ? "→ Cliquez sur PASSER À LA RÉVÉLATION."
                    : lesson.Game.NextPlayerToRevealId is not null ? "→ Cliquez sur RÉVÉLER MAIN SUIVANTE."
                    : "→ Cliquez sur VÉRIFIER LES MAINS.",
                _ => "→ Lisez le conseil, puis continuez ci-dessous."
            };
        if (lesson.ExpectsToken)
            TokenActionHint.Text = lesson.Step == 6 ? "TUTORIEL · REPRENEZ LE JETON 2 À MILA"
                : $"TUTORIEL · GLISSEZ LE JETON {(lesson.Step == 1 ? 1 : lesson.SuggestedToken)} VERS VOTRE MAIN";
        TutorialContinue.Visibility = lesson.Step is 0 or 2 or 15 or 16 ? Visibility.Visible : Visibility.Collapsed;
        TutorialContinue.Content = lesson.Step switch { 0 => "Commencer l’entraînement", 2 => "Mila reprend mon jeton",
            15 => "Prêt pour l’aventure", _ => "Au menu · Jouer avec mes amis" };
        NextHeistButton.Visibility = Visibility.Collapsed;
        if (lesson.Step >= 15) HostActions.Visibility = Visibility.Collapsed;
        if (lesson.Step == 5)
        {
            TokenActions.Visibility = Visibility.Visible;
            TokenActionWheel.Visibility = Visibility.Visible;
        }
        HistoryHideButton.IsEnabled = false;
        TutorialHintScroll.ScrollToTop();
        ConfirmButton.Effect = null;
        var cue = lesson.ExpectsToken ? _visibleTokenSources.GetValueOrDefault(lesson.Step == 1 ? 1 : lesson.SuggestedToken)
            : lesson.ExpectsConfirmation ? ConfirmButton : null;
        if (cue is not null)
            cue.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Gold, BlurRadius = 20, ShadowDepth = 0, Opacity = .8 };
    }

    private void TutorialContinue_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial?.Complete == true) { TutorialExit_Click(sender, e); return; }
        _ = RunAsync(() => { MutateTutorial(() => _tutorial!.Continue()); return Task.CompletedTask; }, false);
    }

    private void TutorialRestart_Click(object sender, RoutedEventArgs e) => StartTutorial();
    private async void TutorialExit_Click(object sender, RoutedEventArgs e)
    {
        HistoryHideButton.IsEnabled = true;
        await ReturnToMenuAsync("À vous de jouer : invitez vos amis ou rejoignez leur table.", preserveGuestResume: true);
    }
}
