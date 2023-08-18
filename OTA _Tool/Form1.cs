using System;
using System.Diagnostics;
using System.Globalization;

//Add Console File Record
using System.IO;
using System.Windows.Forms;

//Add Resource File for used
using BT_NORDIC;

namespace Nordic_OTA
{

    public partial class Form1 : Form
    {
        private delegate void UpdateUICallBack(string value, Control ctl);

        string cultureName;

        CultureInfo culture;
        BLE bluetooth;

        private bool enter_update = false;
        private bool Failed = false;

        private bool ImagePageCRCGet = false;
        private bool ImagePageExeDone = false;
        private bool Update_Progress_Done = false;
        private int sendImageCount = 0;
        private int sendImageMax = 0;
        int intervalEndVal = 0;

        public string FirmwareStr = "";
        public string SoftwareStr = "";
        public int BatteryValue;

        public Form1()
        {
            InitializeComponent();
            FileStream filestream = new FileStream("Log.txt", FileMode.Create);
            var streamwriter = new StreamWriter(filestream);
            streamwriter.AutoFlush = true;
            Console.SetOut(streamwriter);
            Console.SetError(streamwriter);

            cultureName = "en-US";
            culture = new CultureInfo(cultureName);

        }

        #region GUI control
        //Scan for Connect
        private void button1_Click_1(object sender, EventArgs e)
        {
            if (button1.Text == "Scan")
            {
                startscan();
            }

            if (button1.Text == "Update")
            {
                if (bluetooth.ControlCharacteristic == null) { return; }
                if (!radioButton1.Checked)
                {
                    label1.Text = "Start DFU";
                    bluetooth.EnterDFU();
                    Console.WriteLine("[USER_Control] Enter Legacy DFU: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                }
                else
                {
                    label1.Text = "Start Silent DFU";
                    bluetooth.StartDFU();
                    Console.WriteLine("[USER_Control] Enter Silent DFU: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                }
            }
            if (button1.Text == "Done")
            {
                Console.WriteLine("[USER_Control] Close the APP: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                //Wedy 0628 add to close the channel
                bluetooth.clearDFUBTStatus();
                bluetooth.clearBTStatus();

                //Wedy 0818 add for clean the BLE access issue after the reconnect when update finished
                bluetooth.DisconnectAsync();

                Close();
            }

        }

        private void UpdateUI(string value, Control ctl)
        {
            if (this.InvokeRequired)
            {
                UpdateUICallBack uu = new UpdateUICallBack(UpdateUI);
                this.Invoke(uu, value, ctl);
            }
            else { ctl.Text = value; }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e) //Wedy 0628 add to close the channel
        {
            Console.WriteLine("[USER_Control] Close the APP: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            bluetooth.clearDFUBTStatus();
            bluetooth.clearBTStatus();
        }

        private void Form1_Load(object sender, EventArgs e) //Wedy 0628 add to initial the service
        {
            bluetooth = new BLE();
        }
        #endregion
        
        private void startscan()
        {
            Console.WriteLine("[USER_Control] Startscan: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
            label1.Text = "Scanning...";

            //implement the target device name
            string Stylus = textBox1.Text;

            //bluetooth = new BLE(); //Wedy 0628 move the Load
            bluetooth.ValueChanged += Bluetooth_ValueChanged;

            bluetooth.setNamePen(Stylus);
            Console.WriteLine("[USER_Control] Target Device: " + Stylus);

            if (radioButton1.Checked) //Wedy June Modified
            {
                bluetooth.isSilentDFU = true;
            }

            //Check the paired device on system
            bluetooth.FindPairedDevice();
        }

        private void Bluetooth_ValueChanged(MsgType type, string str, byte[] data)
        {
            Console.WriteLine("ValueChanged :{0} : {1}", type.ToString(), str);

            switch (type)
            {
                case MsgType.intervalEndVal:
                    sendImageMax = Convert.ToInt32(str);
                    int interval = 100 / sendImageMax;
                    if (100 % sendImageMax != 0)
                    {
                        interval++;
                    }
                    intervalEndVal = interval * (sendImageMax - 1);
                    break;
                case MsgType.ConnectState:
                    if (str == "Accessing")
                    {
                        if (!enter_update)
                        {
                        
                        }
                    }
                    else if (str == "Aceess Pass")
                    {
                        if (!bluetooth.inConnect)
                        {
                            return;
                        }
                        else
                        {
                            //Wedy mark to enter the Device service information after connection
                            bluetooth.ReadDevice_Version();
                        }
                    }
                    else if (str == "No device")
                    {
                        string msg = "Failed";
                        UpdateUI(msg, label1);
                        UpdateUI("Done", button1);

                        Failed = true;

                    }
                    else if (str == "Multiple Devices")
                    {
                        string msg = "Failed";
                        UpdateUI(msg, label1);
                        UpdateUI("Done", button1);

                        Failed = true;
                    }
                    else if (str == "Find device")
                    {
                        string msg = "Find device";
                        UpdateUI(msg, label1);
                    }
                    else
                    {
                        Failed = true;
                        bluetooth.OnCancelPoll();
                        Console.WriteLine("Access fail and Disconnect..: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                        string msg = "Failed";
                        UpdateUI(msg, label1);
                        UpdateUI("Done", button1);
                    }
                    break;

                case MsgType.FWinfo:

                    FirmwareStr = str;

                    if (Failed)
                    {
                        Debug.WriteLine("Current BLE Version = " + str);
                    }
                    else
                    {
                        string msg = "BLE version " + str;
                        UpdateUI(msg, label5);
                    }
                    if (!Update_Progress_Done)
                    {
                        //Wedy: add condition for checking the battery level
                        bluetooth.ReadDevice_Battery();
                    }
                    break;

                case MsgType.SWinfo:

                    SoftwareStr = str;

                    if (Failed)
                    {
                        Debug.WriteLine("Current ASIC Version = " + str);
                        string msg = "NA";
                        UpdateUI(msg, label6);

                    }
                    else
                    {
                        string msg = "ASIC version " + str;
                        UpdateUI(msg, label6);
                    }

                    if (!Update_Progress_Done)
                    {
                        //Wedy: add condition for checking the battery level
                        bluetooth.ReadDevice_Battery();
                    }

                    break;

                case MsgType.BatteryStatus:

                    try
                    {
                        BatteryValue = Convert.ToInt16(str);
                    }
                    catch
                    {
                        BatteryValue = 0;
                    }

                    if (BatteryValue >= 30)
                    {
                        string msg = "Battery level " + str + "%";
                        UpdateUI(msg, label1);

                        //Wedy: enable the update function after access the service
                        enter_update = true;
                        UpdateUI("Update", button1);
                    }
                    else
                    {
                        string msg = "Battery below 30%";
                        UpdateUI(msg, label1);

                        Console.WriteLine("Battery Level Below 30%: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        UpdateUI("Done", button1);
                    }
                    break;

                case MsgType.DFUStatus:
                    if (str == "EnterBootloader")
                    {
                        Debug.WriteLine("[DEBUG] RECEIVED OTA ACK and START SCAN DFUtarg: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        Console.WriteLine("[EVENT] Recieved ACK for Enter Bootloader then Scan DFU Device: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        
                        //Start to look the DFU device adv
                        bluetooth.StartBleDeviceWatcher();
                    }
                    else if (str == "FIND_DFU_TARGET")
                    {
                        Console.WriteLine("[EVENT] Find DFU Device: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        bluetooth.RemoveConnectionStatusChanged();

                        var aa = bluetooth.RunDFUConnect();
                    }
                    else if (str == "Init_DFU")
                    {
                        Console.WriteLine("[EVENT] Start DFU Init Progress: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        bluetooth.StartDFU();
                    }
                    else if (str == "Start Send FirmwareImage")
                    {
                        string msg = "Start Sending";
                        UpdateUI(msg, label1);

                        Console.WriteLine("[EVENT] Start sending Image: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        bluetooth.StartSendingImage(sendImageCount);
                    }
                    else
                    {
                        if (str == "disconnect")
                        {
                            Console.WriteLine("[EVENT] Disconnect: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);
                        }

                        Failed = true;
                        bluetooth.OnCancelPoll();
                    }
                    break;
                case MsgType.UpdateValue:
                    if (str == "100")
                    {
                        bluetooth.isGetswVer = false;
                        bluetooth.isGetfwVer = false;
                        Update_Progress_Done = true;

                        UpdateUI("Done", button1);
                        
                        Console.WriteLine("[EVENT] End sending Image: {0}, {1:G}", DateTime.Now.ToString(culture), DateTime.Now.Kind);

                        string msg = "Progress: " + str;
                        UpdateUI(msg, label1);
                    }
                    else
                    {
                        string msg = "Progress: "+str;
                        UpdateUI(msg, label1);
                    }

                    break;
                case MsgType.ImagePageStatus:

                    if (str == "CRC get")
                    {
                        ImagePageCRCGet = true;
                    }
                    else if (str == "execute done")
                    {
                        ImagePageExeDone = true;
                    }
                    else
                    {
                        sendImageCount = 0;
                        ImagePageCRCGet = false;
                        ImagePageExeDone = false;
                        bluetooth.OnCancelPoll();
                    }
                    if (ImagePageCRCGet && ImagePageExeDone)
                    {
                        sendImageCount++;
                        bluetooth.UpdateProgressValue(sendImageCount - 1);

                        bluetooth.StartSendingImage(sendImageCount);
                        if (intervalEndVal < 100 && sendImageCount == sendImageMax)
                        {
                            bluetooth.UpdateProgressValue(sendImageCount);
                        }
                        ImagePageCRCGet = false;
                        ImagePageExeDone = false;
                    }
                    break;

                default:
                    break;
            }
        }
    }
}