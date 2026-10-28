using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Model.Game;
using Network;

namespace View;
public partial class MainWindow
{
    private void TutorialMenu_Click(object sender,RoutedEventArgs e)=>OpenTutorialMenu();
    private void OpenTutorialMenu()
    {
        if(_session is not null)return;
        EnsureSpecialOverlay();_specialOverlay!.Visibility=Visibility.Visible;_specialDetail=true;
        _specialBody!.Children.Clear();_specialScroll?.ScrollToTop();
        _specialBody.Children.Add(SpecialText("L’ÉCOLE DU BRAQUAGE",32));
        _specialBody.Children.Add(SpecialText("Choisissez votre initiation. Chaque parcours se joue seul sur le vrai plateau, avec deux complices simulés et les indications du mentor."));
        var paths=new UniformGrid{Columns=2};
        foreach(var mode in Enum.GetValues<GameDifficulty>())
        {
            var m=mode;var words=new StackPanel();words.Children.Add(SpecialText(DifficultyRules.Name(m),25));
            var description=m switch{GameDifficulty.Standard=>"Apprendre les bases · 17 étapes\nJetons, confirmations, flop, turn, river et premier braquage.",GameDifficulty.Advanced=>"Découvrir les 20 cartes spéciales\nUn atelier par effet, avec les choix privés et les pouvoirs des Complices.",GameDifficulty.Pro=>"S’entraîner avec une contrainte permanente\n19 ateliers : chaque carte est associée à une Effraction permanente. Ouverture facile est exclue.",_=>"Coordonner la bande sous pression\n9 ateliers avec deux Effractions simultanées. Aucun Complice et seulement deux alarmes."};
            words.Children.Add(SpecialText(description,15));
            var button=SpecialButton("",()=>{if(m==GameDifficulty.Standard)StartTutorial();else StartSpecialTutorial(m);});button.Content=words;button.Margin=new Thickness(8);button.MinHeight=200;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            paths.Children.Add(button);
        }
        _specialBody.Children.Add(paths);_specialBody.Children.Add(SpecialFamilies());
        _specialBody.Children.Add(SpecialText("ATELIERS À LA CARTE",25));
        _specialBody.Children.Add(SpecialText("Besoin de comprendre un effet précis ? Lancez directement son exercice en mode Avancé. Vous pouvez recommencer autant de fois que nécessaire."));
        var examples=new CenteredCardPanel();
        foreach(var card in SpecialCards.All)
        {
            var c=card;var content=new StackPanel{Width=150};content.Children.Add(SpecialImage(c.Id,100));
            var label=SpecialText(c.Name,14);label.TextAlignment=TextAlignment.Center;content.Children.Add(label);
            var button=SpecialButton("",()=>StartSpecialTutorial(GameDifficulty.Advanced,c.Id));button.Content=content;button.Margin=new Thickness(6);examples.Children.Add(button);
        }
        _specialBody.Children.Add(examples);
    }
    private void StartSpecialTutorial(GameDifficulty difficulty,string? card=null)
    {
        if(_draggedTokenValue is not null)EndChipDrag();
        _refreshTimer.Stop();_tutorial=null;_specialTutorial=new(difficulty,card);
        _tutorialGameId=Guid.NewGuid();_tutorialEvents.Clear();CloseSpecialInterface();HideHeistOutcome();HideRoundTransition();HideNotice();
        MenuPanel.Visibility=EntryPanel.Visibility=LobbyPanel.Visibility=Visibility.Collapsed;SetHistoryVisible(true);RenderTutorial();
    }
    private void RenderSpecialTutorial()
    {
        var lesson=_specialTutorial!;RenderGame(TutorialSnapshot());
        if(StatusText.Text.StartsWith("Cartes spéciales :")||StatusText.Text.StartsWith("Braquage 1 :"))HideNotice();
        TutorialGuide.Visibility=TutorialBadge.Visibility=Visibility.Visible;
        GameDifficultyButton.Visibility=Visibility.Collapsed;
        TutorialEffectsButton.Visibility=Visibility.Visible;
        TutorialProgress.Maximum=lesson.ExerciseCount;TutorialProgress.Value=lesson.Complete?lesson.ExerciseCount:lesson.ExerciseNumber-1;
        TutorialStepText.Text=$"ATELIER {lesson.ExerciseNumber:00} / {lesson.ExerciseCount:00} · {DifficultyRules.Name(lesson.Difficulty).ToUpperInvariant()}";
        TutorialTitle.Text=lesson.Complete?"Votre bande est prête !":lesson.Card.Name;
        TutorialMessage.Text=lesson.Complete?"Vous avez terminé ce parcours. Vous pouvez rejouer un effet précis dans les ateliers à la carte, changer de difficulté ou inviter vos amis pour une vraie partie.":lesson.LessonText;
        TutorialAction.Text=lesson.ActionText;
        TutorialContinue.Visibility=lesson.CanContinue||lesson.Complete?Visibility.Visible:Visibility.Collapsed;
        TutorialContinue.Content=lesson.ContinueText;
        NextHeistButton.Visibility=Visibility.Collapsed;
        if(lesson.Result||lesson.Complete)HostActions.Visibility=Visibility.Collapsed;
        HistoryHideButton.IsEnabled=false;
    }
    private void ContinueSpecialTutorial()
    {
        var lesson=_specialTutorial!;
        if(lesson.Complete){OpenTutorialMenu();return;}
        _=RunAsync(()=>
        {
            if(lesson.Result)
            {
                lesson.Continue();_tutorialGameId=Guid.NewGuid();_tutorialEvents.Clear();CloseSpecialInterface();HideHeistOutcome();HideRoundTransition();RenderTutorial();
            }
            else MutateTutorial(lesson.Continue);
            return Task.CompletedTask;
        },false);
    }
}
