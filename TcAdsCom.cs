using System;
using System.Text;
using System.Threading;
using TwinCAT.Ads;

namespace FingerPrintApp
{
    public class TcAdsCom
    {
        private ConnectionStatusEnum connectionStatus = ConnectionStatusEnum.CheckCfg;
        public ConnectionStatusEnum ConnectionStatus { get { return connectionStatus; }set { connectionStatus = value; } }
        private StateInfo stateInfo;

        private string amsId;
        public string AmsId { get { return amsId; } set { amsId = value; } }
        private int portNo;
        public int PortNo { get { return portNo; } set { portNo = value; } }
        private string varName;
        public string VarName { get { return varName; } set { varName = value; } }

        private bool test;

        int userPlcHandle = 0;
        int userHmiVarHandle = 0;
        int passWordVarHandle = 0;
        int stateVarHandle = 0;
        private TcAdsClient tcAdsClient = null;
        private static AmsAddress serverAddress;
        Thread commThread = null;
        public TcAdsCom(string amsId,int portNo,string varName)
        {
            AmsId = amsId;
            PortNo = portNo;
            VarName = varName;
            commThread = new Thread(Communication);
            commThread.IsBackground = true;
            commThread.Start();
        }

        private void Communication()
        {
            while (true)//connectionStatus != ConnectionStatusEnum.Connected)
            {
                switch (connectionStatus)
                {
                    case ConnectionStatusEnum.CheckCfg:
                        {
                            try
                            {
                                if (amsId.ToUpper() == "LOCAL" ||
                                    amsId == "")
                                {
                                    serverAddress = new AmsAddress(TwinCAT.Ads.AmsNetId.Local, portNo);
                                }
                                else
                                {
                                    serverAddress = new AmsAddress(amsId, portNo);
                                }
                                connectionStatus = ConnectionStatusEnum.Disconnect;
                            }
                            catch
                            {
                                break;
                            }
                        }
                        break;
                    case ConnectionStatusEnum.Disconnect:
                        {
                            try
                            {
                                tcAdsClient = new TcAdsClient();
                                tcAdsClient.Connect(serverAddress.NetId, serverAddress.Port);
                                connectionStatus = ConnectionStatusEnum.ReadStatus;
                            }
                            catch
                            {
                                Thread.Sleep(2000);
                            }
                        }
                        break;
                    //Read ads status
                    case ConnectionStatusEnum.ReadStatus:
                        {
                            try
                            {
                                stateInfo = tcAdsClient.ReadState();
                                if (stateInfo.AdsState != AdsState.Invalid &&
                                    stateInfo.AdsState != AdsState.Exception &&
                                    stateInfo.AdsState != AdsState.Error &&
                                    stateInfo.AdsState != AdsState.Shutdown &&
                                    stateInfo.AdsState != AdsState.Stopping &&
                                    stateInfo.AdsState != AdsState.Stop &&
                                    stateInfo.AdsState != AdsState.PowerFailure)
                                {
                                    connectionStatus = ConnectionStatusEnum.CreateHandler;
                                }
                            }
                            catch (AdsErrorException e)
                            {
                                if (e.ErrorCode == AdsErrorCode.PortNotConnected ||
                                    e.ErrorCode == AdsErrorCode.TargetPortNotFound ||
                                    e.ErrorCode == AdsErrorCode.PortDisabled)
                                {
                                    Thread.Sleep(1000);
                                }
                                else
                                    connectionStatus = ConnectionStatusEnum.Disconnect;
                            }
                            catch
                            { connectionStatus = ConnectionStatusEnum.Disconnect; }
                        }
                        break;
                    case ConnectionStatusEnum.CreateHandler:
                        {
                            try
                            {
                                userPlcHandle = tcAdsClient.CreateVariableHandle(varName + ".UserPlc");
                                userHmiVarHandle = tcAdsClient.CreateVariableHandle(varName + ".UserHmi");
                                passWordVarHandle = tcAdsClient.CreateVariableHandle(varName + ".Password");
                                stateVarHandle = tcAdsClient.CreateVariableHandle(varName + ".State");
                                connectionStatus = ConnectionStatusEnum.Connected;
                            }
                            catch
                            {
                               connectionStatus = ConnectionStatusEnum.ReadStatus;
                            }
                        }
                        break;
                    case ConnectionStatusEnum.DeleteHandler:
                        {
                            try
                            {
                                if (userPlcHandle != 0)
                                {
                                    tcAdsClient.DeleteVariableHandle(userPlcHandle);
                                }
                                if (userHmiVarHandle != 0)
                                {
                                    tcAdsClient.DeleteVariableHandle(userHmiVarHandle);
                                }
                                if (passWordVarHandle != 0)
                                {
                                    tcAdsClient.DeleteVariableHandle(passWordVarHandle);
                                }
                                if (stateVarHandle != 0)
                                {
                                    tcAdsClient.DeleteVariableHandle(stateVarHandle);
                                }
                                connectionStatus = ConnectionStatusEnum.CreateHandler;
                            }
                            catch (AdsErrorException e)
                            {
                                if (e.ErrorCode == AdsErrorCode.DeviceSymbolNotFound)
                                {
                                    connectionStatus = ConnectionStatusEnum.CreateHandler;
                                }
                            }
                            catch
                            {
                                connectionStatus = ConnectionStatusEnum.Disconnect;
                            }
                        }
                        break;
                    case ConnectionStatusEnum.Connected:
                        Thread.Sleep(100);
                        break;
                }
            }
        }

        public HmiUtilAddonLoginState Login(string User,string PassWord)
        {
            if (connectionStatus == ConnectionStatusEnum.Connected)
            {
                HmiUtilAddonLoginState state = HmiUtilAddonLoginState.None;
                try
                {
                    tcAdsClient.WriteAnyString(userPlcHandle, User, 255, Encoding.Default);
                    tcAdsClient.WriteAnyString(userHmiVarHandle, User, 255, Encoding.Default);
                    tcAdsClient.WriteAnyString(passWordVarHandle, PassWord, 255, Encoding.Default);
                    tcAdsClient.WriteAny(stateVarHandle, (byte)HmiUtilAddonLoginState.LogOn);
                    while (state != HmiUtilAddonLoginState.Fail &&
                           state != HmiUtilAddonLoginState.Success)
                    {
                        state = (HmiUtilAddonLoginState)tcAdsClient.ReadAny(stateVarHandle, typeof(byte));
                    }
                }
                catch (AdsErrorException e)
                {
                    if (e.ErrorCode == AdsErrorCode.PortNotConnected ||
                        e.ErrorCode == AdsErrorCode.TargetPortNotFound ||
                        e.ErrorCode == AdsErrorCode.PortDisabled)
                    {
                        connectionStatus = ConnectionStatusEnum.ReadStatus;
                    }
                    else if (e.ErrorCode == AdsErrorCode.DeviceInvalidOffset ||
                        e.ErrorCode == AdsErrorCode.DeviceNotifyHandleInvalid ||
                        e.ErrorCode == AdsErrorCode.DeviceSymbolNotFound)
                    {
                        connectionStatus = ConnectionStatusEnum.DeleteHandler;
                    }
                    else
                    {
                        connectionStatus = ConnectionStatusEnum.Disconnect;
                    }
                }
                catch
                { connectionStatus = ConnectionStatusEnum.Disconnect;
                }
                return state;
            }
            else
            {
                return HmiUtilAddonLoginState.Fail;
            }
        }

        public HmiUtilAddonLoginState LogOff(string User)
        {
            if (connectionStatus == ConnectionStatusEnum.Connected)
            {
                HmiUtilAddonLoginState state = HmiUtilAddonLoginState.None;
                try
                {
                    tcAdsClient.WriteAnyString(userPlcHandle, User, 255, Encoding.Default);
                    tcAdsClient.WriteAnyString(userHmiVarHandle, User, 255, Encoding.Default);
                    tcAdsClient.WriteAny(stateVarHandle, (byte)HmiUtilAddonLoginState.LogOff);
                    state = (HmiUtilAddonLoginState)tcAdsClient.ReadAny(stateVarHandle, typeof(byte));
                }
                catch (AdsErrorException e)
                {
                    if (e.ErrorCode == AdsErrorCode.PortNotConnected ||
                        e.ErrorCode == AdsErrorCode.TargetPortNotFound ||
                        e.ErrorCode == AdsErrorCode.PortDisabled)
                    {
                        connectionStatus = ConnectionStatusEnum.ReadStatus;
                    }
                    else if (e.ErrorCode == AdsErrorCode.DeviceInvalidOffset ||
                        e.ErrorCode == AdsErrorCode.DeviceNotifyHandleInvalid ||
                        e.ErrorCode == AdsErrorCode.DeviceSymbolNotFound)
                    {
                        connectionStatus = ConnectionStatusEnum.DeleteHandler;
                    }
                    else
                    {
                        connectionStatus = ConnectionStatusEnum.Disconnect;
                    }
                }
                catch
                { connectionStatus = ConnectionStatusEnum.Disconnect;
                }
                return state;
            }
            else
            {
                return HmiUtilAddonLoginState.Fail;
            }
        }

        public enum ConnectionStatusEnum
        {
            CheckCfg,
            Disconnect,
            CreateHandler,
            DeleteHandler,
            Connected,
            ReadStatus
        }

        public enum HmiUtilAddonLoginState : Byte
        {
            None,
            LogOn,
            LogOff,
            Success,
            Fail
        }
    }
}
