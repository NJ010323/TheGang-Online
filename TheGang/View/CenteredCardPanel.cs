using System.Windows;
using System.Windows.Controls;
namespace View;

/// <summary>Centre chaque rangée, y compris la dernière quand elle n'est pas complète.</summary>
internal sealed class CenteredCardPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        var limit=double.IsInfinity(available.Width)?1000:available.Width;
        double width=0,height=0,lineWidth=0,lineHeight=0;
        foreach(UIElement child in Children)
        {
            child.Measure(new Size(limit,double.PositiveInfinity));var size=child.DesiredSize;
            if(lineWidth>0&&lineWidth+size.Width>limit){width=Math.Max(width,lineWidth);height+=lineHeight;lineWidth=lineHeight=0;}
            lineWidth+=size.Width;lineHeight=Math.Max(lineHeight,size.Height);
        }
        return new Size(Math.Max(width,lineWidth),height+lineHeight);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var rows=new List<List<UIElement>>();var row=new List<UIElement>();double width=0;
        foreach(UIElement child in Children)
        {
            if(row.Count>0&&width+child.DesiredSize.Width>final.Width){rows.Add(row);row=[];width=0;}
            row.Add(child);width+=child.DesiredSize.Width;
        }
        if(row.Count>0)rows.Add(row);double y=0;
        foreach(var line in rows)
        {
            var height=line.Max(c=>c.DesiredSize.Height);var x=Math.Max(0,(final.Width-line.Sum(c=>c.DesiredSize.Width))/2);
            foreach(var child in line){child.Arrange(new Rect(x,y,child.DesiredSize.Width,height));x+=child.DesiredSize.Width;}
            y+=height;
        }
        return final;
    }
}
