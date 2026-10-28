using System.Windows;
using System.Windows.Controls;
using Model.Game;
namespace View;
public partial class MainWindow
{
    private FrameworkElement SpecialFamilies()
    {
        var row=new Grid{Margin=new Thickness(0,18,0,12)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new());
        var definitions=new[]{("LES EFFRACTIONS","Les sécurités du coffre","Elles compliquent le braquage : jetons verrouillés, informations perdues ou conditions supplémentaires. En Avancé, une réussite déclenche une Effraction au braquage suivant. Pro et Gangster en ajoutent dès le départ."),
            ("LES COMPLICES","Les alliés de votre bande","Ils vous aident : informations autorisées, échanges de cartes ou bonus de classement. En Avancé et Pro, un échec apporte un Complice au braquage suivant. Un pouvoir facultatif nécessite l’accord de tous et ne s’utilise qu’une fois. Aucun Complice en Gangster.")};
        for(var i=0;i<2;i++)
        {
            var words=new StackPanel();words.Children.Add(SpecialText(definitions[i].Item1,12));words.Children.Add(SpecialText(definitions[i].Item2,23));words.Children.Add(SpecialText(definitions[i].Item3,14));
            var frame=DossierPanel(words);frame.Margin=new Thickness(i==0?0:8,0,i==0?8:0,0);Grid.SetColumn(frame,i);row.Children.Add(frame);
        }
        return row;
    }
}
