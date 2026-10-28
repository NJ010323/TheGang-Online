using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Model.Game;

namespace View;

public partial class MainWindow
{
    private Border DossierPanel(UIElement content, bool highlighted=false) => new()
    {
        Child=content, Padding=new Thickness(20), CornerRadius=new CornerRadius(12),
        BorderThickness=new Thickness(highlighted?2:1), BorderBrush=SpecialBrush(highlighted?"#F1CC7B":"#785A3B"),
        Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(highlighted?"#443027":"#2D2023"),(Color)ColorConverter.ConvertFromString("#191114"),100),
        Effect=highlighted?new DropShadowEffect{Color=Colors.Goldenrod,BlurRadius=14,ShadowDepth=0,Opacity=.24}:null
    };
    private UIElement DifficultyTiles(GameDifficulty current,bool currentOnly=false)
    {
        var tiles=new UniformGrid{Columns=currentOnly?1:2};
        foreach(var mode in Enum.GetValues<GameDifficulty>().Where(m=>!currentOnly||m==current))
        {
            var active=mode==current;
            var content=new StackPanel();
            var title=new Grid();title.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});title.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            title.Children.Add(SpecialText(DifficultyRules.Name(mode),25));
            var badge=new Border{Background=SpecialBrush(active?"#C99C52":"#34252A"),CornerRadius=new CornerRadius(5),Padding=new Thickness(9,5,9,5),VerticalAlignment=VerticalAlignment.Top};
            badge.Child=new TextBlock{Text=active?"MODE ACTIF":"CHOISIR",FontSize=10,FontWeight=FontWeights.Bold,Foreground=SpecialBrush(active?"#21140D":"#CEB48A")};Grid.SetColumn(badge,1);title.Children.Add(badge);content.Children.Add(title);
            var level=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,16)};
            for(var i=0;i<4;i++)level.Children.Add(new Border{Width=30,Height=3,Margin=new Thickness(0,0,5,0),Background=SpecialBrush(i<=(int)mode?"#E3B765":"#59443A"),CornerRadius=new CornerRadius(1)});
            content.Children.Add(level);
            AddModeRule(content,"AU DÉPART",mode switch{GameDifficulty.Advanced=>"Un premier braquage classique.",GameDifficulty.Pro=>"Une Effraction permanente tirée au hasard.",GameDifficulty.Gangster=>"Deux Effractions tirées au hasard.",_=>"Les règles de base, sans carte spéciale."});
            AddModeRule(content,"ENTRE LES BRAQUAGES",mode switch{GameDifficulty.Advanced=>"Réussite : une Effraction. Échec : un Complice. Pour le prochain braquage seulement.",GameDifficulty.Pro=>"Une carte temporaire selon le résultat, en plus de la permanente. Ouverture facile est retirée.",GameDifficulty.Gangster=>"Remplacez l’Effraction au plus petit numéro. Aucun Complice, ni Ouverture facile.",_=>"Aucun effet supplémentaire. Idéal pour découvrir le jeu et apprendre à lire les jetons."});
            var result=new Grid{Margin=new Thickness(0,8,0,0)};result.ColumnDefinitions.Add(new());result.ColumnDefinitions.Add(new());
            var win=SpecialText("◆  3 COFFRES",12);win.Foreground=SpecialBrush("#F2D18A");result.Children.Add(win);
            var loss=SpecialText(mode==GameDifficulty.Gangster?"◆  2 ALARMES":"◆  3 ALARMES",12);loss.Foreground=SpecialBrush("#E99786");Grid.SetColumn(loss,1);result.Children.Add(loss);content.Children.Add(result);
            var frame=DossierPanel(content,active);frame.MinHeight=285;
            var button=new Button{Content=frame,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Margin=new Thickness(6),Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Cursor=System.Windows.Input.Cursors.Hand};
            button.Template=new ControlTemplate(typeof(Button)){VisualTree=new FrameworkElementFactory(typeof(ContentPresenter))};
            button.MouseEnter+=(_,_)=>frame.BorderBrush=SpecialBrush("#FFE6AD");
            button.MouseLeave+=(_,_)=>frame.BorderBrush=SpecialBrush(active?"#F1CC7B":"#785A3B");
            button.GotKeyboardFocus+=(_,_)=>frame.BorderBrush=SpecialBrush("#FFE6AD");
            button.LostKeyboardFocus+=(_,_)=>frame.BorderBrush=SpecialBrush(active?"#F1CC7B":"#785A3B");
            
            button.Click+=(_,_)=>{if(_session?.IsHost==true&&_lastLobbySnapshot?.Status==Model.Lobby.LobbyStatus.Open)ChangeDifficulty(mode);};
            System.Windows.Automation.AutomationProperties.SetName(button,DifficultyRules.Name(mode)+(active?", mode actif":""));
            tiles.Children.Add(button);
        }
        return tiles;
    }
    private void AddModeRule(Panel panel,string label,string explanation)
    {
        var heading=SpecialText(label,11);heading.Foreground=SpecialBrush("#C2A26E");heading.FontWeight=FontWeights.Bold;heading.Margin=new Thickness(0,0,0,5);
        panel.Children.Add(heading);panel.Children.Add(SpecialText(explanation,14));
    }
    private void DisplaySpecialDossier(SpecialCardDefinition card)
    {
        _specialDetail=true;_specialBody!.Children.Clear();
        _specialScroll?.ScrollToTop();
        var navigation=SpecialButton("‹  Retour",()=>{_specialDetail=false;BuildSpecialInterface();_specialScroll?.ScrollToTop();});navigation.HorizontalAlignment=HorizontalAlignment.Left;navigation.MinWidth=120;_specialBody.Children.Add(navigation);
        var heading=SpecialText(card.Name,32);heading.Margin=new Thickness(0,8,0,20);_specialBody.Children.Add(heading);
        var row=new Grid{Margin=new Thickness(0,0,0,12)};
        row.ColumnDefinitions.Add(new(){Width=new GridLength(285)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var illustration=new StackPanel();
        var category=SpecialText($"{card.Family.ToUpperInvariant()}  /  {card.Number:00}",13);category.TextAlignment=TextAlignment.Center;illustration.Children.Add(category);
        var art=SpecialImage(card.Id,215);art.HorizontalAlignment=HorizontalAlignment.Center;illustration.Children.Add(art);
        var ornament=SpecialText("━━  ◆  ━━",18);ornament.TextAlignment=TextAlignment.Center;ornament.Margin=new Thickness(0,14,0,0);illustration.Children.Add(ornament);
        var artFrame=DossierPanel(illustration,true);artFrame.Margin=new Thickness(0,0,24,0);artFrame.VerticalAlignment=VerticalAlignment.Top;row.Children.Add(artFrame);
        var explanations=new StackPanel();
        var when=new StackPanel();AddModeRule(when,"MOMENT D’ACTIVATION",card.Timing);var whenFrame=DossierPanel(when);whenFrame.Margin=new Thickness(0,0,0,14);explanations.Children.Add(whenFrame);
        var rule=new StackPanel();var label=SpecialText("L’EFFET SUR LE BRAQUAGE",12);label.Foreground=SpecialBrush("#DAB87A");label.FontWeight=FontWeights.Bold;rule.Children.Add(label);rule.Children.Add(SpecialText(card.Rule,19));
        explanations.Children.Add(DossierPanel(rule));
        var reminder=card.IsComplice?"UN COUP DE MAIN POUR LA BANDE\nCe pouvoir ne dure que le braquage en cours. Les actions facultatives nécessitent l’accord de tous.":card.Id=="E01"?"MODES CONCERNÉS\nDisponible en Avancé. Cette carte est retirée des modes Pro et Gangster.":"UNE CONTRAINTE À ANTICIPER\nAvancé : un seul braquage. Pro : temporaire ou permanente. Gangster : la carte au plus petit numéro est remplacée au braquage suivant.";
        var note=SpecialText(reminder,13);note.Margin=new Thickness(3,18,0,0);note.Foreground=SpecialBrush("#C9AD83");explanations.Children.Add(note);
        Grid.SetColumn(explanations,1);row.Children.Add(explanations);_specialBody.Children.Add(row);
    }
}
