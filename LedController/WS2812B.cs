using System;
using System.Windows.Media;
using System.Management;
using System.IO.Ports;
using System.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;   //needed for [callermembername]

//Using an STM32F103C8T6 to control individually addressable WS28128b LEDs

namespace LedController
{
    class WS2812B: INotifyPropertyChanged
    {
        private const string USB_DEVICE_DESCRIPTOR = "STMicroelectronics Virtual COM Port";
        private const int CONNECTION_MONITOR_DELAY = 200;  //ms 
        private const int SERIAL_READ_WRITE_TIMEOUT = 200; //ms
        private const int SERIAL_BAUD_RATE = 921600;

        private SerialPort comPort;
        private ManualResetEvent KeepRunning = new ManualResetEvent(true);  //used to control running and exiting of comPortWorker Thread.
        private AutoResetEvent responseReceived = new AutoResetEvent(false); //used for waiting for a response.
        private string deviceComPort = null;  //COM1, COM2, COM3 etc...
        public Color[] ledStripState;  //keeps track of the state of each LED on the stirp

        public ushort totalLedsOnTheStrip { get; private set; }

        private string _serialNumber = string.Empty;
        public string serialNumber
        {
            get { return _serialNumber; }
            private set
            {
                if (value != _serialNumber)
                {
                    _serialNumber = value;
                    OnPropertyChanged();                    
                }
            }
        }

        private bool _isConnected = false;
        public bool isConnected
        {
            get { return _isConnected; }
            private set
            {
                if (value != _isConnected)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                    if (value == false)
                    {
                        totalBytesRecv = 0;
                        totalBytesSent = 0;
                        serialNumber = string.Empty;
                    }                   
                }
            }
        }

        private int _totalBytesRecv = 0;
        public int totalBytesRecv
        {
            get { return _totalBytesRecv; }
            private set
            {
                if (value != _totalBytesRecv)
                {
                    _totalBytesRecv = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _totalBytesSent = 0;
        public int totalBytesSent
        {
            get { return _totalBytesSent; }
            private set
            {
                if (value != _totalBytesSent)
                {
                    _totalBytesSent = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _timeToWaitForResponseMs = 50;
        public int timeToWaitForResponseMs
        {
            get { return _timeToWaitForResponseMs;}
            set
            {
                if (value != _timeToWaitForResponseMs)
                {
                    _timeToWaitForResponseMs = value;
                    OnPropertyChanged();
                }
            }

        }

        public enum deviceCommands
        {
            cmdStripInit,         //initialize the strip to the count of LEDs.
            cmdStripOff,          //turn off entire strip
            cmdStripSetColor,     //set all LEDs in the strip to a specific color
            cmdStripIncColor,       //not implemented increments each LED on the strip's G, R and B value the supplied value
            cmdStripDecColor,      //not implemented decrements each LED on the strip's G, R and B value the supplied value
            cmdStripRotate,         //shift all LEDs in the strip in the specified direction by a specific number.
            cmdStripRotateSegment,  //shift a segment of LEDs in the strip in the specified direction by a specific number.
            cmdLedSwap,           //swap the values of two Leds
            cmdLedSetColor,       //sets an led at a selected location to a selected value
            cmdLedGetColor,        //not implemented gets the Led values at a selected location
            cmdLedIncColor,           //not implemented
            cmdLedDecColor,         //not implemented
            cmdLedCopy,         //copy one or more Leds to a new location - number of LEDs to copy, position of 1st LED to copy, position of 1st LED to paste
            cmdLedMove,
            cmdLedCheck,        //Check if the supplied parameters (color channel values) for a specific LED match.
            cmdStripSwapSegment,
        }

        public event PropertyChangedEventHandler PropertyChanged;
        internal void OnPropertyChanged([CallerMemberName] string propertName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertName));
        }

        public WS2812B(ushort ledCount)
        {
            serialNumber = string.Empty;
            isConnected = false;
            totalBytesRecv = totalBytesSent = 0;
            timeToWaitForResponseMs = 50;

            totalLedsOnTheStrip = ledCount;

            ledStripState = new Color[totalLedsOnTheStrip];
            ThreadPool.QueueUserWorkItem(comPortWorker);
        }

        public void Disconnect() //destructor
        {
            KeepRunning.Reset(); // this will cause the comPortWorker Thread to exit.
            KeepRunning.WaitOne();  //ComPortWorker will signal when all work is done.
        }

        private void comPortWorker(object o)
        {
            while (KeepRunning.WaitOne(0))
            {
                deviceComPort = findVirtualComPort();

                if ((deviceComPort != null) && (!isConnected)) //If the port is not null and device was not connected, this indicates that we are now establishing a new connection.
                {
                    try
                    {
                        comPort = new SerialPort(deviceComPort, SERIAL_BAUD_RATE, Parity.None, 8, StopBits.One);
                        comPort.ReadTimeout = SERIAL_READ_WRITE_TIMEOUT;
                        comPort.WriteTimeout = SERIAL_READ_WRITE_TIMEOUT;
                        comPort.NewLine = "\0";
                        comPort.Open();

                        totalBytesSent = 0;
                        totalBytesRecv = 0;
                        comPort.DataReceived += new SerialDataReceivedEventHandler(comDataReceived);
                        comPort.ErrorReceived += new SerialErrorReceivedEventHandler(comErrorReceived);
                        isConnected = true;
                        LedStripInit();
                    }
                    catch { isConnected = false; }
                }
                else if ((isConnected) & (deviceComPort == null))
                { //if it was last connected, but we've lost connection.
                    try
                    {
                        comPort.Close();
                        comPort.Dispose();
                    }
                    catch { }
                    isConnected = false;
                }

                Thread.Sleep(CONNECTION_MONITOR_DELAY);
            }

            // KeepRunning has been reset signaling a ready to exit condition
            if (comPort != null)
            {
                try
                {
                    if (comPort.IsOpen == true)
                    {
                        comPort.DiscardInBuffer();
                        comPort.DiscardOutBuffer();
                        comPort.Close();
                    }
                }
                catch { }

                if (comPort != null)
                {
                    comPort.Dispose();
                }
            }
           
            KeepRunning.Set();  //set it back to indicate that the thread has finished all its work.
        }

        private void comErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            isConnected = false;            
        }

        private void comDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // sp.ReadExisting
            string recvBuffer = string.Empty;
            try
            {
                while (comPort.BytesToRead > 0)
                {
                    recvBuffer = comPort.ReadLine();
                }
            }
            catch
            {
                recvBuffer += comPort.ReadExisting();
            }

            responseReceived.Set();
            totalBytesRecv += recvBuffer.Length;
        }

        private string findVirtualComPort()
        {
            ManagementScope connectionScope = new ManagementScope();
            SelectQuery serialQuery = new SelectQuery("SELECT * FROM Win32_SerialPort");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(connectionScope, serialQuery);

            try
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    if (item["Description"].ToString().Contains(USB_DEVICE_DESCRIPTOR))
                    {
                        serialNumber = item["PNPDeviceID"].ToString();
                        serialNumber = serialNumber.Substring(serialNumber.LastIndexOf('\\') + 1);
                        return item["DeviceID"].ToString();
                    }
                }
            }
            catch
            {
                /* Do Nothing */
            }

            return null;
        }

        /// <summary>
        /// Turns every LED on the strip off.
        /// </summary>        
        /// <returns>True if an acknowledgment is received within timeToWaitForResponseMs.</returns>
        public bool LedStripOff()
        {
            if (!sendPacket(deviceCommands.cmdStripOff)) return false;
            for (int i = 0; i < ledStripState.Length; i++)
            {
                ledStripState[i].G = 0;
                ledStripState[i].R = 0;
                ledStripState[i].B = 0;
            }
            return true;             
        }

        public bool LedStripRotate(bool rotateClockwise = true, ushort byNumberOfLeds = 1)
        {
            return LedStripRotateSegment(rotateClockwise, 0, totalLedsOnTheStrip, byNumberOfLeds);            
        }

        public bool LedSwap(ushort ledId1, ushort ledId2)
        {
            if (!sendPacket(deviceCommands.cmdLedSwap, ledId1, ledId2)) return false;
            Color temp = ledStripState[ledId1];
            ledStripState[ledId2] = ledStripState[ledId1];
            ledStripState[ledId1] = temp;
            return true;
        }

        public bool LedCopy(ushort numLedsToCopy, ushort copyFrom, ushort copyTo)
        {
            if (!sendPacket(deviceCommands.cmdLedCopy, numLedsToCopy, copyFrom, copyTo)) return false;
            Array.Copy(ledStripState, copyFrom, ledStripState, copyTo, numLedsToCopy);           
            return true;
        }

        /// <summary>
        /// Moves a range of LEDs starting at a specified location to a  another location. 
        /// NOTE: The source and destination must not overlap and must remain within the bounds of the LED strip.
        /// </summary>
        /// <param name="numLedsToMove">The number of LEDs to be moved.</param>
        /// <param name="moveFrom"></param>
        /// <param name="moveTo"></param>
        /// <returns>True if an acknowledgment is received within timeToWaitForResponseMs.</returns>
        public bool LedMove(ushort numLedsToMove, ushort moveFrom, ushort moveTo)
        {
            if (!sendPacket(deviceCommands.cmdLedMove, numLedsToMove, moveFrom, moveTo)) return false;
            Array.Copy(ledStripState, moveFrom, ledStripState, moveTo, numLedsToMove);
            for (ushort i = moveFrom; i < (moveFrom + numLedsToMove - 1); i++)
            {
                ledStripState[i].G = 0;
                ledStripState[i].R = 0;
                ledStripState[i].B = 0;
            }
            return true;
        }

        /// <summary>
        /// Sets the color of a single LED.
        /// </summary>
        /// <param name="ledId">The zero based index of the LED on the strip.</param>
        /// <param name="greenColor">Green channel value.</param>
        /// <param name="redColor">Red channel value.</param>
        /// <param name="blueColor">Blue channel value.</param>
        /// <returns>True if an acknowledgment is received within timeToWaitForResponseMs.</returns>
        public bool LedSetColor(ushort ledId, byte greenColor, byte redColor, byte blueColor)
        {
            if (!sendPacket(deviceCommands.cmdLedSetColor, ledId, greenColor, redColor, blueColor)) return false;
            ledStripState[ledId].G = greenColor;
            ledStripState[ledId].R = redColor;
            ledStripState[ledId].B = blueColor;
            return true;
        }

        /// <summary>
        /// Sets the entire strip (every LED) to a specific color.
        /// </summary>
        /// <param name="greenColor">Green channel value.</param>
        /// <param name="redColor">Red channel value.</param>
        /// <param name="blueColor">Blue channel value.</param>
        /// <returns>True if an acknowledgment is received within timeToWaitForResponseMs.</returns>
        public bool LedStripSetColor(byte greenColor, byte redColor, byte blueColor)
        {            
            if (!sendPacket(deviceCommands.cmdStripSetColor, greenColor, redColor, blueColor)) return false;
            for (ushort i = 0; i < ledStripState.Length; i++)
            {
                ledStripState[i].G = greenColor;
                ledStripState[i].R = redColor;
                ledStripState[i].B = blueColor;
            }
            return true;
        }

        public bool LedStripDecrementColor(byte incGreenBy, byte incRedBy, byte incBlueBy)
        {
            if (!sendPacket(deviceCommands.cmdStripDecColor, incGreenBy, incRedBy, incBlueBy)) return false;
            for (ushort i = 0; i < ledStripState.Length; i++)
            {
                ledStripState[i].G -= incGreenBy;
                ledStripState[i].R -= incRedBy;
                ledStripState[i].B -= incBlueBy;
            }
            return true;
        }

        public bool LedStripIncrementColor(byte incGreenBy, byte incRedBy, byte incBlueBy)
        {
            if (!sendPacket(deviceCommands.cmdStripIncColor, incGreenBy, incRedBy, incBlueBy)) return false;
            for (ushort i = 0; i < ledStripState.Length; i++)
            {
                ledStripState[i].G += incGreenBy;
                ledStripState[i].R += incRedBy;
                ledStripState[i].B += incBlueBy;
            }
            return true;
        }

        /// <summary>
        /// Decrements the color for the specified LED by the selected value.
        /// </summary>
        /// <param name="ledId">The zero based location of the LED on the stirp.</param>
        /// <param name="incGreenBy">The current value of Green channel will be decremented by this.</param>
        /// <param name="incRedBy">The current value of Red channel will be decremented by this.</param>
        /// <param name="incBlueBy">The current value of Blue channel will be decremented by this.</param>
        /// <returns>True if an acknowledgment is received within timeToWaitForResponseMs.</returns>
        public bool LedDecrementColor(ushort ledId, byte incGreenBy, byte incRedBy, byte incBlueBy)
        {
            if (!sendPacket(deviceCommands.cmdLedDecColor, ledId, incGreenBy, incRedBy, incBlueBy)) return false;
            ledStripState[ledId].G -= incGreenBy;
            ledStripState[ledId].R -= incRedBy;
            ledStripState[ledId].B -= incBlueBy;
            return true;
        }

        /// <summary>
        /// Increments the color for the specified LED by the selected value.
        /// </summary>
        /// <param name="ledId">The zero based location of the LED on the stirp.</param>
        /// <param name="incGreenBy">The current value of Green channel will be incremented by this.</param>
        /// <param name="incRedBy">The current value of Red channel will be incremented by this.</param>
        /// <param name="incBlueBy">The current value of Blue channel will be incremented by this.</param>
        /// <returns>True if an acknowledgment is received within timeToWaitForResponseMs.</returns>
        public bool LedIncrementColor(ushort ledId, byte incGreenBy, byte incRedBy, byte incBlueBy)
        {
            if (!sendPacket(deviceCommands.cmdLedIncColor, ledId, incGreenBy, incRedBy, incBlueBy)) return false;            
            ledStripState[ledId].G += incGreenBy;
            ledStripState[ledId].R += incRedBy;
            ledStripState[ledId].B += incBlueBy;            
            return true;
        }

        /// <summary>
        /// Tests if the local array ledStripState is in sync with the actual state of the control buffer on the device.
        /// </summary>
        /// <returns>True if the state of every LED on the strip matches with the local values.</returns>
        public bool isInSyncWithDevice()
        {
            for (ushort i = 0; i < ledStripState.Length; i++)
            {
                if (!LedCheck(i, ledStripState[i].G, ledStripState[i].R, ledStripState[i].B)) return false;
            }
            return true;
        }

        public bool LedStripRotateSegment(bool rotateClockwise, ushort startIndex, ushort endIndex, ushort byNumberOfLeds = 1)
        {
            if (rotateClockwise)
            {
                if (!sendPacket(deviceCommands.cmdStripRotateSegment, Convert.ToUInt16(rotateClockwise), startIndex, endIndex, byNumberOfLeds)) return false;
                for (int numberRotations = 0; numberRotations < byNumberOfLeds; numberRotations++)
                {
                    Color temp = ledStripState[startIndex];
                    for (int i = startIndex; i < (endIndex - 1); i++)
                        ledStripState[i] = ledStripState[i + 1];
                    ledStripState[endIndex - 1] = temp;
                }
                return true;
            }
            else
            {
                if (!sendPacket(deviceCommands.cmdStripRotateSegment, Convert.ToUInt16(rotateClockwise), startIndex, endIndex, byNumberOfLeds)) return false;
                for (int numberRotations = 0; numberRotations < byNumberOfLeds; numberRotations++)
                {
                    Color temp = ledStripState[endIndex - 1];
                    for (int i = endIndex - 1; i >= (startIndex + 1); i--)
                        ledStripState[i] = ledStripState[i - 1];
                    ledStripState[startIndex] = temp;
                }
                return true;
            }            
        }
        
        public bool LedStripSwapSegment(ushort indexFrom, ushort indexTo, ushort numToSwap, bool mirror = false)
        {
            if (!sendPacket(deviceCommands.cmdStripSwapSegment, indexFrom, indexTo, numToSwap, Convert.ToUInt16(mirror))) return false;
            if (!mirror)
            {
                for (ushort i = 0; i < numToSwap; i++)
                {
                    Color temp = ledStripState[indexTo + i];
                    ledStripState[indexTo + i] = ledStripState[indexFrom + i];
                    ledStripState[indexFrom + i] = temp;
                }
            }
            else
            {
                for (ushort i = 0; i < numToSwap; i++)
                {
                    Color temp = ledStripState[indexTo + numToSwap - 1 - i];
                    ledStripState[indexTo + numToSwap - 1 - i] = ledStripState[indexFrom + i];
                    ledStripState[indexFrom + i] = temp;
                }
            }         
            return true;
        }

        /// <summary>
        /// Checks if the local state of the specified LED matches with state on the device.
        /// NOTE: if the colors do not match, the device will not send a response.
        /// </summary>
        /// <returns>True if the values on the device match with the supplied value.</returns>
        private bool LedCheck(ushort ledId, byte localGreen, byte localRed, byte localBlue)
        {
            return sendPacket(deviceCommands.cmdLedCheck, ledId, localGreen, localRed, localBlue);
        }

        private bool LedStripInit()
        {
            return sendPacket(deviceCommands.cmdStripInit, (ushort)totalLedsOnTheStrip);
        }

        private bool sendPacket(deviceCommands command, params ushort[] data)
        {
            if (!isConnected) return false;
            string packetToSend = $"{(int)command}";
            foreach(ushort dataElement in data)
                packetToSend += $" {dataElement}";
           
            comPort.WriteLine(packetToSend);
            totalBytesSent += packetToSend.Length;
            if (!responseReceived.WaitOne(timeToWaitForResponseMs)) return false;

            return true;
        }
    }
}
