using libzkfpcsharp;
using System;
using System.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Data.SQLite;
using System.Collections.ObjectModel;
using System.Xml.Linq;
using System.Linq;
using System.Windows.Forms;

namespace FingerPrintApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private static NotifyIcon notifyIcon;
        private WindowState initWs;
        private WindowState lastWs;

        private TcAdsCom adsCom;
        public TcAdsCom AdsCom { get { return adsCom; } }

        SQLiteConnection connection;
        SQLiteCommand sqliteCmd;
        public ObservableCollection<Account> Accounts  { get; set; }
        string registerAccount;
        string accountPermission;
        string accountPermissionPassword;

        Thread captureThread = null;
        DispatcherTimer pressTimer = null;
        bool deleteItem = false;
        
        IntPtr mDevHandle = IntPtr.Zero;
        IntPtr mDBHandle = IntPtr.Zero;

        int deviceId = 0;
        string hmiUtilVarName = "";
        string hmiUserFileDirectory = "";
        string amsId = "";
        int port = 0;

        int RegisterCount = 0;
        byte[][] RegTmps = new byte[3][];
        byte[] RegTmp = new byte[2048];
        byte[] CapTmp = new byte[2048];
        int cbCapTmp = 2048;
        int cbRegTmp = 0;
        private int mfpWidth = 0;
        private int mfpHeight = 0;
        bool IsRegister = false;
        byte[] FPBuffer;

        DeviceStatus deviceStatus = DeviceStatus.Init;

        const int REGISTER_FINGER_COUNT = 3;
        const int MESSAGE_CAPTURED_OK = 0x0400 + 6;

        [DllImport("user32.dll", EntryPoint = "SendMessageA")]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        public MainWindow()
        {
            InitializeComponent();
            notifyIcon = new NotifyIcon();
            notifyIcon.BalloonTipText = "Finger print";
            notifyIcon.Text = "Finger print";
            notifyIcon.Icon = new System.Drawing.Icon("Fingerprint.ico");
            notifyIcon.Visible = true;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;
            this.StateChanged += MainWindow_StateChanged;
            initWs = WindowState;
            contextMenu();

            deviceId = Convert.ToInt32(ConfigurationManager.AppSettings["DeviceId"]);
            hmiUserFileDirectory = (ConfigurationManager.AppSettings["HmiUserFileDirectory"]);
            hmiUtilVarName = (ConfigurationManager.AppSettings["HmiUtilVarName"]);
            amsId = (ConfigurationManager.AppSettings["AmsId"]);
            port = Convert.ToInt32((ConfigurationManager.AppSettings["Port"]));

            connection = new SQLiteConnection("Data source=Accounts.sqlite;");
            ConnectDBtable();
            adsCom = new TcAdsCom(amsId, port, hmiUtilVarName);
            captureThread = new Thread(CaptureRun);
            captureThread.IsBackground = true;
            captureThread.Start();
            pressTimer = new DispatcherTimer();
            pressTimer.Tick += new EventHandler(pressTimerTick);
            pressTimer.Interval = new TimeSpan(0, 0, 3);
        }

        private void contextMenu()
        {
            ContextMenuStrip cms = new ContextMenuStrip();
            System.Windows.Forms.ToolStripMenuItem exitMenuItem = new ToolStripMenuItem();
            exitMenuItem.Text = "Exit";
            exitMenuItem.Click += new EventHandler(delegate (Object o, EventArgs e)
            {
                notifyIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });
            cms.Items.Add(exitMenuItem);
            notifyIcon.ContextMenuStrip = cms;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            lastWs = WindowState;
            if (lastWs == WindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Left)
            {
                this.Show();
                WindowState = initWs;
            }
        }

        private void ConnectDBtable()
        {
            connection.Open();
            sqliteCmd = connection.CreateCommand();
            sqliteCmd.CommandText =
                @"CREATE TABLE IF NOT EXISTS
                [Accounts](
                    [ID]  INTEGER ,
                    [Name] TEXT NOT NULL,
                    [Permission] TEXT NOT NULL,
                    [PermissionPassword] TEXT NOT NULL,
                    [FingerData] BOLB NOT NULL)";
            sqliteCmd.ExecuteNonQuery();
        }

        private void CaptureRun()
        {
            int ret = zkfperrdef.ZKFP_ERR_OK;
            while (true)
            {
                switch (deviceStatus)
                {
                    case DeviceStatus.Init:
                        {
                            if ((ret = zkfp2.Init()) == zkfperrdef.ZKFP_ERR_OK)
                            {
                                int nCount = zkfp2.GetDeviceCount();
                                if (nCount > 0)
                                {
                                    if (nCount >= deviceId)
                                    {
                                        this.Dispatcher.Invoke(new Action(() =>
                                        { this.MessageBox.Text = "Info : Connected."; }));
                                        deviceStatus = DeviceStatus.SelectDevice;
                                    }
                                    else
                                    {
                                        this.Dispatcher.Invoke(new Action(() =>
                                        { this.MessageBox.Text = "Info : Can not find device."; })); ;
                                        Thread.Sleep(3000);
                                    }
                                }
                                else
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    { this.MessageBox.Text = "Info : No device connect"; })); ;
                                    Thread.Sleep(3000);
                                }
                            }
                            else
                            {
                                this.Dispatcher.Invoke(new Action(() =>
                                { this.MessageBox.Text = "Info : Init failed"; })); ;
                                Thread.Sleep(3000);
                            }
                        }
                        break;
                    case DeviceStatus.SelectDevice:
                        {
                            if (IntPtr.Zero == (mDevHandle = zkfp2.OpenDevice(deviceId - 1)))
                            {
                                this.Dispatcher.Invoke(new Action(() =>
                                { this.MessageBox.Text = "Info : Open device failed.Device id = " + deviceId.ToString(); }));
                                break;
                            }
                            if (IntPtr.Zero == (mDBHandle = zkfp2.DBInit()))
                            {
                                this.Dispatcher.Invoke(new Action(() =>
                                { this.MessageBox.Text = "Info : DB init failed "; }));
                                zkfp2.CloseDevice(mDevHandle);
                                mDevHandle = IntPtr.Zero;
                                break;
                            }
                            RegisterCount = 0;
                            cbRegTmp = 0;
                            for (int i = 0; i < 3; i++)
                            {
                                RegTmps[i] = new byte[2048];
                            }
                            byte[] paramValue = new byte[4];
                            int size = 4;
                            zkfp2.GetParameters(mDevHandle, 1, paramValue, ref size);
                            zkfp2.ByteArray2Int(paramValue, ref mfpWidth);
                            zkfp2.GetParameters(mDevHandle, 2, paramValue, ref size);
                            zkfp2.ByteArray2Int(paramValue, ref mfpHeight);
                            FPBuffer = new byte[mfpWidth * mfpHeight];
                            UpdateAccount();
                            this.Dispatcher.Invoke(new Action(() =>
                            { this.MessageBox.Text = "Info : Open successfully "; }));
                            deviceStatus = DeviceStatus.Capture;
                        }
                        break;
                    case DeviceStatus.Capture:
                        {
                            cbCapTmp = 2048;
                            ret = zkfp2.AcquireFingerprint(mDevHandle, FPBuffer, CapTmp, ref cbCapTmp);
                            if (ret == zkfp.ZKFP_ERR_OK)
                            {
                                deviceStatus = DeviceStatus.UpdateImage;
                            }
                        }
                        break;
                    case DeviceStatus.UpdateImage:
                        {
                            MemoryStream ms = new MemoryStream();
                            BitmapFormat.GetBitmap(FPBuffer, mfpWidth, mfpHeight, ref ms);
                            BitmapImage bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = ms;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            this.Dispatcher.BeginInvoke(new Action(() =>
                            { this.picFPImg.Source = bitmap; }));
                            deviceStatus = DeviceStatus.EvalData;
                        }
                        break;
                    case DeviceStatus.EvalData:
                        {
                            if (IsRegister)
                            {
                                ret = zkfp.ZKFP_ERR_OK;
                                int fid = 0, score = 0;
                                ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                                if (zkfp.ZKFP_ERR_OK == ret)
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    { this.MessageBox.Text = "Info : This finger was already register by " + fid + "!"; }));
                                }
                                if (RegisterCount > 0 && zkfp2.DBMatch(mDBHandle, CapTmp, RegTmps[RegisterCount - 1]) <= 0)
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    { this.MessageBox.Text = "Info : Please press the same finger 3 times for the enrollment"; }));
                                }
                                Array.Copy(CapTmp, RegTmps[RegisterCount], cbCapTmp);
                                RegisterCount++;
                                if (RegisterCount >= REGISTER_FINGER_COUNT)
                                {
                                    RegisterCount = 0;
                                    if (zkfp.ZKFP_ERR_OK == (ret = zkfp2.DBMerge(mDBHandle, RegTmps[0], RegTmps[1], RegTmps[2], RegTmp, ref cbRegTmp)))
                                    {
                                        RemoveAccount(registerAccount);
                                        sqliteCmd.CommandText = "INSERT INTO Accounts(ID,Name,Permission,PermissionPassword,FingerData) VALUES " + 
                                                                "(@ID,@Name,@Permission,@PermissionPassword,@FingerData);";
                                        sqliteCmd.Parameters.AddWithValue("ID", 0);
                                        sqliteCmd.Parameters.AddWithValue("Name", registerAccount);
                                        sqliteCmd.Parameters.AddWithValue("Permission", accountPermission);
                                        sqliteCmd.Parameters.AddWithValue("PermissionPassword",accountPermissionPassword);
                                        sqliteCmd.Parameters.AddWithValue("FingerData", RegTmp);
                                        try
                                        {
                                            sqliteCmd.ExecuteNonQuery();
                                        }
                                        catch (Exception e)
                                        {
                                            throw new Exception(e.Message);
                                        }
                                        this.Dispatcher.Invoke(new Action(() =>
                                        {
                                            UpdateAccount();
                                            this.MessageBox.Text = "Info : enroll successfully";
                                            this.RegisterButton.Content = "Register";
                                        }));
                                    }
                                    else
                                    {
                                        this.Dispatcher.Invoke(new Action(() =>
                                        {
                                            this.MessageBox.Text = "Info : enroll fail, error code=" + ret;
                                            this.RegisterButton.Content = "Register";
                                        }));
                                    }
                                    IsRegister = false;
                                }
                                else
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    {
                                        this.MessageBox.Text = "Info : You need to press the " +
                                          (REGISTER_FINGER_COUNT - RegisterCount) + " times fingerprint";
                                    }));
                                }
                            }
                            else
                            {
                                RegisterCount = 0;
                                if (cbRegTmp <= 0)
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    {
                                        this.MessageBox.Text = "Info : Please register your finger first!";
                                    }));
                                }
                                ret = zkfp.ZKFP_ERR_OK;
                                int fid = 0, score = 0;
                                ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                                if (zkfp.ZKFP_ERR_OK == ret)
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    {
                                        foreach (var account in Accounts)
                                        {
                                            if (account.ID == fid)
                                            {
                                                this.CurAccount.Text = account.Name;
                                                adsCom.Login(account.Permission, GetPermissionPassword(account.Name));
                                                break;
                                            }
                                        }
                                        this.MessageBox.Text = "Info : Identify succ, fid = " + fid + ", score = " + score + "!";
                                    }));
                                }
                                else
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    {
                                        this.MessageBox.Text = "Info : Identify fail, ret = " + ret;
                                    }));
                                }
                            }
                            deviceStatus = DeviceStatus.Capture;
                        }
                        break;

                }
            }
        }

        private void RemoveAccount(string Account)
        {
            sqliteCmd.CommandText = string.Format("DELETE FROM {0} WHERE Name='{1}';", "Accounts", @Account);
            sqliteCmd.ExecuteNonQuery();
        }

        private string GetPermissionPassword(string Account)
        {
            sqliteCmd.CommandText = string.Format(@"SELECT * FROM Accounts WHERE Name='{0}';", Account);
            SQLiteDataReader reader = sqliteCmd.ExecuteReader();
            while (reader.Read())
            {
                if(reader.GetString(1) == Account)
                {
                    string password = reader.GetString(3);
                    reader.Close();
                    return password;
                }
            }
            reader.Close();
            return "";
        }

        private void UpdateAccount()
        {
            zkfp2.DBClear(mDBHandle);
            sqliteCmd.CommandText = @"SELECT * FROM Accounts";
            SQLiteDataReader reader = sqliteCmd.ExecuteReader();
            Accounts = new ObservableCollection<Account>();
            int increment = 1;
            while (reader.Read())
            {
                if(reader.HasRows)
                {
                    Account account = new Account();
                    account.ID = increment;
                    account.Name = reader.GetString(1);
                    account.Permission = reader.GetString(2);
                    zkfp2.DBAdd(mDBHandle,increment,(byte[])reader["FingerData"]);
                    Accounts.Add(account);
                    increment++;
                }
            }
            reader.Close();
            foreach (var account in Accounts)
            {
                sqliteCmd.CommandText = string.Format("UPDATE Accounts SET ID={0} WHERE Name='{1}';", account.ID, account.Name);
                sqliteCmd.ExecuteNonQuery();
            }
            this.Dispatcher.Invoke(new Action(() =>
            {
                AccountView.ItemsSource = Accounts;
            }));
        } 
        
        enum DeviceStatus
        {
            Init,
            SelectDevice,
            Capture,
            UpdateImage,
            EvalData,
        }

        private void WindowMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRegister && !deleteItem)
            {
                if (SelectPermission.SelectedItem != null &&
                    (string)SelectPermission.SelectedItem != "" &&
                    RegisterAccount.Text != "")
                {
                    registerAccount = RegisterAccount.Text;
                    accountPermission = (string)SelectPermission.SelectedItem;
                    var verifyWindow = new PermissionVerify();
                    verifyWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    verifyWindow.VerifyUser = accountPermission;
                    verifyWindow.ShowDialog();
                    if (verifyWindow.Verify)
                    {
                        accountPermissionPassword = verifyWindow.PermissionPassword;
                        IsRegister = true;
                        RegisterButton.Content = "Cancel";
                        MessageBox.Text = "Info : Please press the same finger 3 times for the enrollment";
                    }
                    else if(verifyWindow.Cancel)
                    {
                        MessageBox.Text = "Info : Cancel verify permission";
                    }else
                    {
                        MessageBox.Text = "Info : Permission password is worng";
                    }
                }else
                {
                    if ( SelectPermission.SelectedItem == null ||
                        (string)SelectPermission.SelectedItem == "")
                    {
                        MessageBox.Text = "Info : Select permission is empty.";
                    }else
                    {
                        MessageBox.Text = "Info : Account name is empty.";
                    }
                }
            }
            else
            {
                RegisterButton.Content = "Register";
                IsRegister = false;
            }
            if (deleteItem)
            {
                deleteItem = false;
            }
        }

        private void SelectPermission_MouseDown(object sender, MouseButtonEventArgs e)
        {
            XDocument document = XDocument.Load(hmiUserFileDirectory + "HmiUsers.xml");
            var permissions = from permission in document.Descendants("User")
                              select permission.Attribute("name").Value;
            foreach(var permission in permissions)
            {
                if(!SelectPermission.Items.Contains(permission))
                {
                    SelectPermission.Items.Add(permission);
                }
            }
        }

        private void RegisterButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if(e.LeftButton == MouseButtonState.Pressed)
            {
                pressTimer.Start();
            }
        }

        private void RegisterButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            pressTimer.Stop();
        }

        private void pressTimerTick(object sender, EventArgs e)
        {
            deleteItem = true;
            if (AccountView.SelectedIndex >= 0)
            {
                RemoveAccount(Accounts[AccountView.SelectedIndex].Name);
                this.Dispatcher.Invoke(new Action(() =>
                {
                    UpdateAccount();
                    AccountView.SelectedIndex = -1;
                    this.MessageBox.Text = "Info : Delete successfully";
                }));
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}
