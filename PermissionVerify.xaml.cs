using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace FingerPrintApp
{
    /// <summary>
    /// PermissionVerify.xaml 的交互逻辑
    /// </summary>
    public partial class PermissionVerify : Window
    {
        public bool Verify { get; set; }
        public bool Cancel { get; set; }
        public string PermissionPassword { get; set; }
        private string verifyUser = "";

        public string VerifyUser
        {
            get { return verifyUser; }
            set { verifyUser = value;
                this.Dispatcher.Invoke(new Action(() =>
                {
                    User.Text = VerifyUser;
                }));
                }
        }

        public PermissionVerify()
        {
            InitializeComponent();
            Info.Text = "Please entry permission password!";
        }

        private void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            var _mainWindow = Application.Current.Windows.Cast<Window>().FirstOrDefault(window => window is MainWindow) as MainWindow;
            PermissionPassword = Password.Text;
            if (_mainWindow.AdsCom.Login(VerifyUser,PermissionPassword) == TcAdsCom.HmiUtilAddonLoginState.Success)
            {
                _mainWindow.AdsCom.LogOff(VerifyUser);
                Verify = true;
                this.Close();
            }else
            {
                Info.Text = "Wrong password! please enter again";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Cancel = true;
            this.Close();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
}
