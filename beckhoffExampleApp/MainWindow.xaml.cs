using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using System.Timers;
using TwinCAT.Ads;
using System.Text.RegularExpressions;

namespace beckhoffExampleApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    [ToolboxItem(true)]
    class Indicator : Label
    {
        public Indicator()
        {
            this.Width = 100;
            this.Height = 25;
            //TODO
            this.OnColor = (Brush)new BrushConverter().ConvertFromString("#FF4FF40E");  //Green
            this.OffColor = (Brush)new BrushConverter().ConvertFromString("#FFA2BD97");  //Dark Grey
        }

        [Description("The BOOL Variable in the PLC to read from"),
         Category("PLC")]
        public string VariableName { get; set; }

        [Description("Color when the Variable is TRUE"),
         Category("PLC")]
        public Brush OnColor { get; set; }

        [Description("Color when the Variable is FALSE"),
         Category("PLC")]
        public Brush OffColor { get; set; }
    }


    class MomentaryButton : Button
    {
        [Description("The BOOL Variable in the PLC to write to"), Category("PLC")]
        public string VariableName { get; set;  }
    }

    class CommunicationManager : IDisposable
    {
        private readonly int port;
        private readonly TcAdsClient client = new TcAdsClient();
        private readonly List<Action> pollActions
                         = new List<Action>();
        private readonly Dictionary<string, DateTime> readWriteErrors
                         = new Dictionary<string, DateTime>();
        private bool connected;
        private DateTime? lastErrorTime = null;

        public CommunicationManager(int port)
        {
            this.port = port;
        }

        public void Poll()
        {
            foreach (var action in this.pollActions)
            {
                action();
            }
        }

        public bool IsConnected
        {
            get { return this.connected; }
        }

        public ReadOnlyCollection<string> GetReadWriteErrors()
        {
            var result = this.readWriteErrors.Keys
                .OrderBy(x => x)
                .ToList();
            return result.AsReadOnly();
        }

        public void Register(Control control)
        {
            if (control == null) return;
            if (control is MomentaryButton)
            {
                registerMB(control as MomentaryButton);
            }
            else if (control is Indicator)
            {
                register(control as Indicator);
            }
        }

        private void registerMB(MomentaryButton momentaryButton)
        {
            momentaryButton.PreviewMouseLeftButtonDown += (s, e) =>
            {
                
                tryConnect();

                if (connected)
                {
                    try
                    {
                        client.WriteSymbol(momentaryButton.VariableName, true, reloadSymbolInfo: true);
                        readWriteSuccess(momentaryButton.VariableName);
                    }
                    catch (AdsException)
                    {
                        readWriteError(momentaryButton.VariableName);
                    }
                }


            };
            momentaryButton.PreviewMouseLeftButtonUp += (s, e) =>
            {
                
                tryConnect();

                if (connected)
                {
                    try
                    {
                        client.WriteSymbol(momentaryButton.VariableName, false, reloadSymbolInfo: true);
                        readWriteSuccess(momentaryButton.VariableName);
                    }
                    catch (AdsException)
                    {
                        readWriteError(momentaryButton.VariableName);
                    }
                }
            };
        }

        private void register(Indicator indicator)
        {
            this.pollActions.Add(() =>
            {
                this.doWithClient(c =>
                {
                    if (string.IsNullOrWhiteSpace(
                        indicator.VariableName))
                    {
                        return;
                    }
                    bool value = (bool)c.ReadSymbol(
                        indicator.VariableName, typeof(bool),
                        reloadSymbolInfo: true);
                    indicator.Background = value
                        ? indicator.OnColor
                        : indicator.OffColor;
                }, indicator.VariableName);
            });
        }

        private void doWithClient(
            Action<TcAdsClient> action,
            string variableName)
        {
            tryConnect();
            if (connected)
            {
                try
                {
                    action(client);
                    readWriteSuccess(variableName);
                }
                catch (AdsException)
                {
                    readWriteError(variableName);
                }
            }
        }

        private void tryConnect()
        {
            if (!connected)
            {
                if (lastErrorTime.HasValue)
                {
                    // wait a bit before re-establishing connection
                    var elapsed = DateTime.Now
                        .Subtract(lastErrorTime.Value);
                    if (elapsed.TotalMilliseconds < 3000)
                    {
                        return;
                    }
                }
                try
                {
                    client.Connect(port);
                    connected = client.IsConnected;
                }
                catch (AdsException)
                {
                    connectError();
                }
            }
        }

        private void connectError()
        {
            connected = false;
            lastErrorTime = DateTime.Now;
        }

        private void readWriteSuccess(string variableName)
        {
            if (this.readWriteErrors.ContainsKey(variableName))
            {
                this.readWriteErrors.Remove(variableName);
            }
        }

        private void readWriteError(string variableName)
        {
            if (this.readWriteErrors.ContainsKey(variableName))
            {
                this.readWriteErrors[variableName] = DateTime.Now;
            }
            else
            {
                this.readWriteErrors.Add(variableName, DateTime.Now);
            }
        }

        public void Dispose()
        {
            this.client.Dispose();
        }
    }

    public partial class MainWindow : Window
    {
        private readonly CommunicationManager communicationManager
                         = new CommunicationManager(851);

        private System.Windows.Forms.Timer loopTimer;
        private System.Windows.Forms.Timer loopTime;
        private System.ComponentModel.IContainer components;
        private TwinCAT.Ads.TcAdsClient clientBool = new TwinCAT.Ads.TcAdsClient();

        public MainWindow()
        {
            InitializeComponent();

            /// casting the content into panel
            Panel mainContainer = (Panel)this.Content;
            /// GetAll UIElement
            UIElementCollection element = mainContainer.Children;
            /// casting the UIElementCollection into List
            List<FrameworkElement> lstElement = element.Cast<FrameworkElement>().ToList();
            /// Getting all Control from list
            var lstControl = lstElement.OfType<Control>();
            foreach (var control in lstControl)
            {
                this.communicationManager
                    .Register(control as Control);
            }


            this.components = new System.ComponentModel.Container();
            this.loopTime = new System.Windows.Forms.Timer(this.components);
            this.loopTimer = new System.Windows.Forms.Timer(this.components);
            // loopTime
            //
            loopTime.Tick += new System.EventHandler(this.loopTime_Tick);

            this.loopTimer.Tick += (s, e) =>
            {
                this.communicationManager.Poll();
            };

           
            this.clientBool = new TwinCAT.Ads.TcAdsClient();
            this.clientBool.Connect(851);

            

            
            this.loopTimer.Enabled = true;
            this.loopTime.Enabled = true;
            this.loopTimer.Start();

            
        }

        //timer loop to get info IO from NI PCI-6520 IO card  *INPUT*
        private void loopTime_Tick(object sender, System.EventArgs e)
        {

                //var stateIndicator = (bool)clientBool.ReadSymbol("MyGVL.MyBoolVar", typeof(bool), reloadSymbolInfo: true);
                //if (stateIndicator == true)
                //{
                //    indicator.Background = (Brush)new BrushConverter().ConvertFromString("#FF4FF40E");  //Green
                //}
                //else {
                //    indicator.Background = (Brush)new BrushConverter().ConvertFromString("#FFA2BD97");  //Dark grey
                //}

            //var stringVar = (int)clientBool.ReadSymbol("MAIN.test", typeof(int), reloadSymbolInfo: true);
            var stringVar = (bool)clientBool.ReadSymbol("P_Motion2.ati_xStart", typeof(bool), reloadSymbolInfo: true);
            var stringErrorCode = (uint)clientBool.ReadSymbol("P_Motion2.stAxis.NcToPlc.ErrorCode", typeof(uint), reloadSymbolInfo: true);
            var stringPLCResult = (string)clientBool.ReadSymbol("P_Motion2.sResult", typeof(string), reloadSymbolInfo: true);


            label.Content = stringVar.ToString();
            labelErrorCode.Content = "error code: " + stringErrorCode.ToString();
            labelPLCResult.Content = "PLC Program Result: " + stringPLCResult;


        }


        private void buttonTrue_Click(object sender, RoutedEventArgs e)
        {
            using (var client = new TwinCAT.Ads.TcAdsClient())
            {

                client.Connect(851);


                // creates a stream with a length of 4 byte 
                AdsStream ds = new AdsStream(4);
                BinaryReader br = new BinaryReader(ds);

                // reads a DINT from PLC
                client.Read(0x4020, 0x0001, ds);

                ds.Position = 0;
                label1.Content = br.ReadInt32().ToString();

                //client.WriteSymbol("MyGVL.MyBoolVar", true, reloadSymbolInfo: true);
                client.WriteSymbol("P_Motion2.ati_xStart", true, reloadSymbolInfo: true);

                client.Dispose();

            }
        }

        private void buttonFalse_Click(object sender, RoutedEventArgs e)
        {
            using (var client = new TwinCAT.Ads.TcAdsClient())
            {
                try
                {
                    client.Connect(851);

                    // creates a stream with a length of 4 byte
                    AdsStream ds = new AdsStream(4);
                    BinaryWriter bw = new BinaryWriter(ds);

                    ds.Position = 0;

                    bw.Write(Convert.ToInt32(textBox.Text));

                    // writes a DINT to PLC
                    client.Write(0x4020, 0x0001, ds);

                    //client.WriteSymbol("MyGVL.MyBoolVar", false, reloadSymbolInfo: true);
                    client.WriteSymbol("P_Motion2.ati_xStart", false, reloadSymbolInfo: true);

                    client.Dispose();
                }
                catch (TwinCAT.Ads.AdsErrorException ex)
                {
                    MessageBox.Show("PLC Control Error: \r\n\r\n" + ex.Message);
                    
                }
                catch (System.FormatException ex)
                {
                    textBoxPLCSetPosition.Text = "0";
                    MessageBox.Show("Invalid float value for Position. ErrMsg: \r\n\r\n" + ex.Message);
                }


            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            clientBool.Dispose();
        }

        private new void PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private static bool IsTextAllowed(string text)
        {
            
            //Regex regex = new Regex("[^0-9.-]+"); //regex that matches disallowed text
            Regex regex = new Regex("[^0-9.-]+");

                return !regex.IsMatch(text);

            
        }

        
        private void textBoxPLCSetPosition_TextChanged(object sender, TextChangedEventArgs e)
        {
            using (var client = new TwinCAT.Ads.TcAdsClient())
            {
                try
                {
                    client.Connect(851);
                    if (textBoxPLCSetPosition.Text == "")
                    {
                        return;
                    }
                    double dPos = Convert.ToDouble(textBoxPLCSetPosition.Text);
                    

                    //client.WriteSymbol("MyGVL.MyBoolVar", false, reloadSymbolInfo: true);
                    client.WriteSymbol("P_Motion2.stAxis.PlcToNc.ExtSetPos", dPos, reloadSymbolInfo: true);

                    client.Dispose();
                }
                catch (TwinCAT.Ads.AdsErrorException ex)
                {
                    MessageBox.Show("PLC Control Error: \r\n\r\n" + ex.Message);
                    //Application.Current.Shutdown();
                }
                catch (System.FormatException ex)
                {
                    textBoxPLCSetPosition.Text = "0";
                    MessageBox.Show("Invalid float value for Position. ErrMsg: \r\n\r\n" + ex.Message);
                }

            } 
        }

        private void textBoxPLCSetPosition_GotFocus(object sender, RoutedEventArgs e)
        {
            textBoxPLCSetPosition.SelectAll();
        }

        
    }
}
