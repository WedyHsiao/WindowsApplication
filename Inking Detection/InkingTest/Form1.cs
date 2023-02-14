using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InkingTest
{
    
    public partial class Form1 : Form
    {
        System.Timers.Timer Timer;
        int x = 0; int y = 0; int t; int z;
        bool mousemove = false;

        public Form1()
        {
            InitializeComponent();
        }

        private void panel1_MouseLeave(object sender, EventArgs e)
        {
            label1.Text = "Detecting...";
            mousemove = false;
            
            if (!mousemove)
            {
                t = 0;
                x++;
                textBox1.Text = x.ToString();
                Timer.Start();
            }
        }

        private void panel1_MouseMove(object sender, MouseEventArgs e)
        {
            label1.Text = "Detected";
            mousemove = true;
            
            if (mousemove)
            {
                textBox1.Text = x.ToString();
                Timer.Stop();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            textBox2.Text = "30";
            Timer = new System.Timers.Timer();
            Timer.Interval = 1000; //1000ms
            Timer.Elapsed += Timer_Elapsed;
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            int.TryParse(textBox2.Text, out z);

            Invoke(new Action(() =>
            {
                t++;
                textBox4.Text = t.ToString();
                if (t >= z)
                {
                    t = 0;
                    y++;
                    textBox3.Text = y.ToString();
                }
            }));
        }

        private void Reset_Click(object sender, EventArgs e)
        {
            Timer.Stop();
            textBox1.Text = null;
            textBox3.Text = null;
            textBox4.Text = null;
            label1.Text = "Detection Area";
            x = 0; y = 0; t = 0;
        }
    }
}
