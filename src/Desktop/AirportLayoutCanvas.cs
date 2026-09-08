using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;
public sealed class AirportLayoutCanvas : FrameworkElement
{
    private readonly LayoutViewport viewport=new();
    private BuilderViewModel? model;
    private Point lastMouse;
    private LocalPoint dragStart,standStart;
    private int? dragging;
    private LocalPoint? ghost;
    private bool panning,fit=true;
    public AirportLayoutCanvas()
    {
        Focusable=true;ClipToBounds=true;
        DataContextChanged+=(_,e)=>{
            if(model!=null){model.PropertyChanged-=Changed;model.Layout.PropertyChanged-=Changed;}
            model=e.NewValue as BuilderViewModel;
            if(model!=null){model.PropertyChanged+=Changed;model.Layout.PropertyChanged+=Changed;}
            fit=true;InvalidateVisual();
        };
        SizeChanged+=(_,_)=>InvalidateVisual();
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)=>InvalidateVisual();
    private Point Screen(LocalPoint p) {var s=viewport.ToScreen(p,ActualWidth,ActualHeight);return new(s.X,s.Y);}
    private LocalPoint Local(Point p)=>viewport.ToLocal(p.X,p.Y,ActualWidth,ActualHeight);
    public void Fit()
    {
        if(model==null)return;
        double extent=Math.Max(model.LayoutRunwayLength,model.Layout.Stands.Select(s=>2*(Math.Max(Math.Abs(s.EastMeters),Math.Abs(s.NorthMeters))+s.RadiusMeters)).DefaultIfEmpty(100).Max());
        viewport.Fit(extent+60,ActualWidth,ActualHeight);fit=false;InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(237,243,235)),null,new Rect(RenderSize));
        if(model==null||ActualWidth<1||ActualHeight<1)return;
        if(fit)Fit();
        double step=Math.Pow(10,Math.Floor(Math.Log10(65/viewport.PixelsPerMeter)));
        if(step*viewport.PixelsPerMeter<30)step*=5;
        var bottom=Local(new(0,ActualHeight));var top=Local(new(ActualWidth,0));
        var gridPen=new Pen(new SolidColorBrush(Color.FromRgb(211,221,208)),1);
        for(double e=Math.Floor(bottom.East/step)*step;e<=top.East;e+=step){var x=Screen(new(e,0)).X;dc.DrawLine(gridPen,new(x,0),new(x,ActualHeight));}
        for(double n=Math.Floor(bottom.North/step)*step;n<=top.North;n+=step){var y=Screen(new(0,n)).Y;dc.DrawLine(gridPen,new(0,y),new(ActualWidth,y));}
        var origin=Screen(new(0,0));
        dc.PushTransform(new RotateTransform(model.LayoutRunwayHeading,origin.X,origin.Y));
        double w=model.LayoutRunwayWidth*viewport.PixelsPerMeter,l=model.LayoutRunwayLength*viewport.PixelsPerMeter;
        dc.DrawRectangle(Brushes.Tan,new Pen(Brushes.SaddleBrown,1),new(origin.X-w/2,origin.Y-l/2,w,l));
        dc.DrawLine(new Pen(Brushes.White,1){DashStyle=DashStyles.Dash},new(origin.X,origin.Y-l/2),new(origin.X,origin.Y+l/2));
        dc.Pop();
        dc.DrawEllipse(Brushes.DarkGreen,null,origin,3,3);
        foreach(var stand in model.Layout.Stands)
        {
            var pos=dragging==stand.Number&&ghost.HasValue?ghost.Value:new LocalPoint(stand.EastMeters,stand.NorthMeters);
            var center=Screen(pos);double radius=stand.RadiusMeters*viewport.PixelsPerMeter;
            bool selected=model.Layout.SelectedNumber==stand.Number;
            dc.DrawEllipse(selected?Brushes.LightSkyBlue:Brushes.AliceBlue,new Pen(selected?Brushes.DarkBlue:Brushes.SteelBlue,selected?2:1),center,radius,radius);
            double h=stand.TrueHeadingDegrees*Math.PI/180;
            var nose=new Point(center.X+Math.Sin(h)*radius,center.Y-Math.Cos(h)*radius);
            dc.DrawLine(new Pen(Brushes.DarkBlue,2),center,nose);dc.DrawEllipse(Brushes.DarkBlue,null,nose,2.5,2.5);
            Text(dc,stand.Number.ToString(),new(center.X+5,center.Y+3),Brushes.DarkBlue);
        }
        dc.DrawRectangle(Brushes.White,null,new(8,8,235,45));
        Text(dc,model.Survey==null?"Capture a survey to edit":"Fixed survey anchor • north up",new(15,12),Brushes.DarkGreen);
        Text(dc,$"Grid {step:0.##} m  |  zoom {viewport.PixelsPerMeter:0.##} px/m",new(15,32),Brushes.DimGray);
        Text(dc,"N ↑",new(ActualWidth-50,15),Brushes.DarkGreen);
        double metres=Math.Pow(10,Math.Floor(Math.Log10(100/viewport.PixelsPerMeter)));
        if(metres*viewport.PixelsPerMeter<40)metres*=5;
        double bar=metres*viewport.PixelsPerMeter;
        dc.DrawLine(new Pen(Brushes.DarkGreen,3),new(ActualWidth-bar-20,ActualHeight-22),new(ActualWidth-20,ActualHeight-22));
        Text(dc,$"{metres:0.##} m",new(ActualWidth-bar-20,ActualHeight-43),Brushes.DarkGreen);
    }
    private void Text(DrawingContext dc,string text,Point p,Brush brush)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),p);
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();lastMouse=e.GetPosition(this);
        if(e.ChangedButton is MouseButton.Middle or MouseButton.Right){panning=true;CaptureMouse();e.Handled=true;return;}
        if(e.ChangedButton!=MouseButton.Left||model==null||!model.CanLayout)return;
        var point=Local(lastMouse);
        var hit=model.Layout.Stands.Reverse().FirstOrDefault(s=>Math.Sqrt(Math.Pow(s.EastMeters-point.East,2)+Math.Pow(s.NorthMeters-point.North,2))<=Math.Max(s.RadiusMeters,8/viewport.PixelsPerMeter));
        model.Layout.Select(hit?.Number);
        if(hit!=null){dragging=hit.Number;dragStart=point;standStart=new(hit.EastMeters,hit.NorthMeters);ghost=standStart;CaptureMouse();}
        else if(e.ClickCount==2)model.AddParkingAt(point);
        e.Handled=true;InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point=e.GetPosition(this);
        if(panning){viewport.Pan(point.X-lastMouse.X,point.Y-lastMouse.Y);lastMouse=point;InvalidateVisual();}
        else if(dragging.HasValue){var local=Local(point);ghost=new(standStart.East+local.East-dragStart.East,standStart.North+local.North-dragStart.North);InvalidateVisual();}
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if(dragging is {} id&&ghost is {} point)model?.Layout.Move(id,point);
        dragging=null;ghost=null;panning=false;ReleaseMouseCapture();InvalidateVisual();e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e){dragging=null;ghost=null;panning=false;InvalidateVisual();base.OnLostMouseCapture(e);}
    protected override void OnMouseWheel(MouseWheelEventArgs e){var p=e.GetPosition(this);viewport.Zoom(e.Delta>0?1.2:1/1.2,p.X,p.Y,ActualWidth,ActualHeight);InvalidateVisual();e.Handled=true;}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(model==null)return;
        if(e.Key==Key.Escape){dragging=null;ghost=null;panning=false;ReleaseMouseCapture();}
        else if(e.Key==Key.Delete)model.Layout.Delete();
        else if(e.Key==Key.Z&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control))model.Layout.Undo();
        else if(e.Key==Key.Y&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control))model.Layout.Redo();
        else if(e.Key==Key.R)model.Layout.Rotate(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?-15:15);
        else if(e.Key==Key.Home)Fit();
        else {base.OnKeyDown(e);return;}
        e.Handled=true;InvalidateVisual();
    }
}
