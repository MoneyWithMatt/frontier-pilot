using System.Windows.Controls;
using System.Windows.Data;
namespace FrontierAirportBuilder.Desktop;
public partial class AirportReviewControl : UserControl
{
    private string lastStatus="";
    public AirportReviewControl()=>InitializeComponent();
    private void ShowSavedStatus(object sender,DataTransferEventArgs e)
    {
        if(sender is not TextBlock status||status.Text==lastStatus)return;
        lastStatus=status.Text;if(lastStatus.Length>0)status.BringIntoView();
    }
}
