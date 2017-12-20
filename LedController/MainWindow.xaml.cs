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
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Reflection;
using System.Threading;
using System.Diagnostics;

namespace LedController
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        WS2812B ledDevice = new WS2812B(300);

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = ledDevice;
        }

        private void allOffBtn_Click(object sender, RoutedEventArgs e)
        {
            ledDevice.LedStripOff();
        }

        private void btnRotateCw_Click(object sender, RoutedEventArgs e)
        {
            ledDevice.LedStripRotate(true, (ushort)numRotate.Value);
        }

        private void btnRotateCcw_Click(object sender, RoutedEventArgs e)
        {
            ledDevice.LedStripRotate(false, (ushort)numRotate.Value);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ledDevice.Disconnect();
        }

        private void btnRandomSeed_Click(object sender, RoutedEventArgs e)
        {
            Random rnd = new Random();
            for (int i = 0; i < rnd.Next(0, 200); i++)
            {
                ledDevice.LedSetColor((ushort)rnd.Next(0, 300), (byte)rnd.Next(0, 256), (byte)rnd.Next(0, 256), (byte)rnd.Next(0, 256));
            }
        }

        private void btnRandomOff_Click(object sender, RoutedEventArgs e)
        {
            Random rnd = new Random();
            for (int i = 0; i < rnd.Next(0, 200); i++)
            {
                ledDevice.LedSetColor((ushort)rnd.Next(0, 300), 0, 0, 0);
            }
        }

        private void btnSetStrip_Click(object sender, RoutedEventArgs e)
        {
            ledDevice.LedStripSetColor(colorSelector.SelectedColor.Value.G, colorSelector.SelectedColor.Value.R, colorSelector.SelectedColor.Value.B);
        }

        private void btnSetLed_Click(object sender, RoutedEventArgs e)
        {
            ledDevice.LedSetColor((ushort)numLedId.Value, colorSelector.SelectedColor.Value.G, colorSelector.SelectedColor.Value.R, colorSelector.SelectedColor.Value.B);
        }

        private void btnSwapLed_Click(object sender, RoutedEventArgs e)
        {
            ledDevice.LedSwap((ushort)numSwapLed1.Value, (ushort)numSwapLed2.Value);
        }

      //  private Thread specialEffects;
        private ManualResetEvent specialEffectsIsRunning = new ManualResetEvent(false);

        private void btnRotateLoop_Click(object sender, RoutedEventArgs e)
        {
            if (specialEffectsIsRunning.WaitOne(0))
                specialEffectsIsRunning.Reset();
            else
            {
               specialEffectsIsRunning.Set();
               if (sender == btnStreamEffect)
                    (new Thread(streamEffectWorker)).Start();
               if (sender == btnChaseEffect)
                    (new Thread(chasingEffectWorker)).Start();
               //if (sender == btnFadeEffect)
                 //   (new Thread(fadeEffectWorker)).Start();
               if (sender == btnDestructEffect)
                    (new Thread(destructEffectWorker)).Start();
            }
        }

        private ushort ledCollderInc(ref ushort curVal)
        {
            curVal++;
            if (curVal > 299) curVal = 0;
            return curVal;
        }

        private ushort ledCollderDec(ref ushort curVal)
        {
            if (curVal == 0) curVal = 299;
            else curVal--;            
            return curVal;
        }

        private void streamEffectWorker()
        {
            int i = 0;
            int delay = 0;
            Random rnd = new Random();

            while (specialEffectsIsRunning.WaitOne(0))
            {
                ledDevice.LedStripRotate();
                System.Threading.Thread.Sleep(delay);
                i++;
                if ((i % 100) == 0) delay++;

                if ((i % rnd.Next(100, 10000)) == 0) { i = 0; delay = 0; }
            }
        }
        

        private void chasingEffectWorker()
        {
            Random rnd = new Random();
            int offset = 3;
            bool offsetinc = true;

            while (specialEffectsIsRunning.WaitOne(0))
            {
                ledDevice.LedStripSetColor((byte)rnd.Next(5, 25), 0, 0);

                for (int ledID = 0; ledID < 300; ledID++)
                {
                    if (ledID >= offset)                    
                        ledDevice.LedSetColor((ushort)(ledID - offset), 0, 0, 0);

                    ledDevice.LedSetColor((ushort)(ledID), 25, 25, 0);
                }
                
                ledDevice.LedStripSetColor(0, (byte)rnd.Next(5, 25), 0);

                for (int ledID = 299; ledID >= 0; ledID--)
                {
                    if (ledID < (300 - offset))
                        ledDevice.LedSetColor((ushort)(ledID + offset), 0, 0, 0);

                    ledDevice.LedSetColor((ushort)(ledID), 25, 25, 0);
                }

                offset += offsetinc ? 1 : -1;
                if (offset > 7) offsetinc = false;
                if (offset < 3) offsetinc = true;
            }
        }

        private void destructEffectWorker()
        {
            Random rnd = new Random();
            while (specialEffectsIsRunning.WaitOne(0))
            {
                //construct
                for (int i = 0; i < rnd.Next(1000, 2000); i++)
                {
                    ledDevice.LedSetColor((ushort)rnd.Next(0, 300), (byte)rnd.Next(0, 50), (byte)rnd.Next(0, 50), (byte)rnd.Next(0, 50));
                }

                //destruct
                for (int i = 0; i < rnd.Next(2000, 5000); i++)
                {
                    ledDevice.LedSetColor((ushort)rnd.Next(0, 300), 0, 0, 0);
                }
            }
        }

        /*
        private void fadeEffectWorker()
        {
            Random rnd = new Random();
            byte[] GRB = new byte[3];
            rnd.NextBytes(GRB);

            while (specialEffectsIsRunning.WaitOne(0))
            {                
                ledDevice.LedStripSetColor(GRB[0], GRB[1], GRB[2]);
                Thread.Sleep(50);                
            }
        }
          
        private void streamEffectWorker()
        {
            int i = 0;
            int delay = 0;
            Random rnd = new Random();

            ushort ledCollider = 0;
            Color[] tempColor = new Color[3];
            ushort[] tempId = new ushort[3];

            while (specialEffectsIsRunning.WaitOne(0))
            {

                tempColor[0] = ledDevice.ledStripState[ledCollider];
                tempId[0] = ledCollider;
                tempId[1] = ledCollider; ledCollderInc(ref tempId[1]);
                tempId[2] = tempId[1]; ledCollderInc(ref tempId[2]);
                tempColor[1] = ledDevice.ledStripState[tempId[1]];
                tempColor[2] = ledDevice.ledStripState[tempId[2]];

                ledDevice.LedSetColor(ledCollider, 0, 255, 0);
                Thread.Sleep(5);
                ledDevice.LedMove(1, ledCollider, ledCollderInc(ref ledCollider));
                ledDevice.LedSetColor(tempId[0], tempColor[0].G, tempColor[0].R, tempColor[0].B);  //put the original LED back
                Thread.Sleep(5);
                ledDevice.LedMove(1, ledCollider, ledCollderInc(ref ledCollider));
                ledDevice.LedSetColor(tempId[1], tempColor[1].G, tempColor[1].R, tempColor[1].B);  //put the original LED back
                Thread.Sleep(5);
                ledDevice.LedMove(1, ledCollider, ledCollderInc(ref ledCollider));
                ledDevice.LedSetColor(tempId[2], tempColor[2].G, tempColor[2].R, tempColor[2].B);  //put the original LED back
                Thread.Sleep(5);
                ledDevice.LedSetColor(ledCollider, 0, 0, 0);

                ledDevice.LedStripRotate();

                // System.Threading.Thread.Sleep(delay);
                i++;
                // if ((i % 100) == 0) { delay++; btnRandomSeed_Click(null, null); }
                if ((i % rnd.Next(50, 150)) == 0) { delay++; }

                if ((i % rnd.Next(100, 10000)) == 0) { i = 0; delay = 0; }
            }
        }
        */
    }

}
