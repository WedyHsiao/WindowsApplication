using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Windows.Storage.Streams;

namespace BT_FUNCTION
{
    public class Files
    {
        public bool isSendInitPkt;
        public bool isSendImagePkt;
        public bool isSendImagePkt_step1;
        public bool istransfer;
        public IBuffer init_package;
        public int init_package_Len;
        public int sendImageCount;
        public int firmwareImage_LastLen;
        public int sentTimes;
        public int interval = 0;
        public byte[][] firmwareImageSection { get; set; }

        public static IBuffer ToIBuffer(byte[] value)
        {
            if (value == null && value.Length == 0)
                throw new ArgumentException();
            var temp = new byte[value.Length];
            Array.Copy(value, 0, temp, 0, value.Length);
            using (DataWriter writer = new DataWriter())
            {
                writer.WriteBytes(temp);
                var buffer = writer.DetachBuffer();
                return buffer;
            }
        }

        private byte[][] ByteArrayToChunks(byte[] byteData, long BufferSize)
        {
            byte[][] chunks = byteData.Select((value, index) => new { PairNum = Math.Floor(index / (double)BufferSize), value }).GroupBy(pair => pair.PairNum).Select(grp => grp.Select(g => g.value).ToArray()).ToArray();
            return chunks;
        }

        public void Load_fwFile()
        {
            isSendInitPkt = false;
            isSendImagePkt = false;
            sendImageCount = 0;

            Assembly assembly = this.GetType().Assembly;
            string[] names = assembly.GetManifestResourceNames(); //Show the Resource Content

            Stream Initial_File = assembly.GetManifestResourceStream("BLE_OTA.Resources.nrf52832_xxaa.dat"); // Set the file as embedded resource

            int dat_size = (int)Initial_File.Length;
            init_package_Len = dat_size;
            byte[] init_data = new byte[dat_size];
            using (StreamReader reader = new StreamReader(Initial_File))
            {
                Initial_File.Read(init_data, 0, dat_size);
            }
            init_package = Files.ToIBuffer(init_data);
            Stream Binary_File = assembly.GetManifestResourceStream("BLE_OTA.Resources.nrf52832_xxaa.bin"); // Set the file as embedded resource

            int bin_size = (int)Binary_File.Length;
            byte[] firmwareImage = new byte[bin_size];
            using (StreamReader reader = new StreamReader(Binary_File))
            {
                Binary_File.Read(firmwareImage, 0, bin_size);

            }
            firmwareImageSection = ByteArrayToChunks(firmwareImage, 4096);
            sentTimes = firmwareImageSection.Length; // ?section
            interval = 100 / sentTimes;
            if (100 % sentTimes != 0)
            {
                interval++;
            }
            firmwareImage_LastLen = firmwareImageSection[sentTimes - 1].Length;
        }

    }

    public class CMD
    {
        #region DFU related command
        public IBuffer EnterDFU_Command() //Set device switch to bootloader 0x01
        {
            Debug.WriteLine("[DEBUG] DATA SENT EnterDFU_Command: 01");
            var temp = new byte[] { 0x1 };
            var buffer = Files.ToIBuffer(temp);
            return buffer;
        }
        public IBuffer SelectInit_Command()// Select DFU .dat Command 0x0601
        {
            Debug.WriteLine("[DEBUG] DATA SENT SelectInit_Command: 06-01");
            var temp = new byte[] { 0x6, 0x1 };
            var buffer = Files.ToIBuffer(temp);
            return buffer;
        }
        public IBuffer CreateInit_Command(int pcklen)// Create Init Packet Command 0x0101...
        {
            Debug.WriteLine("[DEBUG] DATA SENT CreateInit_Command: 01-01-");

            //var temp = new byte[] { 0x1, 0x1,0x8d,0x0,0x0,0x0 };

            byte[] lenbyte = new byte[4];
            lenbyte = BitConverter.GetBytes(pcklen);
            lenbyte.Reverse();
            var temp = new byte[] { 0x1, 0x1 };
            byte[] data = new byte[6];
            Array.Copy(temp, 0, data, 0, 2);
            Array.Copy(lenbyte, 0, data, 2, 4);

            var buffer = Files.ToIBuffer(data);
            return buffer;
        }
        public IBuffer SRN_Command()// Set Receipt notification 0x02
        {
            Debug.WriteLine("[DEBUG] DATA SENT SRN_Command: 02");
            var temp = new byte[] { 0x2, 0x0, 0x0 };
            var buffer = Files.ToIBuffer(temp);
            return buffer;
        }
        public IBuffer GetCRC_Command()// Get CRC response 0x03
        {
            Debug.WriteLine("[DEBUG] DATA SENT GetCRC_Command: 03");
            var temp = new byte[] { 0x3 };
            var buffer = Files.ToIBuffer(temp);
            return buffer;
        }
        public IBuffer Execute_Command()// Excute Command 0x04
        {
            Debug.WriteLine("[DEBUG] DATA SENT Execute_Command: 04");
            var temp = new byte[] { 0x4 };
            var buffer = Files.ToIBuffer(temp);
            return buffer;
        }
        public IBuffer SelectFW_Command()// Select DFU .bin Command 0x0602
        {
            Debug.WriteLine("[DEBUG] DATA SENT SelectFW_Command: 06-02");
            var temp = new byte[] { 0x6, 0x2 };
            var buffer = Files.ToIBuffer(temp);
            return buffer;
        }
        public IBuffer CreateFW_Command(int pcklen)// Create Firmware Packet Command 0x0102...
        {
            Debug.WriteLine("[DEBUG] DATA SENT CreateFW_Command: 01-02-");
            byte[] lenbyte = new byte[4];
            lenbyte = BitConverter.GetBytes(pcklen);
            lenbyte.Reverse();
            var temp = new byte[] { 0x1, 0x2 };
            byte[] data = new byte[6];
            Array.Copy(temp, 0, data, 0, 2);
            Array.Copy(lenbyte, 0, data, 2, 4);

            var buffer = Files.ToIBuffer(data);
            return buffer;
        }
        #endregion

    }
}
