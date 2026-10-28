using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Model.Game;

namespace View;

/// <summary>Vingt illustrations vectorielles originales, dessinées dans la palette du plateau.</summary>
internal static class SpecialCardVisuals
{
    private static Brush Ink(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    internal static FrameworkElement Card(string id,double width=120,double? height=null)
    {
        var definition=SpecialCards.Get(id);var accent=definition.IsComplice?"#76B7A0":"#DF8268";
        var canvas=new Canvas{Width=240,Height=360,Background=Ink("#211519")};
        var background=new Border{Width=240,Height=360,CornerRadius=new CornerRadius(15),BorderThickness=new Thickness(3),BorderBrush=Ink("#E2BC72"),
            Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(definition.IsComplice?"#283E36":"#4B262A"),(Color)ColorConverter.ConvertFromString("#160F14"),110)};
        canvas.Children.Add(background);
        var inner=new Border{Width=222,Height=342,CornerRadius=new CornerRadius(10),BorderBrush=Ink("#8D6942"),BorderThickness=new Thickness(1)};Canvas.SetLeft(inner,9);Canvas.SetTop(inner,9);canvas.Children.Add(inner);
        Label(canvas,definition.Family.ToUpperInvariant(),18,24,170,12,"#E5C996");
        var number=new Border{Width=32,Height=32,CornerRadius=new CornerRadius(16),Background=Ink("#C59B55"),Child=new TextBlock{Text=definition.Number.ToString("00"),FontWeight=FontWeights.Bold,Foreground=Ink("#241612"),FontSize=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        Canvas.SetLeft(number,185);Canvas.SetTop(number,16);canvas.Children.Add(number);
        Path(canvas,"M 23,64 L 98,64 L 120,76 L 142,64 L 217,64", "#BC9251",1);
        var art=new Canvas{Width=180,Height=170};Canvas.SetLeft(art,30);Canvas.SetTop(art,88);canvas.Children.Add(art);
        Ellipse(art,17,3,146,146,"#C19B57",null,1);
        Ellipse(art,25,11,130,130,"#6F5739",null,1);
        Draw(art,id,accent);
        Path(canvas,"M 23,270 L 103,270 L 120,260 L 137,270 L 217,270", "#BC9251",1);
        Label(canvas,definition.Name,22,280,196,definition.Name.Length>24?16:19,"#F4D694");
        Label(canvas,definition.IsComplice?"UN ATOUT POUR LA BANDE":"UN DÉFI POUR LA BANDE",20,332,200,9,accent);
        for(var i=0;i<4;i++)
        {
            var corner=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 0,18 L 0,0 L 18,0"),Stroke=Ink("#FFE3A1"),StrokeThickness=1.2,RenderTransform=new RotateTransform(i*90,9,9)};
            Canvas.SetLeft(corner,i is 1 or 2?213:9);Canvas.SetTop(corner,i>=2?333:9);canvas.Children.Add(corner);
        }
        var view=new Viewbox{Width=width,Height=height??width*1.5,Stretch=Stretch.Fill,Child=canvas,Margin=new Thickness(6)};
        System.Windows.Automation.AutomationProperties.SetName(view,$"{definition.Family} {definition.Number} : {definition.Name}");
        return view;
    }
    private static void Label(Canvas c,string text,double x,double y,double width,int size,string color)
    {
        var t=new TextBlock{Text=text,Width=width,FontFamily=new FontFamily(size>=16?"Georgia":"Segoe UI"),FontSize=size,FontWeight=FontWeights.Bold,Foreground=Ink(color),TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};Canvas.SetLeft(t,x);Canvas.SetTop(t,y);c.Children.Add(t);
    }
    private static void Path(Canvas c,string data,string color="#E9C77F",double thickness=3,string? fill=null)
    {c.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=Ink(color),StrokeThickness=thickness,Fill=fill is null?null:Ink(fill),StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});}
    private static void Ellipse(Canvas c,double x,double y,double w,double h,string stroke,string? fill=null,double thickness=3)
    {var e=new Ellipse{Width=w,Height=h,Stroke=Ink(stroke),Fill=fill is null?null:Ink(fill),StrokeThickness=thickness};Canvas.SetLeft(e,x);Canvas.SetTop(e,y);c.Children.Add(e);}
    private static void Box(Canvas c,double x,double y,double w,double h,string fill="#322326",string edge="#E9C77F")
    {var b=new Border{Width=w,Height=h,CornerRadius=new CornerRadius(5),Background=Ink(fill),BorderBrush=Ink(edge),BorderThickness=new Thickness(2)};Canvas.SetLeft(b,x);Canvas.SetTop(b,y);c.Children.Add(b);}
    private static void Person(Canvas c,bool hood=false,bool broad=false)
    {
        Path(c,broad?"M 25,145 L 32,91 L 66,78 L 90,95 L 115,78 L 148,91 L 157,145 Z":"M 42,145 L 48,96 L 71,82 L 90,98 L 109,82 L 132,96 L 138,145 Z",fill:"#412C2A");
        Ellipse(c,69,35,42,49,"#D9AF69","#D1AA79",2);
        Path(c,hood?"M 57,90 Q 40,28 90,19 Q 140,28 123,90 L 109,76 L 110,41 L 70,41 L 71,76 Z":"M 67,52 L 64,34 Q 85,13 113,36 L 114,52 L 100,36 L 84,45 Z",fill:"#20151A");
        Path(c,"M 90,99 L 85,113 L 90,137 L 95,113 Z",fill:"#C69648",thickness:1);
        Path(c,"M 55,99 L 76,118 L 68,88 M 125,99 L 104,118 L 112,88",thickness:2);
    }
    private static void Draw(Canvas c,string id,string accent)
    {
        switch(id)
        {
            case "E01":Path(c,"M 45,139 L 82,82 M 134,139 L 98,82",accent,12);Path(c,"M 80,88 L 67,55 L 82,32 L 91,65 L 100,32 L 114,55 L 100,88",thickness:5);Ellipse(c,83,77,15,15,"#F1D38D","#3F2B25",2);break;
            case "E02":Box(c,35,58,110,52);for(var i=0;i<9;i++)Box(c,45+i*10,90-i%4*6,4,10+i%4*6,accent,accent);Path(c,"M 24,43 Q 5,83 24,125 M 156,43 Q 175,83 156,125",accent,3);break;
            case "E03":Box(c,66,28,50,55);Ellipse(c,81,41,20,20,accent,accent,1);Path(c,"M 90,67 L 51,128 L 133,128 Z",accent,2);Ellipse(c,83,88,14,14,"#EDCC87",null,2);Path(c,"M 90,104 L 90,119 M 80,112 L 101,112 M 72,139 L 109,139",thickness:3);break;
            case "E04":Path(c,"M 22,82 Q 90,18 158,82 Q 90,144 22,82 Z",thickness:4,fill:"#281A20");Ellipse(c,65,56,51,51,"#D9B368",accent,2);Ellipse(c,80,69,22,25,"#F2D18B","#171016",2);Path(c,"M 90,27 L 90,47 M 90,118 L 90,138 M 19,82 L 57,82",accent,2);break;
            case "E05":Path(c,"M 29,99 L 42,75 L 70,71 L 86,49 L 118,49 L 137,78 L 155,87 L 155,119 L 25,119 Z",accent,3,"#603329");Path(c,"M 77,70 L 91,54 L 113,54 L 124,75 Z",fill:"#1A1518",thickness:2);Ellipse(c,43,106,28,28,"#D6AE67","#211619",5);Ellipse(c,116,106,28,28,"#D6AE67","#211619",5);Path(c,"M 8,86 L 30,86 M 4,99 L 22,99 M 11,115 L 20,115",accent,3);break;
            case "E06":Box(c,42,32,96,108);for(var i=0;i<10;i++)Path(c,$"M 50,{42+i*9} L 130,{42+i*9}",i%2==0?accent:"#B78D53",3);break;
            case "E07":Box(c,29,30,122,113,"#21151A");for(var i=0;i<4;i++)Path(c,$"M 35,{40+i*27} L 145,{132-i*27}",accent,2);Ellipse(c,28,40,8,8,accent,accent,1);Ellipse(c,144,128,8,8,accent,accent,1);break;
            case "E08":Path(c,"M 90,18 L 90,49 M 59,68 Q 90,33 121,68 L 138,86 L 42,86 Z",fill:"#735B3D");Path(c,"M 91,96 L 74,120 L 91,116 L 79,149",accent,5);Path(c,"M 49,100 L 34,119 M 131,100 L 145,119",accent,2);break;
            case "E09":for(var i=0;i<5;i++){var n=i*9;Path(c,$"M {42+n},129 C {18+n},{28+n/2} {160-n},{17+n/2} {139-n},113",i%2==0?"#E9C77F":accent,3);}Path(c,"M 91,72 C 77,91 109,105 87,145",accent,3);break;
            case "E10":Box(c,42,24,96,42);Path(c,"M 34,70 L 146,70 Q 144,145 90,145 Q 36,145 34,70 Z",fill:"#45332C");Ellipse(c,66,84,48,48,accent,"#151017",3);Ellipse(c,80,97,20,20,"#EDC577",accent,2);Path(c,"M 47,44 L 132,44",accent,2);break;
            default:
                Person(c,id=="C05",id=="C10");
                switch(id)
                {
                    case "C01":Box(c,50,108,80,45,"#9B7040");Path(c,"M 52,111 L 90,136 L 128,111",accent,2);break;
                    case "C02":Ellipse(c,57,103,66,51,"#E2BA73","#211619",4);Path(c,"M 89,106 L 89,145 M 60,122 L 120,122",accent,4);break;
                    case "C03":Box(c,41,107,98,48);Path(c,"M 53,145 L 68,131 L 83,136 L 108,118 L 126,118",accent,3);break;
                    case "C04":Path(c,"M 66,143 L 114,143 L 107,130 L 76,130 L 82,115 L 80,104 L 92,90 L 103,105 L 99,119 L 107,130",accent,3,"#9E783E");break;
                    case "C05":Box(c,41,104,98,46,"#142422",accent);Path(c,"M 65,119 L 57,126 L 65,133 M 114,119 L 122,126 L 114,133 M 83,137 L 96,116",accent,3);Path(c,"M 30,154 L 150,154",thickness:5);break;
                    case "C06":Path(c,"M 30,102 L 10,102 L 20,92 M 10,102 L 20,113 M 150,102 L 170,102 L 160,92 M 170,102 L 160,113",accent,4);Ellipse(c,78,112,24,24,accent,null,3);break;
                    case "C07":Box(c,66,105,50,57,"#EAD5AF");Label(c,"V",71,109,40,31,"#563723");break;
                    case "C08":Box(c,39,109,104,43,"#222620",accent);for(var i=0;i<3;i++){Path(c,$"M 50,{119+i*11} L 133,{119+i*11}",thickness:1);Ellipse(c,57+i*21,115+i*11,9,9,accent,accent,1);}break;
                    case "C09":Path(c,"M 68,51 L 111,51 L 105,65 L 75,65 Z",accent,2,"#263B34");Box(c,45,111,36,45,"#DBC18C");Box(c,89,107,36,45,"#DBC18C");Label(c,"A",50,117,25,22,"#753A30");Label(c,"R",94,114,25,22,"#493129");break;
                    case "C10":Ellipse(c,29,109,42,39,accent,"#BB8E62",3);Ellipse(c,110,109,42,39,accent,"#BB8E62",3);Path(c,"M 40,115 L 40,130 M 49,114 L 49,130 M 60,116 L 60,132 M 122,116 L 122,131 M 133,115 L 133,130",thickness:1);break;
                }
                break;
        }
    }
}
