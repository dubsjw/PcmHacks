﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// This class encapsulates all code that is unique to the ScanTool MX interface.
    /// </summary>
    public class ScanToolDevice : ElmDevice
    {
        /// <summary>
        /// Device type for use in the Device Picker dialog box, and for internal comparisons.
        /// </summary>
        public new const string DeviceType = "ObdLink SX";
        
        /// <summary>
        /// Constructor.
        /// </summary>
        public ScanToolDevice(IPort port, ILogger logger) : base(port, logger)
        {
            // Both of these numbers could be slightly larger, but round numbers are easier to work with,
            // and these are only used with the Scantool SX interface anyhow. If we detect an AllPro
            // adapter we'll overwrite these values, see the Initialize method below.

            // Please keep the left side easy to read in hex. Then add 12 bytes for VPW overhead.
            this.MaxSendSize = 192 + 12;

            // The ScanTool SX will download 512kb in roughly 30 minutes at 500 bytes per read.
            // ScanTool reliability suffers at 508 bytes or more, so we're going with a number
            // that's round in base 10 rather than in base 2.
            this.MaxReceiveSize = 500 + 12;

            // This would need a firmware upgrade at the very least, and likely isn't even possible 
            // with current hardware.
            this.Supports4X = false;
        }

        /// <summary>
        /// This string is what will appear in the drop-down list in the UI.
        /// </summary>
        public override string GetDeviceType()
        {
            return DeviceType;
        }

        /// <summary>
        /// Confirm that we're actually connected to the right device, and initialize it.
        /// </summary>
        public override async Task<bool> Initialize()
        {
            this.Logger.AddDebugMessage("Determining whether " + this.ToString() + " is connected.");
            
            try
            {
                string stID = await this.SendRequest("ST I");                 // Identify (ScanTool.net)
                if (stID == "?")
                {
                    this.Logger.AddDebugMessage("This is not a ScanTool device.");
                    return false;
                }

                this.Logger.AddUserMessage("ScanTool device ID: " + stID);
            }
            catch (Exception exception)
            {
                this.Logger.AddDebugMessage("Unable to initalize " + this.ToString());
                this.Logger.AddDebugMessage(exception.ToString());
                return false;
            }

            return true;
        }
        
        /// <summary>
        /// Send a message, do not expect a response.
        /// </summary>
        public override async Task<bool> SendMessage(Message message)
        {
            byte[] messageBytes = message.GetBytes();
            
            StringBuilder builder = new StringBuilder();
            builder.Append("STPX H:");
            builder.Append(messageBytes[0].ToString("X2"));
            builder.Append(messageBytes[1].ToString("X2"));
            builder.Append(messageBytes[2].ToString("X2"));
            builder.Append(", R:0");
            builder.Append(", D:");

            for (int index = 3; index < messageBytes.Length; index++)
            {
                builder.Append(messageBytes[index].ToString("X2"));
            }

            builder.Append("\r");

            string sendCommand = builder.ToString();

            string sendMessageResponse = await this.SendRequest(sendCommand);
            if (!this.ProcessResponse(sendMessageResponse, "message content"))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to read an incoming message from the device.
        /// </summary>
        protected override async Task Receive()
        {
            try
            {
                string response = await this.ReadELMLine();
                this.ProcessResponse(response, "receive");

                if (this.ReceivedMessageCount == 0)
                {
                   // await this.ReceiveViaMonitorMode();
                }
            }
            catch (TimeoutException)
            {
                this.Logger.AddDebugMessage("Timeout during receive.");
                // await this.ReceiveViaMonitorMode();
            }
        }

        /// <summary>
        /// This doesn't actually work yet, but I like the idea...
        /// </summary>
        private async Task ReceiveViaMonitorMode()
        {
            try
            {
                // The code below is currently only supported by Scantool (not AllPro).
                string monitorResponse = await this.SendRequest("AT MA");
                this.Logger.AddDebugMessage("Response to AT MA 1: " + monitorResponse);

                if (monitorResponse != ">?")
                {
                    string response = await this.ReadELMLine();
                    this.ProcessResponse(monitorResponse, "receive via monitor");
                }
            }
            catch(TimeoutException)
            {
                this.Logger.AddDebugMessage("Timeout during receive via monitor mode.");
            }
            finally
            { 
                string stopMonitorResponse = await this.SendRequest("AT MA");
                this.Logger.AddDebugMessage("Response to AT MA 2: " + stopMonitorResponse);
            }
        }
    }
}