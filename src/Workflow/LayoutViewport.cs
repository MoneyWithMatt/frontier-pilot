using FrontierAirportBuilder.Core;
namespace FrontierAirportBuilder.Workflow;

// View-only projection. No geographic calculation consumes screen pixels.
public sealed class LayoutViewport
{
    public double PixelsPerMeter { get; private set; } = 1;
    public double PanX { get; private set; }
    public double PanY { get; private set; }
    public (double X,double Y) ToScreen(LocalPoint p,double width,double height) => (width/2+PanX+p.East*PixelsPerMeter,height/2+PanY-p.North*PixelsPerMeter);
    public LocalPoint ToLocal(double x,double y,double width,double height) => new((x-width/2-PanX)/PixelsPerMeter,-(y-height/2-PanY)/PixelsPerMeter);
    public void Pan(double dx,double dy) { if(double.IsFinite(dx+dy)){PanX+=dx;PanY+=dy;} }
    public void Zoom(double factor,double x,double y,double width,double height)
    {
        if(!double.IsFinite(factor)||factor<=0)return;
        var fixedPoint=ToLocal(x,y,width,height);
        PixelsPerMeter=Math.Clamp(PixelsPerMeter*factor,.02,20);
        PanX=x-width/2-fixedPoint.East*PixelsPerMeter;PanY=y-height/2+fixedPoint.North*PixelsPerMeter;
    }
    public void Fit(double extentMeters,double width,double height)
    {
        if(!double.IsFinite(extentMeters)||extentMeters<=0)return;
        PixelsPerMeter=Math.Clamp(Math.Min(width,height)*.8/extentMeters,.02,20);PanX=PanY=0;
    }
}
