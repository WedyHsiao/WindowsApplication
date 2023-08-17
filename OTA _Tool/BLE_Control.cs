using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

using BT_FUNCTION; //Package Handler and Command
using Bluetooth_Lib; //Take MSFT Displayer as reference


namespace BT_NORDIC
{
    public enum MsgType
    {
        intervalEndVal,
        ConnectState,
        DISinfo,
        DFUStatus,
        ImagePageStatus,
        UpdateValue,
    }

    public class BLE
    {
        public CancellationTokenSource cancellationTokenSource;

        public delegate void eventRun(MsgType type, string str, byte[] data = null);
        public event eventRun ValueChanged;

        private DeviceWatcher deviceWatcher;

        public GattCharacteristic ControlCharacteristic;
        public GattCharacteristic registeredCharacteristic;
        public GattCharacteristic BTLControlCharacteristic;
        public GattCharacteristic BTLPackageCharacteristic;

        private GattDeviceService SecureDFUservice = null;
        public GattDeviceService batteryservice = null;
        public GattDeviceService deviceservice = null;

        Guid DFUControlPoint = new Guid("8EC90001-F315-4F60-9FB8-838830DAEA50");
        Guid DFUPacket = new Guid("8EC90002-F315-4F60-9FB8-838830DAEA50");
        Guid DFUControlPoint_Legacy = new Guid("8EC90003-F315-4F60-9FB8-838830DAEA50"); //Wedy June Modified

        //Original Device
        private ObservableCollection<BluetoothLEAttributeDisplay> Device_ServiceCollection = new ObservableCollection<BluetoothLEAttributeDisplay>();
        private ObservableCollection<BluetoothLEAttributeDisplay> CharacteristicCollection = new ObservableCollection<BluetoothLEAttributeDisplay>();


        //displayed the device for Known dfu target (Enter Bootloader)
        private ObservableCollection<BluetoothLEDeviceDisplay> KnownDevices = new ObservableCollection<BluetoothLEDeviceDisplay>();
        private ObservableCollection<BluetoothLEAttributeDisplay> DFU_Device_ServiceCollection = new ObservableCollection<BluetoothLEAttributeDisplay>();



        //Check the device
        public BluetoothLEDevice DUT_Device { get; set; }
        public BluetoothLEDevice bluetoothLeDevice { get; set; }
        public string DUT_DeviceMAC { get; set; }
        public string DFU_DeviceMAC { get; set; }
        public string DFUADVNAME { get; set; }

        //The target device name for searching
        private string NameofStylus { get; set; }

        public bool inConnect = false;
        public bool isGetfwVer = false;
        public bool getBootloaderACK = false;
        public bool isSilentDFU = false; //Wedy June Modified
        bool scanDFUtarg = false;
        bool asyncLock = false;
        bool subscribedForNotifications = false;

        int retryConnectNum = 10;
        int retryConnectCount = 0;

        //using the class from function.cs
        CMD cmd = new CMD();
        Files file = new Files();

        CultureInfo culture = new CultureInfo("en-US");


        public BLE()
        {
            cancellationTokenSource = new CancellationTokenSource();
            file.Load_fwFile();
        }

        //Get the target device name for searching
        public void setNamePen(string pen)
        {
            this.NameofStylus = pen;
        }

        public void OnCancelPoll()
        {
            Debug.WriteLine("OnCancelPoll");
            cancellationTokenSource.Cancel();
        }

        #region Device Watcher setting
        public void StartBleDeviceWatcher()
        {
            //Check whether the devcie is already exist in the system
            // Additional properties we would like about the device.
            // Property strings are documented here https://msdn.microsoft.com/en-us/library/windows/desktop/ff521659(v=vs.85).aspx
            // string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Le.IsConnectable" };
            string[] requestedProperties = { "System.Devices.Aep.DeviceAddress" };


            // Query for extra properties you want returned
            //string[] requested_Properties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

            // BT_Code: Example showing paired and non-paired in a single query.
            string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

            deviceWatcher = DeviceInformation.CreateWatcher(aqsAllBluetoothLEDevices, requestedProperties, DeviceInformationKind.AssociationEndpoint);
            Debug.WriteLine("[DEBUG] StartBleDeviceWatcher: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

            // Register event handlers before starting the watcher.
            // Added, Updated and Removed are required to get all nearby devices
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Stopped += DeviceWatcher_Stopped;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;
            deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted; //EnumerationCompleted and Stopped are optional to implement.

            KnownDevices.Clear();
            deviceWatcher.Start();
        }

        private void StopBleDeviceWatcher()
        {
            Debug.WriteLine("[DEBUG] StopBleDeviceWatcher: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
           
            if (deviceWatcher != null)
            {
                Debug.WriteLine("[DEBUG] Remove all KnownDevices count {0}", KnownDevices.Count);
                
                // Unregister the event handlers.
                deviceWatcher.Added -= DeviceWatcher_Added;
                deviceWatcher.Stopped -= DeviceWatcher_Stopped;
                deviceWatcher.Updated -= DeviceWatcher_Updated;
                deviceWatcher.Removed -= DeviceWatcher_Removed;
                deviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;

                // Stop the watcher.
                deviceWatcher.Stop();
                deviceWatcher = null;

            }
        }

        private void DeviceWatcher_Stopped(DeviceWatcher sender, object args)
        {
            Debug.WriteLine("DeviceWatcher_Stopped");
        }

        private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            Debug.WriteLine("[DEBUG] DeviceWatcher_Updated:{0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

        }

        private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            Debug.WriteLine("[DEBUG] DeviceWatcher_Removed: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
        }

        private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object e)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            Debug.WriteLine("[DEBUG] DeviceWatcher_EnumerationCompleted: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
        }

        private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            Dispatcher.CurrentDispatcher.Invoke(async () =>
            {
                lock (this)
                {
                    //The device be scan by the host (Paired or non-paired)
                    Debug.WriteLine(String.Format("[DEBUG] Added {0}", args.Name.ToString()));

                    if (sender == deviceWatcher)
                    {
                        // Make sure device isn't already present in the list.
                        if (FindBluetoothLEDeviceDisplay(args.Id) == null)
                        {
                            //DFUADVNAME is set and define in SETADVNAMECommand()
                            if (Equals(args.Name, DFUADVNAME))
                            {
                                scanDFUtarg = true;
                                KnownDevices.Add(new BluetoothLEDeviceDisplay(args));
                                Debug.WriteLine("[DEBUG] Find DFU Device: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                ValueChanged(MsgType.DFUStatus, "FIND_DFU_TARGET");
                            }
                            else
                            {

                            }
                        }
                    }
                }
            });
        }
        #endregion


        #region Find/Match and Close Device for Main Device
        public void FindPairedDevice()
        {
            Task.Run(async () => await SelectDevice("", cancellationTokenSource.Token)).Wait();
            Debug.WriteLine("[DEBUG] Target Stylus Name: " + NameofStylus);
            Debug.WriteLine("[DEBUG] Finding Pair Device: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
        }

        public async Task SelectDevice(string MAC, CancellationToken cts) //used to selec the existed device in the host
        {
            Debug.WriteLine("[DEBUG] Select Device: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

            DUT_DeviceMAC = MAC;
            DUT_Device = null;
            int countDUT = 0;
            DeviceInformation DUT_DeviceInfo;
            DUT_DeviceInfo = null;
            DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector()).Completed = async (asyncInfo, asyncStatus) =>
            {
                if (asyncStatus == AsyncStatus.Completed)
                {
                    DeviceInformationCollection deviceInformation = asyncInfo.GetResults();
                    Debug.WriteLine("[DEBUG] number of device pairing with system = {0}", deviceInformation.Count);

                    //Check the paired device name
                    foreach (DeviceInformation DUT in deviceInformation)
                    {
                        //Wedy: Check the target device is existed
                        if (DUT.Name.ToString() == NameofStylus) //"Dell PN7522W"
                        {
                            countDUT++;
                            DUT_DeviceInfo = DUT;
                        }
                    }

                    if (deviceInformation.Count == 0)
                    {

                        string msg = "No device";
                        ValueChanged(MsgType.ConnectState, msg);
                        return;

                    }
                    if (countDUT == 0)
                    {
                        string msg = "No device";
                        ValueChanged(MsgType.ConnectState, msg);
                    }
                    else if (countDUT == 1)
                    {
                        //Get the matching device then enter to discover the service
                        await Matching(DUT_DeviceInfo.Id, cancellationTokenSource.Token);
                        string msg = "Find device";
                        ValueChanged(MsgType.ConnectState, msg);
                        return;
                    }
                    else
                    {
                        string msg = "Multiple Devices";
                        ValueChanged(MsgType.ConnectState, msg);
                        return;
                    }
                }
            };
            Debug.WriteLine("[DEBUG] SelectDevice Process done: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
        }

        private async Task<bool> Matching(string Id, CancellationToken cts)  //Bluetooth Device be searched
        {
            try
            {
                BluetoothLEDevice.FromIdAsync(Id).Completed = async (asyncInfo, asyncStatus) =>
                {
                    if (asyncStatus == AsyncStatus.Completed)
                    {
                        BluetoothLEDevice bleDevice = asyncInfo.GetResults();
                        if (bleDevice == null) { return; }

                        DUT_Device = bleDevice;
                        Debug.WriteLine("[DEBUG] Matching: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                        //Wedy mark for the target device
                        if (DUT_Device.Name.ToString() == NameofStylus) //"Dell PN7522W"
                        {
                            Debug.WriteLine("[DEBUG] Find the target pen: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                            DUT_DeviceMAC = Id; // Mark down the ID

                            //DFU Address after Enter Bootloader
                            DFU_DeviceMAC = TargetDFU_MACAddress();

                            DUT_Device.ConnectionStatusChanged += DUT_Device_ConnectionStatusChanged;

                            if (DUT_Device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                            {
                                
                                Debug.WriteLine("[DEBUG] Device be connecting: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                                //Device is existed and connected try to acess the device for connect and link
                                ConnectDevice();
                            }
                        }
                        else
                        {
                            string msg = "Pen disconnect";
                            ValueChanged(MsgType.ConnectState, msg);
                        }
                    }
                };
            }
            catch (Exception e)
            {
                Debug.WriteLine("Could not find the matching device " + e);
                string msg = "error";
                ValueChanged(MsgType.ConnectState, msg);
            }
            return false;
        }

        public async void ConnectDevice()
        {
            if (!inConnect)
            {
                inConnect = true;
                Debug.WriteLine("[DEBUG] await Connect(): {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                var ss = await Connect();
            }
        }

        private async Task<bool> Connect()  //Connect to check the device service
        {
            long ConnectStartTime = DateTime.Now.Ticks;
            Debug.WriteLine("[DEBUG] Accessing...: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            ValueChanged(MsgType.ConnectState, "Accessing");
            try
            {
                await SelectDeviceService(cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("[DEBUG] SelectDeviceService failed: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                inConnect = false;
            }
            long ConnectEndTime = DateTime.Now.Ticks;
            float testTime = (float)(ConnectEndTime - ConnectStartTime) / 10000000;
            Debug.WriteLine("[DEBUG] Connected and Finished GATT Access time: " + testTime);
            return true;
        }

        public void clearBTStatus() //Wedy 0628 change to close the channel
        {

            if (ControlCharacteristic != null)
            {
                ControlCharacteristic = null;
            }

            if (batteryservice != null)
            {
                batteryservice.Dispose();
            }
            if (deviceservice != null)
            {
                deviceservice.Dispose();
            }
            if (SecureDFUservice != null)
            {
                SecureDFUservice.Dispose();
            }
            if (Device_ServiceCollection.Count != 0)
            {
                foreach (var ser in Device_ServiceCollection)
                {
                    ser.service?.Dispose();
                }
                Device_ServiceCollection.Clear();
            }
            inConnect = false;
        }
        #endregion


        #region Read the service information for main device
        public void ReadDevice_Firmware()
        {
            var success = Task.Run(async () => await ReadDevice_Service(cancellationTokenSource.Token));

        }
        
        private async Task<bool> ReadDevice_Service(CancellationToken cts)
        {
            GattCharacteristicsResult GattResult = null;
            IReadOnlyList<GattCharacteristic> Device_Characteristics_INFO = null;
            Debug.WriteLine("[DEBUG] ReadDevice_Service {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

            try
            {
                if (!isGetfwVer)
                {
                    if (deviceservice == null)
                    {
                        inConnect = false;
                        return false;
                    }
                    
                    //Discover the characteristic of Device service
                    GattResult = await deviceservice.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    Debug.WriteLine("[DEBUG] DeviceService Acess: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                    if (GattResult.Status != GattCommunicationStatus.Success)
                    {
                        Debug.WriteLine("DeviceService GetCharateristic failed:{0}", GattResult.Status.ToString());
                        Device_Characteristics_INFO = new List<GattCharacteristic>();
                        deviceservice.Dispose();
                        clearBTStatus();
                        ValueChanged(MsgType.ConnectState, "Access fail");
                        return false;
                    }
                    Device_Characteristics_INFO = GattResult.Characteristics;
                    
                    foreach (GattCharacteristic c in Device_Characteristics_INFO)
                    {
                        GattCharacteristicProperties properties = c.CharacteristicProperties;
                        Debug.WriteLine($"[DEBUG] Firmware Revision characteristics: {c.Uuid}");
                        if (properties.HasFlag(GattCharacteristicProperties.Read))
                        {
                            // This characteristic supports reading from it.
                            GattReadResult result1 = await c.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (result1.Status == GattCommunicationStatus.Success)
                            {
                                GattNativeCharacteristicUuid charName;
                                if (Enum.TryParse(Utilities.ConvertUuidToShortId(c.Uuid).ToString(), out charName))
                                {
                                    if (charName.ToString() == "FirmwareRevisionString")
                                    {
                                        byte[] data;
                                        isGetfwVer = true;
                                        Debug.WriteLine("[DEBUG] Device Service Accessed Done: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                        CryptographicBuffer.CopyToByteArray(result1.Value, out data);
                                        
                                        //Get Firmware Version
                                        ValueChanged(MsgType.DISinfo, Encoding.UTF8.GetString(data));
                                        Debug.WriteLine("[DEBUG] Current FW version is " + Encoding.UTF8.GetString(data));
                                        Debug.WriteLine("[DEBUG] FW version recieved {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                        return true;
                                    }
                                }
                            }
                            else
                            {
                                clearBTStatus();

                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                Debug.WriteLine("ReadDevice_Service Failed: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                clearBTStatus();
            }
            return false;
        }
        
        public async Task<bool> SelectDeviceService(CancellationToken cts) // Discover all service and focus to check DFU service existed
        {
            Debug.WriteLine("[DEBUG] SelectDeviceService start: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

            GattCharacteristicsResult GattCharacterResult = null;
            IReadOnlyList<GattCharacteristic> Device_Characteristics_DFU = null;

            retryConnectCount++; //If deivice is not be accessable retry until failed.
            if (retryConnectCount > retryConnectNum)
            {
                string msg = "retry fail";
                Debug.WriteLine("Retry times: {0}", retryConnectCount);
                ValueChanged(MsgType.ConnectState, msg);
                retryConnectCount = 0;
                inConnect = false;
                return false;
            }

            try
            {
                if (DUT_Device == null)
                {
                    Debug.WriteLine("SelectDeviceService return");
                    inConnect = false;
                    return false;
                }
                Debug.WriteLine("[DEBUG] GetGattServicesAsync: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                if (DUT_Device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                {
                    Debug.WriteLine("DUT_Device.GetGattServicesAsync Disconnected return");
                    inConnect = false;
                    return false;
                }

                //Discover the service
                GattDeviceServicesResult GATTServiceResult = await DUT_Device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                if (GATTServiceResult.Status == GattCommunicationStatus.Success)
                {
                    var services = GATTServiceResult.Services;
                    foreach (var service in services)
                    {
                        Device_ServiceCollection.Add(new BluetoothLEAttributeDisplay(service));

                        GattNativeServiceUuid serviceName;
                        if (Enum.TryParse(Utilities.ConvertUuidToShortId(service.Uuid).ToString(), out serviceName))
                        {
                            if (serviceName.ToString() == "Battery")
                            {
                                batteryservice = service;
                                Debug.WriteLine($"[DEBUG] Batteryservice: {batteryservice.Uuid}");
                            }
                            if (serviceName.ToString() == "DeviceInformation")
                            {
                                deviceservice = service;
                                Debug.WriteLine($"[DEBUG] Deviceservice: {deviceservice.Uuid}");
                            }
                            if (serviceName.ToString() == "SecureDFU")
                            {
                                SecureDFUservice = service;
                                Debug.WriteLine($"[DEBUG] SecureDFUservice: {SecureDFUservice.Uuid}");
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("GetGattServicesAsync Failed :{0}", GATTServiceResult.Status.ToString());
                    clearBTStatus();
                    ValueChanged(MsgType.ConnectState, "disconnect");
                    return false;
                }
                if (SecureDFUservice == null)
                {
                    Debug.WriteLine("SecureDFUservice failed");
                    inConnect = false;
                    return false;
                }

                //Discover the characteristic of DFU service
                GattCharacterResult = await SecureDFUservice.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                Debug.WriteLine("[DEBUG] SecureDFUservice Acess: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                if (GattCharacterResult.Status != GattCommunicationStatus.Success)
                {
                    Device_Characteristics_DFU = new List<GattCharacteristic>();
                    SecureDFUservice.Dispose();
                    clearBTStatus();
                    Debug.WriteLine("SecureDFU GetCharateristic failed:{0}", GattCharacterResult.Status.ToString());
                    ValueChanged(MsgType.ConnectState, "disconnect");
                    await Connect();
                    return false;
                }
                Device_Characteristics_DFU = GattCharacterResult.Characteristics;

                foreach (GattCharacteristic c in Device_Characteristics_DFU)
                {
                    // Guid DFUControlPoint = new Guid("8EC90003-F315-4F60-9FB8-838830DAEA50"); //Wedy June Block
                    if (c.Uuid == DFUControlPoint_Legacy) //Wedy June modified from DFUControlPoint
                    {
                        ControlCharacteristic = c;
                        // initialize status
                        GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
                        var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
                        if (ControlCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                        {
                            cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                        }

                        try
                        {
                            // BT_Code: Must write the CCCD in order for server to send indications.
                            // We receive them in the ValueChanged event handler.
                            status = await ControlCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);
                            if (status == GattCommunicationStatus.Success)
                            {
                                if (isGetfwVer)
                                {
                                    RemoveValueChangedHandler();
                                }
                                registeredCharacteristic = ControlCharacteristic;

                                AddValueChangedHandler();
                                IBuffer tmp0 = SETADVNAMECommand();
                                await WriteBufferToPackgetAsync(tmp0, cancellationTokenSource.Token);
                                if (!isSilentDFU) //Wedy June Modified
                                {
                                    Debug.WriteLine("[DEBUG] Service Accessed Done: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                    ValueChanged(MsgType.ConnectState, "Aceess Pass");
                                    retryConnectCount = 0;
                                    inConnect = true;
                                }
                            }
                            else
                            {
                                SecureDFUservice.Dispose();
                                clearBTStatus();
                                inConnect = false;
                                Debug.WriteLine("GattCommunicationStatus.fail");
                                ValueChanged(MsgType.ConnectState, "disconnect");
                                return false;
                            }
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Debug.WriteLine("AccessException {0}", ex);
                            SecureDFUservice.Dispose();
                            clearBTStatus();
                            ValueChanged(MsgType.ConnectState, "disconnect");
                            return false;
                        }
                    }

                    //Wedy June Modified
                    if (isSilentDFU)
                    {
                        if (c.Uuid == DFUControlPoint)
                        {
                            Debug.WriteLine("[DEBUG] Silent -> Find DFU DFUControlPoint: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                            BTLControlCharacteristic = null;
                            BTLControlCharacteristic = c;
                            GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
                            var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
                            if (c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                            {
                                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
                            }
                            try
                            {
                                status = await BTLControlCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

                                if (status == GattCommunicationStatus.Success)
                                {
                                    BTLControlCharacteristic.ValueChanged += BTL_Characteristic_ValueChanged;
                                    subscribedForNotifications = true;
                                    Debug.WriteLine("[DEBUG] Silent -> Ready for DFU Process: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                    ValueChanged(MsgType.ConnectState, "Aceess Pass");
                                    retryConnectCount = 0;
                                    inConnect = true;
                                }
                                else
                                {
                                    Debug.WriteLine("[DEBUG] Silent -> BTLControlCharacteristic notify fail: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                    clearDFUBTStatus();
                                    SecureDFUservice.Dispose();
                                }
                            }

                            catch (UnauthorizedAccessException e)
                            {
                                Debug.WriteLine("[DEBUG] Silent -> UnauthorizedAccessException", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                clearDFUBTStatus();
                                SecureDFUservice.Dispose();
                                string msg = "UnauthorizedAccessException";
                                ValueChanged(MsgType.DFUStatus, msg);
                            }
                        }
                        if (c.Uuid == DFUPacket)
                        {
                            Debug.WriteLine("[DEBUG] Silent -> Find DFU DFUPacket: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                            BTLPackageCharacteristic = null;
                            BTLPackageCharacteristic = c;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("SelectDeviceService exception (E)");
                clearBTStatus();
                return false;
            }
            return true;
        }
        #endregion


        #region Define DFU Device Name for Scan
        private IBuffer SETADVNAMECommand() //Set the adv name afte enter bootloader for DFU
        {
            string ADVName = "";
            if (DUT_DeviceMAC != null)
            {
                if (DUT_DeviceMAC.Length > 6)
                {
                    Debug.WriteLine("[DEBUG] SETADVNAMECommand mac str len = {0}", DUT_DeviceMAC.Length);
                    int last4wordIndex = DUT_DeviceMAC.Length - 6;
                    string numberStr = DUT_DeviceMAC.Substring(last4wordIndex);
                    ADVName = "Dfu: " + numberStr;
                }
            }
            byte[] tmp = Encoding.ASCII.GetBytes(ADVName);
            byte num = BitConverter.GetBytes(tmp.Length)[0];
            var temp = new byte[] { 0x2, num };
            byte[] data = new byte[temp.Length + tmp.Length];
            System.Buffer.BlockCopy(temp, 0, data, 0, temp.Length);
            System.Buffer.BlockCopy(tmp, 0, data, temp.Length, tmp.Length);
            var buffer = Files.ToIBuffer(data);
            DFUADVNAME = ADVName; //Name of DFU device
            return buffer;
        }
        private string TargetDFU_MACAddress() //Get DFU device address
        {
            string dfuid = "N";
            if (DUT_DeviceMAC != null)
            {
                if (DUT_DeviceMAC.Length > 6)
                {
                    int last4wordIndex = DUT_DeviceMAC.Length - 2;
                    string numberStr = DUT_DeviceMAC.Substring(last4wordIndex);
                    byte newByte = byte.Parse(numberStr, System.Globalization.NumberStyles.HexNumber);
                    int lastmacNumber = Convert.ToInt32(newByte);
                    lastmacNumber++;
                    string aa = Convert.ToByte(lastmacNumber).ToString("x2");

                    Debug.WriteLine("[DEBUG] TargetDFU_MACAddress {0} to {1}", numberStr, aa);
                    dfuid = DUT_DeviceMAC.Remove(last4wordIndex, 2).Insert(last4wordIndex, aa);

                }
            }
            return dfuid;
        }
        #endregion


        #region DFU device connection Control
        public bool RunDFUConnect()
        {
            
            Debug.WriteLine("[DEBUG] Start Accessing DFU Device: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

            var aa = Task.Run(async () => await ConnectDfuAsync(cancellationTokenSource.Token));

            Debug.WriteLine("[DEBUG] End of Accessing DFU Device: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            
            return aa.Result;
        }
        
        private async Task<bool> ConnectDfuAsync(CancellationToken cts)
        {
            if (KnownDevices.Count > 0 && getBootloaderACK && scanDFUtarg)
            {
                getBootloaderACK = false;
                scanDFUtarg = false;
                StopBleDeviceWatcher();

                try
                {
                    DFU_DeviceMAC = KnownDevices[0].Id;
                    var aa = await Matching_DFU_Device(KnownDevices[0].Id, cancellationTokenSource.Token);
                    if (aa == true) { return true; }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("[DEBUG] ConnectDfuAsync Failed: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                }
            }
            return false;
        }
        
        private async Task<bool> Matching_DFU_Device(string Id, CancellationToken cts)
        {
            Debug.WriteLine("[DEBUG] Matching DFU Target Device: " + Id);

            retryConnectCount++;
            if (retryConnectCount > retryConnectNum)
            {
                string msg = "retry fail";
                ValueChanged(MsgType.DFUStatus, msg);
                return false;
            }
            try
            {
                bluetoothLeDevice = null;
                
                // BT_Code: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
                bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(Id);
                DUT_Device = bluetoothLeDevice;
                if (bluetoothLeDevice == null)
                {
                    var aa = Matching_DFU_Device(Id, cancellationTokenSource.Token);
                    return false;
                }
                await SelectDFUDeviceService(cancellationTokenSource.Token);
                return true;

            }
            catch (Exception e)
            {
                Debug.WriteLine("[DEBUG] Matching DFU Device Failed: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                var aa = Matching_DFU_Device(Id, cancellationTokenSource.Token);
                return false;
            }
        }
        
        public void clearDFUBTStatus() //Wedy 0628 change to close the channel
        {

            if (BTLControlCharacteristic != null)
            {
                BTLControlCharacteristic.ValueChanged -= BTL_Characteristic_ValueChanged;
                BTLControlCharacteristic = null;
            }
            if (BTLPackageCharacteristic != null)
            {
                BTLPackageCharacteristic = null;
            }
            if (DFU_Device_ServiceCollection.Count != 0)
            {
                foreach (var ser in DFU_Device_ServiceCollection)
                {
                    ser.service?.Dispose();
                }
                DFU_Device_ServiceCollection.Clear();
            }

        }
        #endregion


        #region After enter bootloader for DFU, then read the DFU service information and access right 
        public async Task SelectDFUDeviceService(CancellationToken cts)
        {
            GattDeviceService dfuservices = null;
            IReadOnlyList<GattDeviceService> services = null;
            IReadOnlyList<GattCharacteristic> characteristics = null;

            try
            {
                if (bluetoothLeDevice == null)
                {
                    Debug.WriteLine("[DEBUG] SelectDFUDeviceService return: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                    return;
                }
                GattDeviceServicesResult result = await bluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                if (result.Status == GattCommunicationStatus.Success)
                {
                    services = result.Services;
                    foreach (var service in services)
                    {
                        DFU_Device_ServiceCollection.Add(new BluetoothLEAttributeDisplay(service));
                    }
                    dfuservices = services[2];
                    Debug.WriteLine("[DEBUG] SecureDFUservices {0}", dfuservices.Uuid);
                }
                else
                {
                    Debug.WriteLine("SelectDFUDeviceService GetGattServicesAsync: {0}", result.Status.ToString());
                    clearDFUBTStatus();
                    services = null;
                    await SelectDFUDeviceService(cancellationTokenSource.Token);
                    return;
                }
                
                if (bluetoothLeDevice == null)
                {
                    Debug.WriteLine("[DEBUG] SelectDFUDevice is null");
                    return;
                }
                bluetoothLeDevice.ConnectionStatusChanged += DUT_Device_ConnectionStatusChanged;
                CharacteristicCollection.Clear();

                //Get DFU service access
                var result1 = await dfuservices.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (result1.Status == GattCommunicationStatus.Success)
                {
                    characteristics = result1.Characteristics;
                }
                else
                {
                    Debug.WriteLine("GetCharacteristicsAsync failed:{0} ", result1.Status.ToString());
                    clearDFUBTStatus();
                    dfuservices.Dispose();
                    services = null;
                    characteristics = null;
                    await SelectDFUDeviceService(cancellationTokenSource.Token);

                    return;
                }
                foreach (GattCharacteristic c in characteristics)
                {
                    if (c.Uuid == DFUControlPoint)
                    {
                        Debug.WriteLine("[DEBUG] Find DFU DFUControlPoint: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        BTLControlCharacteristic = null;
                        BTLControlCharacteristic = c;
                        GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
                        var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
                        if (c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                        {
                            cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
                        }
                        try
                        {

                            if (BTLControlCharacteristic == null) { return; }
                            status = await BTLControlCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

                            if (status == GattCommunicationStatus.Success)
                            {
                                BTLControlCharacteristic.ValueChanged += BTL_Characteristic_ValueChanged;
                                subscribedForNotifications = true;
                                Debug.WriteLine("[DEBUG] Ready for DFU Process: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                ValueChanged(MsgType.DFUStatus, "Init_DFU");
                            }
                            else
                            {
                                Debug.WriteLine("[DEBUG] BTLControlCharacteristic notify fail: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                                clearDFUBTStatus();
                                dfuservices.Dispose();
                                services = null;
                                characteristics = null;
                            }
                        }

                        catch (UnauthorizedAccessException e)
                        {
                            Debug.WriteLine("[DEBUG] UnauthorizedAccessException", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                            clearDFUBTStatus();
                            dfuservices.Dispose();
                            services = null;
                            characteristics = null;
                            string msg = "UnauthorizedAccessException";
                            ValueChanged(MsgType.DFUStatus, msg);
                        }
                    }
                    if (c.Uuid == DFUPacket)
                    {
                        Debug.WriteLine("[DEBUG] Find DFU DFUPacket: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        BTLPackageCharacteristic = null;
                        BTLPackageCharacteristic = c;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("SelectDFUDeviceService Exception:{0}", e.ToString());
                clearDFUBTStatus();
                dfuservices.Dispose();
                services = null;
            }
        }
        #endregion


        #region write buffer to the device
        public async Task<bool> WriteBufferToPackgetAsync(IBuffer buffer, CancellationToken cts)
        {
            try
            {
                // BT_Code: Writes the value from the buffer to the characteristic.
                if (ControlCharacteristic == null) { return false; }
                var result = await ControlCharacteristic.WriteValueWithResultAsync(buffer);

                if (result.Status == GattCommunicationStatus.Success)
                {

                    return true;
                }
                else
                {

                    return false;
                }
            }
            catch (Exception e)
            {

                return false;
            }

        }

        public async Task<bool> WriteBufferToBTLCTLAsync(IBuffer buffer, CancellationToken cts)
        {
            try
            {
                // BT_Code: Writes the value from the buffer to the characteristic.
                if (BTLControlCharacteristic == null)
                {
                    return false;
                }
                var result = await BTLControlCharacteristic.WriteValueWithResultAsync(buffer);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    return true;
                }
                else
                {
                    Debug.WriteLine("WriteBufferToBTLCTLAsync 2");
                    string msg = "transf fail";
                    ValueChanged(MsgType.DFUStatus, msg);
                    return false;
                }
            }
            catch (Exception e)
            {
                string msg = "transf fail";
                ValueChanged(MsgType.DFUStatus, msg);
                Debug.WriteLine("WriteBufferToBTLCTLAsync 3");
                return false;
            }

        }

        public async Task<bool> WriteBufferToBTLPCKAsync(IBuffer buffer, CancellationToken cts)
        {
            try
            {
                if (BTLPackageCharacteristic == null)
                {
                    return false;
                }
                // BT_Code: Writes the value from the buffer to the characteristic.
                var result = await BTLPackageCharacteristic.WriteValueWithResultAsync(buffer);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    //Debug.WriteLine("WriteBufferToBTLPCKAsync 1");
                    return true;
                }
                else
                {
                    Debug.WriteLine("WriteBufferToBTLPCKAsync 2");
                    return false;
                }
            }
            catch (Exception e)
            {
                string msg = "transf fail";
                ValueChanged(MsgType.DFUStatus, msg);
                Debug.WriteLine("WriteBufferToBTLPCKAsync 3");
                return false;
            }

        }
        #endregion


        #region monitor the charteristis changed
        public void RemoveConnectionStatusChanged()
        {
            Debug.WriteLine("[DEBUG] RemoveConnectionStatusChanged: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            if (DUT_Device != null)
            {
                DUT_Device.ConnectionStatusChanged -= DUT_Device_ConnectionStatusChanged;
            }
        }
        
        private void DUT_Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                inConnect = false;
                if (!asyncLock)
                {
                    asyncLock = true;
                    string msg = "disconnect";
                    ValueChanged(MsgType.ConnectState, msg);

                    Debug.WriteLine("[DEBUG] DUT_Device_ConnectionStatusChanged Disconnect: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                    if (Device_ServiceCollection.Count > 1)
                    {
                        foreach (var ser in Device_ServiceCollection)
                        {
                            ser.service?.Dispose();
                        }
                        Device_ServiceCollection.Clear();
                    }
                    RemoveValueChangedHandler();
                }

            }
            else
            {
                Debug.WriteLine("[DEBUG] DUT_Device_ConnectionStatusChanged Connect: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                ConnectDevice();
                asyncLock = false;
            }
        }
        
        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            Debug.WriteLine("[DEBUG] Characteristic_ValueChanged");
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);
            ProcessDataAsync(data);
        }
        
        private void BTL_Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);
            Task.Run(async () => await ProcessDataAsync(data)).Wait();
        }
        
        private void AddValueChangedHandler()
        {
            if (!subscribedForNotifications)
            {
                registeredCharacteristic.ValueChanged += Characteristic_ValueChanged;
                subscribedForNotifications = true;
                Debug.WriteLine("[DEBUG] AddValueChangedHandler: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            }
        }
        
        public void RemoveValueChangedHandler()
        {
            if (registeredCharacteristic == null) { return; }
            if (subscribedForNotifications)
            {
                if (ControlCharacteristic != null)
                {
                    ControlCharacteristic = null;
                }
                registeredCharacteristic.ValueChanged -= Characteristic_ValueChanged;
                registeredCharacteristic = null;
                subscribedForNotifications = false;
                Debug.WriteLine("[DEBUG] RemoveValueChangedHandler end: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            }
        }
        #endregion


        #region DFU API Calling
        public void EnterDFU() //Legacy DFU would be start from here to set device switched to bootloader
        {
            Debug.WriteLine("[DEBUG] EnterDFU: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            IBuffer tmp = cmd.EnterDFU_Command();
            var success = Task.Run(async () => await WriteBufferToPackgetAsync(tmp, cancellationTokenSource.Token));
            if (success.Result == false)
            {
                EnterDFU();
            }
        }
        
        public void StartDFU() //Silent DFU would be start from here
        {
            Debug.WriteLine("[DEBUG] StartDFU: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            file.isSendInitPkt = true;
            IBuffer tmp = cmd.SelectInit_Command();
            var success = WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
        }
        
        public void StartSendingImage(int imagepagenum)
        {
            if (imagepagenum != file.sentTimes)
            {
                var ss = Task.Run(async () => await SendingImageTestAsync(imagepagenum, cancellationTokenSource.Token));
            }
        }
        
        private void StartUpdateImage()
        {
            Debug.WriteLine("[DEBUG] FW (.bin) PrepareUpdateImage: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            file.isSendImagePkt = true;
            file.isSendImagePkt_step1 = false;
            IBuffer tmp = cmd.SelectInit_Command();
            var success = WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
        }
       
        private async Task SendingImageTestAsync(int chunkindex, CancellationToken cts) //DFU packet transfer function (.bin)
        {
            file.istransfer = true;
            IBuffer tmp;
            Debug.WriteLine("[DEBUG] SendingImageTestAsync {0}", chunkindex);
            try
            {

                tmp = cmd.CreateFW_Command(4096); // Start sending the filfe
                if (chunkindex == (file.sentTimes - 1)) //Last section of image
                {
                    tmp = cmd.CreateFW_Command(file.firmwareImage_LastLen);
                    Debug.WriteLine("[DEBUG] last image {0}", file.firmwareImage_LastLen);
                }

                var success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);

                //step 1 firmware transfer 
                var temp = file.firmwareImageSection[chunkindex];
                int offset = 0;
                int AttributeDataLen = 244;

                while (offset < temp.Length)
                {
                    int length = temp.Length - offset;
                    if (length > AttributeDataLen)
                    {
                        length = AttributeDataLen;
                    }
                    byte[] data = new byte[length];

                    Array.Copy(temp, offset, data, 0, length);
                    offset += length;

                    var writeBuffer = Files.ToIBuffer(data);
                    var writeSuccessful = await WriteBufferToBTLPCKAsync(writeBuffer, cancellationTokenSource.Token);

                }

                Debug.WriteLine("[DEBUG] transfer image {0}/{1} ", chunkindex + 1, file.sentTimes);
                if (chunkindex + 1 == file.sentTimes)
                {
                    DUT_Device.ConnectionStatusChanged -= DUT_Device_ConnectionStatusChanged;
                }

                //step 2 Get CRC after sending image
                tmp = cmd.GetCRC_Command();
                success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
                System.Threading.Thread.Sleep(10);

                // step 3 Excute Command be sent after recieved CRC
                tmp = cmd.Execute_Command();
                success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("[DEBUG] transfer fail : {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                string msg = "transfer fail";
                ValueChanged(MsgType.DFUStatus, msg);
            }
        }  
        
        private async Task<string[]> ProcessDataAsync(byte[] data) //Check the acknowledgement from the device
        {
            var returnValues = new string[3];

            if (data == null || data.Length == 0)
            {
                Debug.WriteLine("[DEBUG] ProcessDataAsync return: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                return returnValues;
            }
            string StringByte = BitConverter.ToString(data);
            Debug.WriteLine("[DEBUG] DATA RECIEVED: " + StringByte);
            var opCode = data[0];
            byte responseValue = byte.MinValue;
            byte requestedOpCode = byte.MinValue;
            //var returnValues = new string[2];
            if (opCode == 0x20 && data[1] == 0x1 && data[2] == 0x1)//0x200102
            {
                Debug.WriteLine("[DEBUG] DFU cmd ack done: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                // OTA bootloader start advertising, app need to reconect it.
                RemoveValueChangedHandler();
                getBootloaderACK = true;
                Debug.WriteLine("[DEBUG] BootloaderACK is {0}", getBootloaderACK);

                Debug.WriteLine("[DEBUG] EnterBootloaer: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                ValueChanged(MsgType.DFUStatus, "EnterBootloader");
            }
            else if (opCode == 0x60)
            {
                requestedOpCode = data[1];
                responseValue = data[2];
                if (requestedOpCode == 0x6 && responseValue == 0x1) //0x600601
                {

                    byte[] maxsize = new byte[4];
                    Array.Copy(data, 3, maxsize, 0, 4);
                    Array.Reverse(maxsize);
                    int result = BitConverter.ToInt32(maxsize, 0);
                    Debug.WriteLine("[DEBUG] max size: {0}", result);
                    if (file.isSendInitPkt)
                    {
                        IBuffer tmp = cmd.CreateInit_Command(file.init_package_Len);
                        var success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);

                    }
                    if (file.isSendImagePkt)
                    {
                        if (!file.isSendImagePkt_step1)
                        {
                            IBuffer tmp = cmd.SelectFW_Command();
                            var success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
                            file.isSendImagePkt_step1 = true;
                        }
                        else
                        {
                            IBuffer tmp = cmd.SRN_Command();
                            var success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
                        }
                    }
                    return returnValues;
                }

                else if (requestedOpCode == 0x1 && responseValue == 0x1)//0x600101
                {
                    if (file.isSendInitPkt && !file.istransfer)
                    {
                        Debug.WriteLine("[DEBUG] Set notification {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                        IBuffer tmp = cmd.SRN_Command();
                        var success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
                    }
                    if (file.isSendImagePkt)
                    {

                    }
                }
                else if (requestedOpCode == 0x2 && responseValue == 0x1)//0x600201
                {
                    if (file.isSendInitPkt)
                    {
                        Debug.WriteLine("[DEBUG] Init (.dat) Send Initial package (.dat): {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        var writeSuccessful = WriteBufferToBTLPCKAsync(file.init_package, cancellationTokenSource.Token);
                        
                        IBuffer tmp = cmd.GetCRC_Command();
                        var success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
                    }
                    if (file.isSendImagePkt && !file.istransfer)
                    {
                        Debug.WriteLine("[DEBUG] FW (.bin) Start Send Firmware Image: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        ValueChanged(MsgType.DFUStatus, "Start Send FirmwareImage");
                        ValueChanged(MsgType.intervalEndVal, file.sentTimes.ToString());
                    }
                }
                else if (requestedOpCode == 0x3 && responseValue == 0x1)//0x600301
                {
                    byte[] maxsize = new byte[4];
                    byte[] offset = new byte[4];
                    byte[] crc = new byte[4];

                    Array.Copy(data, 3, maxsize, 0, 4);
                    Array.Copy(data, 3, offset, 0, 4);
                    Array.Copy(data, 7, crc, 0, 4);
                    Array.Reverse(crc);
                    Debug.WriteLine("[DEBUG] CRC: "+ BitConverter.ToString(crc).Replace("-", ""));
                    
                    if (file.isSendInitPkt && !file.istransfer)
                    {
                        IBuffer tmp = cmd.Execute_Command();
                        var success = await WriteBufferToBTLCTLAsync(tmp, cancellationTokenSource.Token);
                    }
                    if (file.istransfer)
                    {
                        ValueChanged(MsgType.ImagePageStatus, "CRC get");
                    }
                }
                else if (requestedOpCode == 0x4 && responseValue == 0x1)//600401
                {
                    if (file.isSendInitPkt && !file.istransfer)
                    {
                        Debug.WriteLine("[DEBUG] Init (.dat) Tranfer packget done: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        file.isSendInitPkt = false;
                        StartUpdateImage();
                    }
                    if (file.istransfer)
                    {
                        ValueChanged(MsgType.ImagePageStatus, "execute done");
                    }
                }
                else
                {
                    ValueChanged(MsgType.ImagePageStatus, "Ack Error");
                    Debug.WriteLine("[DEBUG] ProcessDataAsync else: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                }
            }
            else if (opCode == 0x20 && data[1] == 0x2)
            {
                if (data[2] == 0x1) //0x200201
                {
                    Debug.WriteLine("[DEBUG] Name cmd ack done: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                }
                else
                {
                    Debug.WriteLine("[DEBUG] Name cmd err: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                }
            }
            return returnValues;
        }
        #endregion


        #region Progress Calculation
        private string CalculateProcessValue(int count) //Evaluate the sending progress
        {
            int value = file.interval * count;
            Debug.WriteLine("[DEBUG] FW (.bin) progress percentage = {0} Sending Time = {1}", value, count);
            if (value > 100) { value = 100; }
            if (value == 10) { value = 12; }
            return value.ToString();
        }
        
        public void UpdateProgressValue(int count)
        {
            ValueChanged(MsgType.UpdateValue, CalculateProcessValue(count));
        }
        #endregion


        private BluetoothLEDeviceDisplay FindBluetoothLEDeviceDisplay(string id)
        {
            foreach (BluetoothLEDeviceDisplay bleDeviceDisplay in KnownDevices)
            {
                if (bleDeviceDisplay.Id == id)
                {
                    return bleDeviceDisplay;
                }
            }
            return null;
        }
    }
}



