using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

using MouseHandler = MouseToJoystick2.MouseToJoystickFps;


namespace MouseToJoystick2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MouseHandler? handler = null;

        public MainWindow()
        {
            Debug.WriteLine($"{Cia.Exe.Util.Tid()} init()");
            InitializeComponent();
            //Closing += OnWindowClosing;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Release();
            base.OnClosing(e);
        }

        private void Release()
        {
            if (this.handler != null)
            {
                this.handler.Dispose();
                this.handler = null;
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var model = (MainWindowModel)this.DataContext;

            if (model.ShouldRun == true)
            {
                uint deviceId = Convert.ToUInt32(model.DeviceId);
                int manualWidth = Convert.ToInt32(model.ScreenWidth);
                int manualHeight = Convert.ToInt32(model.ScreenHeight);
                try
                {
                    handler = new MouseHandler(deviceId);
                    //handler = new MouseHandler(deviceId, model.InvertX, model.InvertY, model.AutoCenter, model.AutoScreenSize, manualWidth, manualHeight);
                    model.SettingsEnabled = false;
                }
                catch (Exception err)
                {
                    Console.WriteLine("Whoops!");
                    System.Windows.Forms.MessageBox.Show(err.Message);
                    model.ShouldRun = false;
                }
            }
            else
            {
                Release();
                model.SettingsEnabled = true;
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            new OSSInfoWindow().Show();
        }
    }
}
