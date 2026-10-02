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
    private LocalPoint dragStart,itemStart;
    private int? dragging;
    private bool draggingObject;
    private LocalPoint? ghost;
    private bool panning,fit=true;
    public event EventHandler? ViewChanged;
    public (LocalPoint Center,double PixelsPerMeter) ViewState =>
        (viewport.ToLocal(ActualWidth/2,ActualHeight/2,ActualWidth,ActualHeight),viewport.PixelsPerMeter);
    public AirportLayoutCanvas()
    {
        Focusable=true;ClipToBounds=true;
        DataContextChanged+=(_,e)=>{
            if(model!=null){model.PropertyChanged-=Changed;model.Layout.PropertyChanged-=Changed;}
            model=e.NewValue as BuilderViewModel;
            if(model!=null){model.PropertyChanged+=Changed;model.Layout.PropertyChanged+=Changed;}
            fit=true;InvalidateVisual();
        };
        SizeChanged+=(_,_)=>{if(fit)Fit();InvalidateVisual();};
        Loaded+=(_,_)=>Fit();
    }
    private void Changed(object? sender,PropertyChangedEventArgs e){InvalidateVisual();ViewChanged?.Invoke(this,EventArgs.Empty);}
    private Point Screen(LocalPoint p) {var s=viewport.ToScreen(p,ActualWidth,ActualHeight);return new(s.X,s.Y);}
    private LocalPoint Local(Point p)=>viewport.ToLocal(p.X,p.Y,ActualWidth,ActualHeight);
    public void Fit()
    {
        if(model==null||ActualWidth<1||ActualHeight<1){fit=true;return;}
        double extent=Math.Max(model.HasRunway?model.LayoutRunwayLength:100,model.Layout.Stands.Select(s=>2*(Math.Max(Math.Abs(s.EastMeters),Math.Abs(s.NorthMeters))+s.RadiusMeters)).DefaultIfEmpty(100).Max());
        foreach(var t in model.Layout.Taxiways)
            foreach(var v in t.Points){var p=v.Resolve(model.Layout.Stands);extent=Math.Max(extent,2*(Math.Max(Math.Abs(p.East),Math.Abs(p.North))+t.WidthMeters));}
        foreach(var o in model.Layout.Objects)
        {
            var a=SceneryAssets.Find(o.AssetId);if(a==null)continue;
            foreach(var corner in SceneryObjectGeometry.FootprintCorners(o,a,a.Foundation?.MarginMeters??0))
                extent=Math.Max(extent,2*Math.Max(Math.Abs(corner.East),Math.Abs(corner.North)));
        }
        viewport.Fit(extent+60,ActualWidth,ActualHeight);fit=false;InvalidateVisual();ViewChanged?.Invoke(this,EventArgs.Empty);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(55,237,243,235)),null,new Rect(RenderSize));
        if(model==null||ActualWidth<1||ActualHeight<1)return;
        if(fit)Fit();
        double step=Math.Pow(10,Math.Floor(Math.Log10(65/viewport.PixelsPerMeter)));
        if(step*viewport.PixelsPerMeter<30)step*=5;
        var bottom=Local(new(0,ActualHeight));var top=Local(new(ActualWidth,0));
        var gridPen=new Pen(new SolidColorBrush(Color.FromRgb(211,221,208)),1);
        for(double e=Math.Floor(bottom.East/step)*step;e<=top.East;e+=step){var x=Screen(new(e,0)).X;dc.DrawLine(gridPen,new(x,0),new(x,ActualHeight));}
        for(double n=Math.Floor(bottom.North/step)*step;n<=top.North;n+=step){var y=Screen(new(0,n)).Y;dc.DrawLine(gridPen,new(0,y),new(ActualWidth,y));}
        var origin=Screen(new(0,0));
        if(model.HasRunway)
        {
            dc.PushTransform(new RotateTransform(model.LayoutRunwayHeading,origin.X,origin.Y));
            double w=model.LayoutRunwayWidth*viewport.PixelsPerMeter,l=model.LayoutRunwayLength*viewport.PixelsPerMeter;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(210,210,180,125)),new Pen(Brushes.SaddleBrown,2),new(origin.X-w/2,origin.Y-l/2,w,l));
            dc.DrawLine(new Pen(Brushes.White,1){DashStyle=DashStyles.Dash},new(origin.X,origin.Y-l/2),new(origin.X,origin.Y+l/2));
            dc.Pop();
        }
        dc.DrawEllipse(model.IsPlacingRunway?Brushes.DarkOrange:Brushes.DarkGreen,new Pen(Brushes.White,1),origin,5,5);
        foreach(var t in model.Layout.Taxiways)
        {
            var points=t.Points.Select(v=>v.Resolve(model.Layout.Stands)).ToArray();
            var selected=model.Layout.SelectedTaxiway?.Number==t.Number;
            DrawPath(dc,points,selected?Brushes.SteelBlue:Brushes.Peru,t.WidthMeters);
            if(points.Length>0)Text(dc,t.Name,Screen(points[0]),Brushes.DarkBlue);
        }
        DrawPath(dc,model.Layout.DraftPoints.Select(v=>v.Resolve(model.Layout.Stands)).ToArray(),Brushes.DarkOrange,12);
        foreach(var stand in model.Layout.Stands)
        {
            var pos=!draggingObject&&dragging==stand.Number&&ghost.HasValue?ghost.Value:new LocalPoint(stand.EastMeters,stand.NorthMeters);
            var center=Screen(pos);double radius=stand.RadiusMeters*viewport.PixelsPerMeter;
            bool selected=model.Layout.SelectedNumber==stand.Number;
            dc.DrawEllipse(selected?Brushes.LightSkyBlue:Brushes.AliceBlue,new Pen(selected?Brushes.DarkBlue:Brushes.SteelBlue,selected?2:1),center,radius,radius);
            double h=stand.TrueHeadingDegrees*Math.PI/180;
            var nose=new Point(center.X+Math.Sin(h)*radius,center.Y-Math.Cos(h)*radius);
            dc.DrawLine(new Pen(Brushes.DarkBlue,2),center,nose);dc.DrawEllipse(Brushes.DarkBlue,null,nose,2.5,2.5);
            Text(dc,stand.Number.ToString(),new(center.X+5,center.Y+3),Brushes.DarkBlue);
        }
        foreach(var item in model.Layout.Objects)
        {
            var asset=SceneryAssets.Find(item.AssetId);if(asset==null)continue;
            var pos=draggingObject&&dragging==item.Number&&ghost.HasValue?ghost.Value:new LocalPoint(item.EastMeters,item.NorthMeters);
            var displayed=item with{EastMeters=pos.East,NorthMeters=pos.North};var originPoint=Screen(pos);bool selected=model.Layout.SelectedObject?.Number==item.Number;
            if(asset.Foundation is {} foundation)
            {
                void Outline(double margin,Pen pen)
                {
                    var outline=SceneryObjectGeometry.FootprintCorners(displayed,asset,margin).Select(Screen).ToArray();
                    var shape=new StreamGeometry();using(var context=shape.Open()){context.BeginFigure(outline[0],false,true);context.PolyLineTo(outline.Skip(1).ToArray(),true,true);}shape.Freeze();dc.DrawGeometry(null,pen,shape);
                }
                Outline(foundation.MarginMeters+foundation.FalloffMeters,new Pen(Brushes.DarkSeaGreen,1){DashStyle=DashStyles.Dot});
                Outline(foundation.MarginMeters,new Pen(Brushes.OliveDrab,1){DashStyle=DashStyles.Dash});
            }
            var corners=SceneryObjectGeometry.FootprintCorners(displayed,asset).Select(Screen).ToArray();
            var geometry=new StreamGeometry();using(var context=geometry.Open()){context.BeginFigure(corners[0],true,true);context.PolyLineTo(corners.Skip(1).ToArray(),true,true);}
            geometry.Freeze();dc.DrawGeometry(selected?Brushes.Gold:Brushes.Goldenrod,new Pen(selected?Brushes.DarkRed:Brushes.SaddleBrown,selected?2:1),geometry);
            var forward=Screen(SceneryObjectGeometry.TransformModelPoint(displayed,0,Math.Min(25,asset.Footprint.MaximumForwardMeters)));
            dc.DrawLine(new Pen(Brushes.DarkRed,2),originPoint,forward);dc.DrawEllipse(Brushes.DarkRed,null,originPoint,3,3);
            Text(dc,$"H{item.Number}",new(originPoint.X+5,originPoint.Y+3),Brushes.DarkRed);
        }
        dc.DrawRectangle(Brushes.White,null,new(8,8,235,45));
        Text(dc,model.Survey==null?"Capture a survey to edit":model.HasRunway?"Surveyed runway centre • north up":"Operation anchor • no runway",new(15,12),Brushes.DarkGreen);
        Text(dc,$"Grid {step:0.##} m  |  zoom {viewport.PixelsPerMeter:0.##} px/m",new(15,32),Brushes.DimGray);
        Text(dc,"N ↑",new(ActualWidth-50,15),Brushes.DarkGreen);
        double metres=Math.Pow(10,Math.Floor(Math.Log10(100/viewport.PixelsPerMeter)));
        if(metres*viewport.PixelsPerMeter<40)metres*=5;
        double bar=metres*viewport.PixelsPerMeter;
        dc.DrawLine(new Pen(Brushes.DarkGreen,3),new(ActualWidth-bar-20,ActualHeight-22),new(ActualWidth-20,ActualHeight-22));
        Text(dc,$"{metres:0.##} m",new(ActualWidth-bar-20,ActualHeight-43),Brushes.DarkGreen);
    }
    private void Text(DrawingContext dc,string text,Point p,Brush brush)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),p);
    private void DrawPath(DrawingContext dc,IReadOnlyList<LocalPoint> points,Brush brush,double width)
    {
        var pen=new Pen(brush,Math.Max(2,width*viewport.PixelsPerMeter)){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        for(int i=1;i<points.Count;i++)dc.DrawLine(pen,Screen(points[i-1]),Screen(points[i]));
        foreach(var p in points)dc.DrawEllipse(Brushes.White,new Pen(brush,1),Screen(p),3,3);
    }
    private static double SegmentDistance(LocalPoint p,LocalPoint a,LocalPoint b)
    {
        double e=b.East-a.East,n=b.North-a.North,d=e*e+n*n;
        double t=d==0?0:Math.Clamp(((p.East-a.East)*e+(p.North-a.North)*n)/d,0,1);
        return TaxiwayGeometry.Distance(p,new(a.East+t*e,a.North+t*n));
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();lastMouse=e.GetPosition(this);
        if(e.ChangedButton is MouseButton.Middle or MouseButton.Right){panning=true;CaptureMouse();e.Handled=true;return;}
        if(e.ChangedButton!=MouseButton.Left||model==null||!model.CanLayout)return;
        var point=Local(lastMouse);
        if(model.IsPlacingRunway)
        {
            model.PlaceRunwayEnd(point);e.Handled=true;return;
        }
        var hit=model.Layout.Stands.Reverse().FirstOrDefault(s=>Math.Sqrt(Math.Pow(s.EastMeters-point.East,2)+Math.Pow(s.NorthMeters-point.North,2))<=Math.Max(s.RadiusMeters,8/viewport.PixelsPerMeter));
        if(model.Layout.IsDrawingTaxiway)
        {
            // Screen tolerance selects a stand; the committed location is its exact metre-based position.
            model.Layout.AddTaxiwayPoint(hit==null?point:new(hit.EastMeters,hit.NorthMeters));e.Handled=true;return;
        }
        var objectHit=model.Layout.Objects.Reverse().FirstOrDefault(o=>
        {
            var asset=SceneryAssets.Find(o.AssetId);return asset!=null&&SceneryObjectGeometry.ContainsFootprint(o,asset,point,8/viewport.PixelsPerMeter);
        });
        if(objectHit!=null)
        {
            model.Layout.SelectObject(objectHit.Number);dragging=objectHit.Number;draggingObject=true;dragStart=point;itemStart=new(objectHit.EastMeters,objectHit.NorthMeters);ghost=itemStart;CaptureMouse();
        }
        else if(hit!=null)
        {
            model.Layout.Select(hit.Number);dragging=hit.Number;draggingObject=false;dragStart=point;itemStart=new(hit.EastMeters,hit.NorthMeters);ghost=itemStart;CaptureMouse();
        }
        else
        {
            model.Layout.Select(null);
            var taxi=model.Layout.Taxiways.Reverse().FirstOrDefault(t=>Enumerable.Range(1,t.Points.Count-1).Any(i=>
                SegmentDistance(point,t.Points[i-1].Resolve(model.Layout.Stands),t.Points[i].Resolve(model.Layout.Stands))<=Math.Max(t.WidthMeters/2,8/viewport.PixelsPerMeter)));
            if(taxi!=null)model.Layout.SelectTaxiway(taxi.Number);
            else if(e.ClickCount==2)model.AddParkingAt(point);
        }
        e.Handled=true;InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point=e.GetPosition(this);
        if(panning){viewport.Pan(point.X-lastMouse.X,point.Y-lastMouse.Y);lastMouse=point;InvalidateVisual();ViewChanged?.Invoke(this,EventArgs.Empty);}
        else if(dragging.HasValue){var local=Local(point);ghost=new(itemStart.East+local.East-dragStart.East,itemStart.North+local.North-dragStart.North);InvalidateVisual();}
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if(dragging is {} id&&ghost is {} point)
        {
            if(draggingObject)model?.Layout.MoveObject(id,point);else model?.Layout.Move(id,point);
        }
        dragging=null;draggingObject=false;ghost=null;panning=false;ReleaseMouseCapture();InvalidateVisual();e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e){dragging=null;draggingObject=false;ghost=null;panning=false;InvalidateVisual();base.OnLostMouseCapture(e);}
    protected override void OnMouseWheel(MouseWheelEventArgs e){var p=e.GetPosition(this);viewport.Zoom(e.Delta>0?1.2:1/1.2,p.X,p.Y,ActualWidth,ActualHeight);InvalidateVisual();ViewChanged?.Invoke(this,EventArgs.Empty);e.Handled=true;}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(model==null)return;
        if(e.Key==Key.Escape){model.Layout.CancelTaxiway();model.CancelRunwayPlacement();dragging=null;ghost=null;panning=false;ReleaseMouseCapture();}
        else if(e.Key==Key.Enter&&model.Layout.IsDrawingTaxiway)model.Layout.FinishTaxiway();
        else if(e.Key==Key.Delete)model.Layout.Delete();
        else if(e.Key==Key.Z&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control))model.Layout.Undo();
        else if(e.Key==Key.Y&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control))model.Layout.Redo();
        else if(e.Key==Key.R)model.Layout.Rotate(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?-15:15);
        else if(e.Key==Key.Home)Fit();
        else {base.OnKeyDown(e);return;}
        e.Handled=true;InvalidateVisual();
    }
}
